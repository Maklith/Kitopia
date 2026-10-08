import argparse
import hashlib
import json
from pathlib import Path
import time

import requests


MODEL = "onnx-community/embeddinggemma-2-ONNX"
REVISION = "daa72c51243991dfcaf9f9137d2c573d8f7790c0"
ENDPOINT = "https://hf-mirror.com"
ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "artifacts/embedding-comparison"
PHOTO_FILES = [
    "airport.jpg", "astronaut.png", "beach.png", "beetle.png",
    "bread_small.png", "butterfly.jpg", "cats.jpg", "city-streets.jpg",
    "corgi.jpg", "football-match.jpg", "handwriting.jpg", "handwritten-math.jpg",
    "house.jpg", "invoice.png", "invoice-with-table.png", "moraine-lake.png",
    "new-york.jpg", "nougat_paper.png", "pikachu.png", "portrait-of-woman_small.jpg",
    "pubtables.png", "quadratic_formula.png", "receipt.png", "sam-car_small.png",
    "savanna.jpg", "schedule.png", "scientific_publication.png", "tiger.jpg",
    "weather-events-diagram.png", "young-man-standing-and-leaning-on-car.jpg",
]


def download(url, target, expected_size=None, expected_sha=None):
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and (expected_size is None or target.stat().st_size == expected_size):
        with target.open("rb") as cached:
            valid = expected_sha is None or hashlib.file_digest(cached, "sha256").hexdigest() == expected_sha
        if valid:
            print(f"Cached {target.name}", flush=True)
            return
    temporary = target.with_suffix(target.suffix + ".part")
    for attempt in range(3):
        try:
            print(f"Downloading {target.name}", flush=True)
            with requests.get(url, stream=True, timeout=(20, 120)) as response:
                response.raise_for_status()
                digest = hashlib.sha256()
                received = 0
                last_progress = time.monotonic()
                with temporary.open("wb") as output:
                    for chunk in response.iter_content(1024 * 1024):
                        output.write(chunk)
                        digest.update(chunk)
                        received += len(chunk)
                        if time.monotonic() - last_progress > 10:
                            print(f"  {target.name}: {received / 1e6:.1f} MB", flush=True)
                            last_progress = time.monotonic()
                if expected_size is not None and received != expected_size:
                    raise ValueError(f"Wrong size for {target}: {received} != {expected_size}")
                if expected_sha is not None and digest.hexdigest() != expected_sha:
                    raise ValueError(f"SHA256 mismatch for {target}")
            temporary.replace(target)
            return
        except (requests.RequestException, ValueError) as error:
            if attempt == 2:
                raise
            print(f"Retry: {type(error).__name__}", flush=True)
            time.sleep(2)


def repository_files(repo, kind="models", revision="main"):
    response = requests.get(f"{ENDPOINT}/api/{kind}/{repo}", timeout=30)
    response.raise_for_status()
    commit = response.json()["sha"] if revision == "main" else revision
    response = requests.get(
        f"{ENDPOINT}/api/{kind}/{repo}/tree/{commit}?recursive=true", timeout=30
    )
    response.raise_for_status()
    return commit, response.json()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--only", choices=["models", "data", "data-en", "all"], default="all")
    args = parser.parse_args()
    output = args.output.resolve()
    manifest = {"model": MODEL, "model_revision": REVISION, "sources": []}
    if args.only in ("models", "all"):
        _, files = repository_files(MODEL, revision=REVISION)
        wanted = {
            f"onnx/{component}{suffix}.onnx{external}"
            for component in ("model", "vision_encoder")
            for suffix in ("_quantized", "_q4")
            for external in ("", "_data")
        }
        for entry in files:
            name = entry["path"]
            if entry["type"] != "file" or not (name in wanted or "/" not in name and name.endswith(".json")):
                continue
            download(
                f"{ENDPOINT}/{MODEL}/resolve/{REVISION}/{name}",
                output / "eg2" / name,
                entry["size"], entry.get("lfs", {}).get("oid"),
            )
    if args.only in ("data", "all"):
        for repo in ("C-MTEB/T2Retrieval", "C-MTEB/T2Retrieval-qrels"):
            revision, files = repository_files(repo, "datasets")
            manifest["sources"].append({"dataset": repo, "revision": revision})
            for entry in files:
                if entry["type"] == "file" and entry["path"].endswith(".parquet"):
                    download(
                        f"{ENDPOINT}/datasets/{repo}/resolve/{revision}/{entry['path']}",
                        output / "data" / repo.split("/")[-1] / Path(entry["path"]).name,
                        entry["size"], entry.get("lfs", {}).get("oid"),
                    )
        repo = "Xenova/transformers.js-docs"
        revision, files = repository_files(repo, "datasets")
        manifest["sources"].append({"dataset": repo, "revision": revision})
        entries = {entry["path"]: entry for entry in files}
        for name in PHOTO_FILES:
            entry = entries[name]
            download(
                f"{ENDPOINT}/datasets/{repo}/resolve/{revision}/{name}",
                output / "data/images" / name, entry["size"], entry.get("lfs", {}).get("oid"),
            )
    if args.only == "data-en":
        for repo in ("BeIR/scifact", "BeIR/scifact-qrels"):
            revision, files = repository_files(repo, "datasets")
            manifest["sources"].append({"dataset": repo, "revision": revision})
            for entry in files:
                name = entry["path"]
                if entry["type"] == "file" and (name.endswith(".parquet") or name == "test.tsv"):
                    download(
                        f"{ENDPOINT}/datasets/{repo}/resolve/{revision}/{name}",
                        output / "data" / repo.split("/")[-1] / Path(name).name,
                        entry["size"], entry.get("lfs", {}).get("oid"),
                    )
    output.mkdir(parents=True, exist_ok=True)
    (output / f"manifest-{args.only}.json").write_text(
        json.dumps(manifest, indent=2), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
