# EmbeddingGemma 2 Q4

Model: https://huggingface.co/google/embeddinggemma-2

ONNX export: https://huggingface.co/onnx-community/embeddinggemma-2-ONNX/tree/daa72c51243991dfcaf9f9137d2c573d8f7790c0

The text and vision Q4 graphs use float32 activations, as recommended by Google.
The audio encoder is not needed for file search. External weights use native ONNX
shards no larger than 32 MiB, except the single 64 MiB embedding tensor, which is
kept intact in its own shard. Every repository file is below GitHub's 100 MiB
limit, without Git LFS. ONNX Runtime loads the shards directly; no merging is needed.
Only external-data locations and offsets change; all tensors retain their original bytes.
The upstream model is licensed under Apache 2.0; see LICENSE.txt and NOTICE.txt.

To regenerate shards from the original pinned downloads (requires onnx==1.22.0):

```powershell
artifacts/embedding-comparison/.venv/Scripts/python.exe build/SplitEmbeddingGemmaWeights.py --source-directory artifacts/embedding-comparison/eg2/onnx
```

- Query: `task: search result | query: {query}`.
- Document: `title: {title} | text: {content}`; use `none` for missing titles.
- Image: no text prompt; 280 soft tokens, 16x16 RGB patches, 3x3 pooling,
  aspect-preserving antialiased bicubic resize to multiples of 48 with intermediate
  uint8 rounding/clipping, values in [0,1].
- Use the official tokenizer JSON with BOS/EOS and right truncation within
  the shared 8192-token context. Images use the official image placeholder tokens.
- Read `sentence_embedding`, keep all 768 dimensions and L2-normalize.

Document indexing preserves paragraph boundaries and splits long inputs within the
8192-token context (including the title/prefix and special tokens), with a 16,384-character
buffer bound. Each chunk has its own normalized vector; search returns each file once,
using its highest-scoring chunk. Chunk vectors are not averaged into a file vector.
These chunk boundaries and overlap settings are application choices, not prescribed
by the model card. Previously averaged document vectors are invalidated on database
migration and rebuilt during the next indexing update; other vectors and file sources survive.

The app bundles these files under `%LOCALAPPDATA%\Kitopia\EmbeddingGemma2`.
Portable and Linux builds can also load the complete `EmbeddingGemma2` directory
beside the executable. Image decoding is bounded to 64 megapixels.
Old BGE/Chinese-CLIP vectors are rebuilt in the new shared space.
