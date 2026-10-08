"""Regenerate golden data with the official Transformers processor and pinned Q4 ONNX.

Run using artifacts/embedding-comparison/.venv/Scripts/python.exe -X utf8.
"""
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
from PIL import Image
import torch
import transformers
from transformers import AutoProcessor

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
MODELS = ROOT / "artifacts/embedding-comparison/eg2"
torch.set_num_threads(1)
processor = AutoProcessor.from_pretrained(MODELS, local_files_only=True)
options = ort.SessionOptions()
options.intra_op_num_threads = 4
options.enable_cpu_mem_arena = False
text_session = ort.InferenceSession(str(MODELS / "onnx/model_q4.onnx"), options)
vision_session = ort.InferenceSession(str(MODELS / "onnx/vision_encoder_q4.onnx"), options)


def embed(inputs):
    feeds = {"input_ids": inputs["input_ids"], "attention_mask": inputs["attention_mask"]}
    for name in ("image_features", "video_features", "audio_features"):
        feeds[name] = np.empty((0, 512), dtype=np.float32)
    if "pixel_values" in inputs:
        features = vision_session.run(["image_features"], {
            "pixel_values": inputs["pixel_values"],
            "pixel_position_ids": inputs["image_position_ids"],
        })[0]
        feeds["image_features"] = features.reshape(-1, 512)
    vector = text_session.run(["sentence_embedding"], feeds)[0][0]
    return (vector / np.linalg.norm(vector)).tolist()


texts = [
    ("task: search result | query: Find a document about database backups.", 512),
    ("title: backup.txt | text: Nightly database backups are stored on a separate disk.", 8192),
    ("task: search result | query: \u6570\u636e\u5e93\u5907\u4efd\uff0c\u4fdd\u7559\u6700\u8fd1\u4e00\u5468\u3002", 512),
    ("title: none | text: Tabs\there\nnewlines\r\nand caf\u00e9 \U0001f600", 8192),
    ("task: search result | query: " + "hello " * 30, 24),
]
reference = {"transformers_version": transformers.__version__, "texts": [], "images": []}
for text, limit in texts:
    inputs = processor(text=text, truncation=True, max_length=limit, return_tensors="np")
    reference["texts"].append({
        "text": text, "limit": limit, "ids": inputs["input_ids"][0].tolist(), "vector": embed(inputs),
    })

for name, width, height in (("pattern.png", 97, 61), ("panorama.png", 1024, 9), ("downsample.png", 1073, 677)):
    y, x = np.mgrid[:height, :width]
    rgb = np.stack(((x * 7 + y * 11) % 256, (x * 3 + y * 17) % 256,
                    (x * 13 + y * 5) % 256), axis=-1).astype(np.uint8)
    image = Image.fromarray(rgb)
    image.save(HERE / name)
    inputs = processor(images=image, return_tensors="np")
    pixels = inputs["pixel_values"].reshape(-1)
    offsets = np.linspace(0, pixels.size - 1, 1000, dtype=int)
    positions = inputs["image_position_ids"][0]
    reference["images"].append({
        "file": name, "width": width, "height": height,
        "soft_tokens": len(inputs["input_ids"][0]) - 4,
        "ids": inputs["input_ids"][0].tolist(),
        "positions": positions.tolist(),
        "pixel_offsets": offsets.tolist(), "pixel_values": pixels[offsets].tolist(),
        "vector": embed(inputs),
    })
(HERE / "reference.json").write_text(json.dumps(reference, indent=2) + "\n", encoding="utf-8")
print(f"Generated reference with Transformers {transformers.__version__}")
