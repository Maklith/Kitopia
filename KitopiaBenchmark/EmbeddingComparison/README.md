# Embedding comparison

Reproducible Windows CPU comparison of the current bundled BGE-small-zh-v1.5
INT8 / Chinese-CLIP RN50 INT8 models and EmbeddingGemma 2 ONNX Q8 / Q4.
This is an isolated benchmark; it does not change application models or indexes.

## Run

Run from the repository root with .NET 10 and Python 3.13. The virtual environment,
downloads, sampled data, vectors and detailed results live in the ignored
`artifacts/embedding-comparison/` directory.

```powershell
python -m venv artifacts/embedding-comparison/.venv --system-site-packages
artifacts/embedding-comparison/.venv/Scripts/python.exe -m pip install -r KitopiaBenchmark/EmbeddingComparison/requirements.txt
dotnet build KitopiaBenchmark/EmbeddingComparison/Tokenize/Tokenize.csproj -c Release
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/prepare.py
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/benchmark.py --queries 128 --negatives 1024
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/report.py
```

An optional faster download index can be passed to pip as
`--index-url https://mirrors.aliyun.com/pypi/simple`. The model/data downloader uses
HF Mirror, pins the EG2 model revision, and verifies LFS sizes and SHA256 hashes.
Dataset revisions are recorded in `manifest-data.json`. The corpus is about
157 MB; the EG2 Q8 and Q4 text/vision downloads together are about 794 MB.

Use `--model current`, `--model eg2-q8` or `--model eg2-q4` to run one model.
Changing sample arguments recreates the common `dataset.json`; use matching
arguments for separate runs. The default corpus sample has 64 queries and 512
random distractors; the command above uses a larger sample.

### English

The English follow-up uses the 300 SciFact test queries and all 5,183 documents.
It concatenates titles and abstracts for all models, retaining the same production
baseline tokenizer and BGE query instruction. The 50 image queries are English
translations of the Chinese labels; the eight Chinese UI screenshot queries test
cross-language retrieval. Existing image vectors can be reused after checking
image ordering, paths and model SHA256 fingerprints, without copying model files.

```powershell
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/prepare.py --only data-en
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/benchmark.py --language en --queries 300 --negatives -1 --resource-root artifacts/embedding-comparison --image-cache artifacts/embedding-comparison --output artifacts/embedding-comparison/english
artifacts/embedding-comparison/.venv/Scripts/python.exe KitopiaBenchmark/EmbeddingComparison/report.py --output artifacts/embedding-comparison/english
```

The cache requires a completed Chinese run with unchanged images. Omit
`--image-cache` to encode images again. English data revisions are recorded in
`manifest-data-en.json`; English results have a separate directory. SciFact is
scientific English, so its results do not represent all English search tasks.

## Method

- Text: seeded random queries from `C-MTEB/T2Retrieval`, their complete positive
  qrels, and random other documents. Candidate subsets make this a diagnostic
  comparison, not an official full-corpus C-MTEB score.
- Images: 30 public Transformers.js fixture images and 11 repository screenshots,
  with 50 manually annotated Chinese queries. Labels were written after visual
  inspection and before model inference. Photo, document and screenshot results
  are reported separately. This small fixture set is not a held-out image benchmark.
- Current text inputs use the actual production `BertWordPieceTokenizer.cs`,
  linked into a small BCL-only CLI. The BGE query instruction, query limit of 128,
  document limit of 256, and Chinese-CLIP padding/limit of 52 match the application.
  The benchmark exposed a vocabulary-loading bug: trimming legitimate Unicode
  whitespace tokens created empty trie entries and could hang unknown-word encoding.
  The baseline includes the fix that preserves each vocabulary line unchanged.
- Chinese-CLIP images use RGB, OpenCV linear resize to 224x224, and the application's
  channel mean/std and NCHW layout. These samples have no EXIF rotation or transparency.
- EG2 uses the official Transformers processor, its search/document instructions,
  query/document limits of 128/256, the default 280 vision token budget, and full
  768-dimensional normalized embeddings. BGE and CLIP keep their 512/1024 dimensions.
- All models use ONNX Runtime 1.28.0 CPU, 8 intra-op threads, one inter-op thread,
  full graph optimization, and disabled CPU memory arenas. Each candidate runs in
  a fresh process, sequentially, with two warm-up runs for each workload.
- Document throughput uses batches of 8 for every candidate. Query latency uses
  single queries across three passes. Image indexing uses single images and includes
  both EG2 ONNX graphs. Recorded inference timings exclude tokenizer/processor
  work and disk decoding; preprocessing totals are recorded separately.
- Peak process RSS/private memory is sampled every 20 ms after preprocessing,
  across model loading and inference. Session-related increases are relative to
  memory immediately before loading sessions. Absolute Python memory includes
  processors and prepared tensors; it does not predict the C# host's exact footprint.
- Scores include Hit@1, Recall@5/10, nDCG@10 and MRR@10, with per-query rankings
  and all vectors preserved. Every output must be finite and nonzero.
- These tests compare embeddings directly. File-name ranking, lexical search,
  whole-file chunk averaging, OCR extraction, score fusion, vector database latency
  and GUI behavior are excluded. Each text sample is encoded once with truncation.

Sources:

- https://huggingface.co/onnx-community/embeddinggemma-2-ONNX
- https://huggingface.co/datasets/C-MTEB/T2Retrieval
- https://huggingface.co/datasets/C-MTEB/T2Retrieval-qrels
- https://huggingface.co/datasets/BeIR/scifact
- https://huggingface.co/datasets/BeIR/scifact-qrels
- https://huggingface.co/datasets/Xenova/transformers.js-docs
