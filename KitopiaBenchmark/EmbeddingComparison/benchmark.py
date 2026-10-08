import argparse
import hashlib
import json
import math
from pathlib import Path
import platform
import subprocess
import sys
import threading
import time

import cv2
import numpy as np
import onnxruntime as ort
from PIL import Image
import polars as pl
import psutil


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ARTIFACTS = ROOT / "artifacts/embedding-comparison"
ASSETS = ROOT / "Kitopia.Desktop.Features/Assets"
TOKENIZER = HERE / "Tokenize/bin/Release/net10.0/Tokenize.dll"
SEED = 20261008


def prepare_dataset(output, resources, query_count, negatives, language):
    source = resources / ("data/scifact" if language == "en" else "data/T2Retrieval")
    queries = pl.read_parquet(next(source.glob("queries-*.parquet")))
    corpus = pl.read_parquet(next(source.glob("corpus-*.parquet")))
    if language == "en":
        queries = queries.rename({"_id": "id"}).select("id", "text")
        corpus = corpus.rename({"_id": "id"}).with_columns(
            pl.concat_str(["title", "text"], separator="\n").alias("text")
        ).select("id", "text")
        qrels = pl.read_csv(
            resources / "data/scifact-qrels/test.tsv", separator="\t",
            schema_overrides={"query-id": pl.String, "corpus-id": pl.String},
        ).rename({"query-id": "qid", "corpus-id": "pid"})
        queries = queries.filter(pl.col("id").is_in(qrels["qid"].to_list()))
    else:
        qrels = pl.read_parquet(next((resources / "data/T2Retrieval-qrels").glob("*.parquet")))
    queries = queries.sample(n=query_count, seed=SEED).sort("id")
    qrels = qrels.filter(pl.col("qid").is_in(queries["id"].to_list()) & (pl.col("score") > 0))
    positive_ids = set(qrels["pid"].to_list())
    positives = corpus.filter(pl.col("id").is_in(list(positive_ids)))
    if negatives == -1:
        documents = corpus.sort("id")
    else:
        distractors = corpus.filter(~pl.col("id").is_in(list(positive_ids))).sample(n=negatives, seed=SEED)
        documents = pl.concat([positives, distractors]).sort("id")
    relevant = {}
    for row in qrels.iter_rows(named=True):
        relevant.setdefault(row["qid"], []).append(row["pid"])
    if set(positives["id"].to_list()) != positive_ids:
        raise ValueError("Missing relevant documents in the source corpus")
    images = [
        {"id": path.name, "path": str(path.resolve())}
        for path in sorted((resources / "data/images").glob("*"))
    ]
    images.extend(
        {"id": path.name, "path": str(path.resolve())}
        for path in sorted((ROOT / "assets").glob("Kitopia*.png"))
    )
    image_query_file = "image-queries-en.json" if language == "en" else "image-queries.json"
    image_queries = json.loads((HERE / image_query_file).read_text(encoding="utf-8"))
    if not set().union(*(set(query["relevant"]) for query in image_queries)) <= {image["id"] for image in images}:
        raise ValueError("Missing an annotated image")
    dataset = {
        "seed": SEED,
        "language": language,
        "source": "BeIR/scifact (test)" if language == "en" else "C-MTEB/T2Retrieval",
        "full_corpus": negatives == -1,
        "random_distractors": None if negatives == -1 else negatives,
        "documents": documents.to_dicts(),
        "queries": [{**row, "relevant": relevant[row["id"]]} for row in queries.iter_rows(named=True)],
        "images": images,
        "image_queries": image_queries,
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / "dataset.json").write_text(json.dumps(dataset, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Dataset: {len(dataset['queries'])} queries, {len(dataset['documents'])} documents, "
          f"{len(images)} images, {len(image_queries)} image queries", flush=True)


def tokenize_current(texts, vocabulary, maximum_tokens):
    response = subprocess.run(
        ["dotnet", str(TOKENIZER), str(vocabulary), str(maximum_tokens)],
        input=json.dumps(texts, ensure_ascii=False), capture_output=True, text=True,
        encoding="utf-8", check=True, timeout=120,
    )
    return json.loads(response.stdout.lstrip("\ufeff"))


def prepare_feeds(model, dataset, resources, batch_size, cached_images=False):
    feeds = {"documents": [], "queries": [], "images": [], "image_queries": []}
    token_counts = {}
    if model == "current":
        for key, limit in (("documents", 256), ("queries", 128), ("image_queries", 52)):
            texts = [row.get("text", row.get("query")) for row in dataset[key]]
            if key == "queries":
                texts = ["为这个句子生成表示以用于检索相关文章：" + text for text in texts]
            vocabulary = ASSETS / ("ChineseClip/vocab.txt" if key == "image_queries" else "BGE_Model/tokenizer.json")
            tokens = tokenize_current(texts, vocabulary, limit)
            token_counts[key] = [len(value) for value in tokens]
            size = batch_size if key == "documents" else 1
            for start in range(0, len(tokens), size):
                batch = tokens[start:start + size]
                length = 52 if key == "image_queries" else max(map(len, batch))
                ids = np.zeros((len(batch), length), dtype=np.int64)
                mask = np.zeros_like(ids)
                for index, values in enumerate(batch):
                    ids[index, :len(values)] = values
                    mask[index, :len(values)] = 1
                feeds[key].append({"text": ids} if key == "image_queries" else {
                    "input_ids": ids, "attention_mask": mask, "token_type_ids": np.zeros_like(ids),
                })
        mean = np.array([0.48145466, 0.4578275, 0.40821073], dtype=np.float32)
        standard_deviation = np.array([0.26862954, 0.26130258, 0.27577711], dtype=np.float32)
        for row in ([] if cached_images else dataset["images"]):
            with Image.open(row["path"]) as image:
                rgb = np.array(image.convert("RGB"))
            rgb = cv2.resize(rgb, (224, 224), interpolation=cv2.INTER_LINEAR)
            pixels = (rgb.astype(np.float32) / 255 - mean) / standard_deviation
            feeds["images"].append({"image": np.ascontiguousarray(pixels.transpose(2, 0, 1)[None])})
    else:
        import torch
        from transformers import AutoProcessor

        torch.set_num_threads(1)
        processor = AutoProcessor.from_pretrained(resources / "eg2", local_files_only=True)
        for key, limit in (("documents", 256), ("queries", 128), ("image_queries", 128)):
            texts = [row.get("text", row.get("query")) for row in dataset[key]]
            prefix = "title: none | text: " if key == "documents" else "task: search result | query: "
            size = batch_size if key == "documents" else 1
            token_counts[key] = []
            for start in range(0, len(texts), size):
                batch = processor(
                    text=[prefix + text for text in texts[start:start + size]],
                    padding=True, truncation=True, max_length=limit, return_tensors="np",
                )
                token_counts[key].extend(np.asarray(batch["attention_mask"]).sum(axis=1).tolist())
                feeds[key].append(dict(batch))
        for row in ([] if cached_images else dataset["images"]):
            with Image.open(row["path"]) as image:
                batch = processor(images=image.convert("RGB"), return_tensors="np")
            feeds["images"].append(dict(batch))
    return feeds, token_counts


def retrieval_metrics(query_vectors, item_vectors, queries, items):
    scores = query_vectors @ item_vectors.T
    item_ids = [item["id"] for item in items]
    rows = []
    for query, values in zip(queries, scores):
        order = np.argsort(-values, kind="stable")
        relevant = set(query["relevant"])
        hits = [item_ids[index] in relevant for index in order]
        dcg = sum(hit / math.log2(rank + 2) for rank, hit in enumerate(hits[:10]))
        ideal = sum(1 / math.log2(rank + 2) for rank in range(min(10, len(relevant))))
        first_rank = next((rank + 1 for rank, hit in enumerate(hits) if hit), None)
        rows.append({
            "query": query.get("text", query.get("query")), "group": query.get("group", "text"),
            "relevant": sorted(relevant), "hit1": int(hits[0]),
            "recall5": sum(hits[:5]) / len(relevant), "recall10": sum(hits[:10]) / len(relevant),
            "ndcg10": dcg / ideal, "mrr10": 1 / first_rank if first_rank is not None and first_rank <= 10 else 0,
            "first_relevant_rank": first_rank,
            "top10": [{"id": item_ids[index], "score": float(values[index])} for index in order[:10]],
        })
    metrics = {
        key: float(np.mean([row[key] for row in rows]))
        for key in ("hit1", "recall5", "recall10", "ndcg10", "mrr10")
    }
    return metrics, rows


def worker(args):
    output = args.output.resolve()
    resources = args.resource_root.resolve() if args.resource_root else output
    dataset = json.loads((output / "dataset.json").read_text(encoding="utf-8"))
    cached_vectors = None
    cached_result = None
    if args.image_cache:
        cache = args.image_cache.resolve()
        cached_dataset = json.loads((cache / "dataset.json").read_text(encoding="utf-8"))
        if cached_dataset["images"] != dataset["images"]:
            raise ValueError("Image cache ordering or paths do not match this dataset")
        cached_result = json.loads((cache / f"{args.model}.json").read_text(encoding="utf-8"))
        if cached_result["model"] != args.model:
            raise ValueError("Image cache model does not match")
        with np.load(cache / f"{args.model}-vectors.npz") as archive:
            cached_vectors = archive["images"]
        if (cached_vectors.shape != (len(dataset["images"]), cached_result["image_dimensions"])
                or not np.isfinite(cached_vectors).all()
                or not np.allclose(np.linalg.norm(cached_vectors, axis=1), 1, atol=1e-5)):
            raise ValueError("Invalid cached image embeddings")
    cv2.setNumThreads(1)
    start = time.perf_counter()
    feeds, token_counts = prepare_feeds(args.model, dataset, resources, args.batch_size, args.image_cache is not None)
    preprocessing_seconds = time.perf_counter() - start
    process = psutil.Process()
    memory_before = process.memory_info()
    peak_memory = [memory_before.rss, memory_before.private]
    stop_sampling = threading.Event()

    def sample_memory():
        while not stop_sampling.wait(0.02):
            memory = process.memory_info()
            peak_memory[0] = max(peak_memory[0], memory.rss)
            peak_memory[1] = max(peak_memory[1], memory.private)

    sampler = threading.Thread(target=sample_memory, daemon=True)
    sampler.start()
    options = ort.SessionOptions()
    options.intra_op_num_threads = args.threads
    options.inter_op_num_threads = 1
    options.enable_cpu_mem_arena = False
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    if args.model == "current":
        paths = {
            "documents": ASSETS / "BGE_Model/quantized/model_quantized.onnx",
            "images": ASSETS / "ChineseClip/chinese-clip-rn50.img.int8.onnx",
            "image_queries": ASSETS / "ChineseClip/chinese-clip-rn50.txt.int8.onnx",
        }
    else:
        suffix = "_quantized" if args.model == "eg2-q8" else "_q4"
        paths = {
            "documents": resources / f"eg2/onnx/model{suffix}.onnx",
            "images": resources / f"eg2/onnx/vision_encoder{suffix}.onnx",
        }
    model_files = list(paths.values()) + [path.with_name(path.name + "_data") for path in paths.values()
                                        if path.with_name(path.name + "_data").exists()]
    fingerprints = {}
    for path in model_files:
        with path.open("rb") as model_file:
            fingerprints[str(path)] = hashlib.file_digest(model_file, "sha256").hexdigest()
    if cached_result is not None and cached_result["model_sha256"] != fingerprints:
        raise ValueError("Image cache model SHA256 fingerprints do not match")
    try:
        start = time.perf_counter()
        sessions = {key: ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
                    for key, path in paths.items() if not (args.image_cache and key == "images")}
        load_seconds = time.perf_counter() - start
        print(f"{args.model}: loaded in {load_seconds:.2f}s", flush=True)
        metadata = {key: {value.name: {"type": value.type, "shape": value.shape} for value in session.get_inputs()}
                    for key, session in sessions.items()}

        def infer(key, values):
            if args.model == "current":
                session = sessions["documents" if key == "queries" else key]
                output_name = "sentence_embedding" if key in ("queries", "documents") else session.get_outputs()[0].name
                vector = session.run([output_name], values)[0]
            else:
                values = dict(values)
                empty = np.empty((0, 512), dtype=np.float32)
                values.update(image_features=empty, video_features=empty, audio_features=empty)
                if key == "images":
                    vision = sessions["images"]
                    # The Python processor and this ONNX export use different names.
                    values["pixel_position_ids"] = values["image_position_ids"]
                    values["image_features"] = vision.run(
                        ["image_features"], {value.name: values[value.name] for value in vision.get_inputs()}
                    )[0]
                session = sessions["documents"]
                vector = session.run(
                    ["sentence_embedding"], {value.name: values[value.name] for value in session.get_inputs()}
                )[0]
            if not np.isfinite(vector).all():
                raise ValueError(f"Non-finite output from {args.model}, {key}")
            lengths = np.linalg.norm(vector, axis=1, keepdims=True)
            if (lengths < 1e-8).any():
                raise ValueError(f"Zero embedding from {args.model}, {key}")
            return vector / lengths

        vectors = {"images": cached_vectors} if cached_vectors is not None else {}
        timings = {}
        for key, inputs in feeds.items():
            if not inputs:
                timings[key] = {"count": len(vectors[key]), "cached": True}
                continue
            for _ in range(2):
                infer(key, inputs[0])
            elapsed = []
            batches = []
            for index, values in enumerate(inputs):
                start = time.perf_counter()
                batches.append(infer(key, values))
                elapsed.append((time.perf_counter() - start) * 1000)
                if index % 25 == 0:
                    print(f"{args.model}: {key} {index + 1}/{len(inputs)}", flush=True)
            vectors[key] = np.concatenate(batches)
            throughput_elapsed = sum(elapsed)
            if key in ("queries", "image_queries"):
                for _ in range(args.repeats - 1):
                    for values in inputs:
                        start = time.perf_counter()
                        infer(key, values)
                        elapsed.append((time.perf_counter() - start) * 1000)
            timings[key] = {
                "count": len(vectors[key]), "median_ms": float(np.median(elapsed)),
                "p95_ms": float(np.percentile(elapsed, 95)),
                "items_per_second": len(vectors[key]) / (throughput_elapsed / 1000),
                "total_seconds_first_pass": throughput_elapsed / 1000,
                "batch_size": args.batch_size if key == "documents" else 1,
            }
        text_metrics, text_rankings = retrieval_metrics(
            vectors["queries"], vectors["documents"], dataset["queries"], dataset["documents"]
        )
        image_metrics, image_rankings = retrieval_metrics(
            vectors["image_queries"], vectors["images"], dataset["image_queries"], dataset["images"]
        )
        image_groups = {}
        for group in sorted({row["group"] for row in image_rankings}):
            group_rows = [row for row in image_rankings if row["group"] == group]
            image_groups[group] = {"queries": len(group_rows), **{
                key: float(np.mean([row[key] for row in group_rows])) for key in image_metrics
            }}
        result = {
            "model": args.model, "ort_version": ort.__version__, "provider": "CPUExecutionProvider",
            "threads": args.threads, "cpu_arena": False, "model_files": [str(path) for path in model_files],
            "model_sha256": fingerprints,
            "image_cache": str(args.image_cache.resolve()) if args.image_cache else None,
            "loaded_sessions": list(sessions),
            "model_bytes": sum(path.stat().st_size for path in model_files),
            "load_seconds": load_seconds, "preprocessing_seconds": preprocessing_seconds,
            "text_dimensions": vectors["documents"].shape[1], "image_dimensions": vectors["images"].shape[1],
            "text": text_metrics, "image": image_metrics, "image_groups": image_groups,
            "timings_inference_only": timings,
            "memory": {
                "before_sessions_rss_mb": memory_before.rss / 2**20,
                "before_sessions_private_mb": memory_before.private / 2**20,
                "peak_rss_mb": peak_memory[0] / 2**20, "peak_private_mb": peak_memory[1] / 2**20,
                "peak_rss_increase_mb": (peak_memory[0] - memory_before.rss) / 2**20,
                "peak_private_increase_mb": (peak_memory[1] - memory_before.private) / 2**20,
            },
            "token_counts": {key: {"median": float(np.median(values)), "max": max(values)}
                             for key, values in token_counts.items()},
            "input_metadata": metadata, "text_rankings": text_rankings, "image_rankings": image_rankings,
        }
        (output / f"{args.model}.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        np.savez(output / f"{args.model}-vectors.npz", **vectors)
        print(json.dumps({"model": args.model, "text": text_metrics, "image": image_metrics}), flush=True)
    finally:
        stop_sampling.set()
        sampler.join()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ARTIFACTS)
    parser.add_argument("--resource-root", type=Path)
    parser.add_argument("--image-cache", type=Path)
    parser.add_argument("--language", choices=["zh", "en"], default="zh")
    parser.add_argument("--model", choices=["all", "current", "eg2-q8", "eg2-q4"], default="all")
    parser.add_argument("--queries", type=int, default=64)
    parser.add_argument("--negatives", type=int, default=512, help="Random distractors; -1 uses the full corpus")
    parser.add_argument("--threads", type=int, default=8)
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--worker", action="store_true")
    args = parser.parse_args()
    if args.worker:
        worker(args)
        return
    output = args.output.resolve()
    resources = args.resource_root.resolve() if args.resource_root else output
    prepare_dataset(output, resources, args.queries, args.negatives, args.language)
    environment = {
        "platform": platform.platform(), "python": platform.python_version(),
        "logical_cpus": psutil.cpu_count(), "ram_gb": psutil.virtual_memory().total / 2**30,
        "seed": SEED, "queries": args.queries, "random_distractors": args.negatives,
        "cpu": subprocess.check_output(
            ["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"], text=True
        ).strip(),
    }
    (output / "environment.json").write_text(json.dumps(environment, indent=2), encoding="utf-8")
    models = ("current", "eg2-q8", "eg2-q4") if args.model == "all" else (args.model,)
    for model in models:
        command = [
            sys.executable, str(Path(__file__).resolve()), "--worker", "--model", model,
            "--output", str(output), "--resource-root", str(resources), "--threads", str(args.threads),
            "--batch-size", str(args.batch_size), "--repeats", str(args.repeats),
        ]
        if args.image_cache:
            command.extend(["--image-cache", str(args.image_cache.resolve())])
        subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
