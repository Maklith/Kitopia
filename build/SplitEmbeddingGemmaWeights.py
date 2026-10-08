"""Repack the pinned Q4 ONNX export into native external-data shards.

Requires onnx==1.22.0. Tensor bytes and computation graphs are preserved.
"""
import argparse
from contextlib import ExitStack
import hashlib
import json
from pathlib import Path

import onnx


SHARD_BYTES = 32 * 1024 * 1024
GITHUB_BYTES = 100 * 1024 * 1024
SOURCES = {
    "model_q4.onnx": {
        "graph_sha256": "f9eeba97acddf139b8ee2ddf04bc30dceafa88de93fadf74d7644e0d61a477a9",
        "data_sha256": "c3975f2d1ab7a1878ae31a7d7a9b7804a827aff3800b60dfceafce21cac3df49",
    },
    "vision_encoder_q4.onnx": {
        "graph_sha256": "7ea284226d4938f0ad921ab091f1d80a9ca699aa802984ef5cd5eec4f4761d96",
        "data_sha256": "0a9d6c927334f152a33dd90874f65d6ea5228999abe6a450d3f7813677fa704c",
    },
}


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-directory", type=Path, required=True)
parser.add_argument("--output-directory", type=Path,
                    default=Path(__file__).resolve().parents[1] / "Kitopia.Desktop.Features/Assets/EmbeddingGemma2/onnx")
args = parser.parse_args()
if args.source_directory.resolve() == args.output_directory.resolve():
    parser.error("Source and output directories must differ.")

# Verify both upstream graphs and their external data before producing any output.
for name, expected in SOURCES.items():
    if sha256(args.source_directory / name) != expected["graph_sha256"]:
        raise ValueError(f"The graph does not match the pinned upstream export: {name}")
    if sha256(args.source_directory / (name + "_data")) != expected["data_sha256"]:
        raise ValueError(f"The weights do not match the pinned upstream export: {name}")

args.output_directory.mkdir(parents=True, exist_ok=True)
manifest = {"upstream_revision": "daa72c51243991dfcaf9f9137d2c573d8f7790c0", "models": {}}
for name, expected in SOURCES.items():
    model = onnx.load_model(args.source_directory / name, load_external_data=False)
    tensors = [tensor for tensor in model.graph.initializer if tensor.external_data]
    tensors.sort(key=lambda tensor: int(dict((item.key, item.value) for item in tensor.external_data)["offset"]))
    shards = []
    small_output = None
    upstream_ranges = {}
    with ExitStack() as stack:
        source = stack.enter_context((args.source_directory / (name + "_data")).open("rb"))
        for tensor in tensors:
            metadata = {item.key: item for item in tensor.external_data}
            if metadata["location"].value != name + "_data":
                raise ValueError(f"Unexpected external-data location: {tensor.name}")
            offset, length = int(metadata["offset"].value), int(metadata["length"].value)
            if length >= GITHUB_BYTES:
                raise ValueError(f"Tensor exceeds GitHub's file limit: {tensor.name}")
            upstream_ranges[tensor.name] = (offset, length)
            if length > SHARD_BYTES or small_output is None or small_output.tell() + length > SHARD_BYTES:
                shard_name = f"{name}_data.part{len(shards) + 1:03d}"
                output = stack.enter_context((args.output_directory / shard_name).open("wb"))
                shards.append(shard_name)
                if length <= SHARD_BYTES:
                    small_output = output
            else:
                output = small_output
            metadata["location"].value = Path(output.name).name
            metadata["offset"].value = str(output.tell())
            source.seek(offset)
            remaining = length
            while remaining:
                buffer = source.read(min(1024 * 1024, remaining))
                if not buffer:
                    raise EOFError(f"Unexpected end of weights: {tensor.name}")
                output.write(buffer)
                remaining -= len(buffer)
        onnx.save_model(model, args.output_directory / name)

    # Check every rewritten range byte-for-byte against the original tensor.
    rewritten = onnx.load_model(args.output_directory / name, load_external_data=False)
    with ExitStack() as stack:
        source = stack.enter_context((args.source_directory / (name + "_data")).open("rb"))
        files = {shard: stack.enter_context((args.output_directory / shard).open("rb")) for shard in shards}
        for tensor in rewritten.graph.initializer:
            if not tensor.external_data:
                continue
            metadata = {item.key: item.value for item in tensor.external_data}
            offset, remaining = upstream_ranges[tensor.name]
            source.seek(offset)
            target = files[metadata["location"]]
            target.seek(int(metadata["offset"]))
            if int(metadata["length"]) != remaining:
                raise ValueError(f"Tensor length changed: {tensor.name}")
            while remaining:
                size = min(1024 * 1024, remaining)
                if source.read(size) != target.read(size):
                    raise ValueError(f"Tensor bytes changed: {tensor.name}")
                remaining -= size

    original = onnx.load_model(args.source_directory / name, load_external_data=False)
    for source_tensor, target_tensor in zip(original.graph.initializer, rewritten.graph.initializer, strict=True):
        del target_tensor.external_data[:]
        target_tensor.external_data.extend(source_tensor.external_data)
    if rewritten.SerializeToString() != original.SerializeToString():
        raise ValueError(f"The computation graph changed: {name}")

    manifest["models"][name] = {
        "upstream_graph_sha256": expected["graph_sha256"],
        "upstream_data_sha256": expected["data_sha256"],
        "graph_sha256": sha256(args.output_directory / name),
        "verified_external_tensors": len(tensors),
        "shards": [{"file": shard, "bytes": (args.output_directory / shard).stat().st_size,
                    "sha256": sha256(args.output_directory / shard)} for shard in shards],
    }
    for shard in manifest["models"][name]["shards"]:
        print(f"{shard['file']}: {shard['bytes'] / 1024**2:.2f} MiB")

(args.output_directory / "shards.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="ascii")
