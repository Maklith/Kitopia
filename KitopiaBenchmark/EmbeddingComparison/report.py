import argparse
import json
from pathlib import Path

import numpy as np


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "artifacts/embedding-comparison")
    args = parser.parse_args()
    output = args.output.resolve()
    models = ["current", "eg2-q8", "eg2-q4"]
    results = {model: json.loads((output / f"{model}.json").read_text(encoding="utf-8")) for model in models}
    dataset = json.loads((output / "dataset.json").read_text(encoding="utf-8"))
    environment = json.loads((output / "environment.json").read_text(encoding="utf-8"))
    language = "English" if dataset.get("language") == "en" else "Chinese"
    full_corpus = dataset.get("full_corpus", False)
    cached_images = any(result.get("image_cache") for result in results.values())
    candidate_description = "full corpus" if full_corpus else (
        f"{dataset['random_distractors']} random distractors plus all query positives"
    )
    rng = np.random.default_rng(20261008)
    comparison = {"paired_differences": {}, "q4_q8_vectors": {}}
    lines = [
        "# Embedding comparison results",
        "",
        f"CPU: {environment['cpu']}; RAM: {environment['ram_gb']:.1f} GiB; "
        f"ONNX Runtime {results['current']['ort_version']}, CPU provider, 8 threads, CPU arena disabled.",
        "",
        f"Text: {dataset['source']}, {len(dataset['queries'])} {language} queries, "
        f"{len(dataset['documents'])} candidates ({candidate_description}). "
        f"Images: {len(dataset['images'])} candidates, {len(dataset['image_queries'])} manually labelled {language} queries.",
        "",
        "## Retrieval",
        "",
        "| Model | Text Hit@1 | Text Recall@10 | Text nDCG@10 | Image Hit@1 | Image Recall@10 |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
    ]
    for model, result in results.items():
        text, image = result["text"], result["image"]
        lines.append(f"| {model} | {text['hit1']:.1%} | {text['recall10']:.1%} | {text['ndcg10']:.4f} "
                     f"| {image['hit1']:.1%} | {image['recall10']:.1%} |")
    lines.extend([
        "", "### Image Groups", "",
        "| Model | Photos Hit@1 (31) | Visual documents Hit@1 (11) | Screenshots Hit@1 (8) |",
        "| --- | ---: | ---: | ---: |",
    ])
    for model, result in results.items():
        groups = result["image_groups"]
        lines.append(f"| {model} | {groups['photo']['hit1']:.1%} | {groups['document']['hit1']:.1%} "
                     f"| {groups['screenshot']['hit1']:.1%} |")
    lines.extend([
        "", "## CPU Performance", "",
        "Inference only, after warm-up. Query medians use three passes. " + (
            "Image vectors are reused from the Chinese run after checking image ordering, paths and model SHA256. "
            "Image encoders are not loaded or timed in this run."
            if cached_images else "Image timings include both EG2 graphs."
        ),
        "",
        "| Model | Weight files (MiB) | Load (s) | Text query median/p95 (ms) | Image query median/p95 (ms) | Documents/s (batch 8) | Image median (ms) |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: |",
    ])
    for model, result in results.items():
        timings = result["timings_inference_only"]
        query, image_query = timings["queries"], timings["image_queries"]
        image_time = "cached" if timings["images"].get("cached") else f"{timings['images']['median_ms']:.2f}"
        lines.append(f"| {model} | {result['model_bytes'] / 2**20:.1f} | {result['load_seconds']:.2f} "
                     f"| {query['median_ms']:.2f}/{query['p95_ms']:.2f} "
                     f"| {image_query['median_ms']:.2f}/{image_query['p95_ms']:.2f} "
                     f"| {timings['documents']['items_per_second']:.2f} | {image_time} |")
    lines.extend([
        "", "### Memory", "",
        "All loaded sessions stay resident during measurement. " + (
            "With cached images, the baseline loads BGE and the CLIP text encoder; EG2 loads its text graph. "
            "Memory figures exclude the vision encoder and cannot be directly compared with the Chinese run."
            if cached_images else "The baseline contains BGE and both CLIP encoders; EG2 contains text and vision."
        ),
        "",
        "| Model | Absolute peak RSS (MiB) | Peak RSS increase after loading/inference (MiB) | Peak private increase (MiB) |",
        "| --- | ---: | ---: | ---: |",
    ])
    for model, result in results.items():
        memory = result["memory"]
        lines.append(f"| {model} | {memory['peak_rss_mb']:.1f} | {memory['peak_rss_increase_mb']:.1f} "
                     f"| {memory['peak_private_increase_mb']:.1f} |")
    lines.extend([
        "", "## Paired Uncertainty", "",
        "10,000 paired bootstrap resamples of queries; 95% percentile intervals. "
        "Positive differences favor the first model. These intervals describe only this diagnostic sample.",
        "",
        "| Pair | Text nDCG@10 difference [95% CI] | Image Hit@1 difference [95% CI] |",
        "| --- | ---: | ---: |",
    ])
    for candidate, baseline in (("eg2-q8", "current"), ("eg2-q4", "current"), ("eg2-q4", "eg2-q8")):
        pair = f"{candidate} - {baseline}"
        comparison["paired_differences"][pair] = {}
        cells = []
        for task, metric in (("text", "ndcg10"), ("image", "hit1")):
            differences = np.array([row[metric] for row in results[candidate][f"{task}_rankings"]])
            differences -= np.array([row[metric] for row in results[baseline][f"{task}_rankings"]])
            resamples = rng.integers(0, len(differences), (10000, len(differences)))
            low, high = np.percentile(differences[resamples].mean(axis=1), [2.5, 97.5])
            comparison["paired_differences"][pair][task] = {
                "metric": metric, "difference": float(differences.mean()),
                "ci95": [float(low), float(high)],
            }
            cells.append(f"{differences.mean():+.4f} [{low:+.4f}, {high:+.4f}]")
        lines.append(f"| {pair} | {' | '.join(cells)} |")
    lines.extend([
        "", "## Q4 Versus Q8", "",
        "Vector similarity measures quantization drift, not retrieval accuracy. Q8 is the reference here, not FP32.",
        "",
        "| Workload | Mean cosine | Minimum cosine |",
        "| --- | ---: | ---: |",
    ])
    with np.load(output / "eg2-q8-vectors.npz") as q8, np.load(output / "eg2-q4-vectors.npz") as q4:
        for key in q8.files:
            cosine = np.sum(q8[key] * q4[key], axis=1)
            comparison["q4_q8_vectors"][key] = {"mean_cosine": float(cosine.mean()), "min_cosine": float(cosine.min())}
            lines.append(f"| {key} | {cosine.mean():.6f} | {cosine.min():.6f} |")
    lines.extend([
        "", "## Limits", "",
        (
            "- English text uses all 300 SciFact test claims and all 5,183 scientific abstracts. "
            "This measures scientific English retrieval, not general English accuracy. "
            "Titles and abstracts are concatenated for every model. The input limits below differ from "
            "standard BEIR model recipes, so these are application-oriented scores."
            if full_corpus and language == "English" else
            "- This is a candidate-subset text evaluation, not the full 118,605-document benchmark. "
            "Random distractors can be easy and cause ceiling effects."
        ),
        "- Images are a small manually labelled fixture set; the same fixture source appears in "
        "Transformers.js examples. Results do not establish general image retrieval quality.",
        *([
            "- English image queries are translations of the 50 Chinese queries, with identical relevance labels. "
            "The eight screenshot queries are English queries against Chinese application UI (cross-language retrieval). "
            "The current baseline retains production casing, tokenization and the Chinese BGE query instruction."
        ] if language == "English" else []),
        "- Image retrieval compares native visual embeddings, with no OCR text or file-name ranking. "
        "Kitopia's existing OCR+BGE branch can improve screenshot results beyond the CLIP scores here.",
        "- Text documents are limited to 256 tokens and queries to 128, following the current application. "
        "Each document is encoded once with truncation, without Kitopia's whole-file chunk averaging. "
        "Different tokenizers fit different amounts of text; EG2's 8K context advantage is not evaluated.",
        "- Timings exclude decoding and preprocessing. The memory increase is more comparable than "
        "absolute process memory because EG2 preprocessing imports PyTorch and retains larger tensors.",
        "- CPU results do not establish CUDA, DirectML, OpenVINO or ARM performance. "
        "Native .NET execution and mixed-input plugin integration still need separate validation.",
        "- Current baseline includes the vocabulary whitespace fix discovered while preparing the test.",
        "", "Raw vectors, source revisions, SHA256 fingerprints, every query's top 10 results, "
        "input tensor metadata and environment details are saved alongside this report.", "",
    ])
    (output / "comparison.json").write_text(json.dumps(comparison, indent=2), encoding="utf-8")
    (output / "report.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
