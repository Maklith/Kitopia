# Search and indexing

Paths in backticks are relative to the repository root unless stated otherwise.

## Behavior and contracts

- Indexing/search: `Indexing/IIndexService.cs` also defines `ISearchEntryIndex` and `IIndexMaintenanceService`; app indexing and Everything contracts are in `Search/`.
- Interactive Everything results load in pages of 100. `EverythingFileListControl` observes the DataGrid's vertical scrollbar and sends column sort changes through the search view model to `IAppToolService`. Everything owns sorting across all matches; changing the sort restarts at offset zero, and stale queries must not append results or reset the new query's loading state.
- Everything prefix searches keep the full result list by default; preview remains available through the manual toggle. The search preview uses a draggable middle splitter to adjust the list and preview widths, and closing preview restores the full list width.
- Document text vectors are stored by file key and chunk index in `IndexVectorStore`. Images use EG2 image embeddings without OCR; old OCR text vectors are removed when the index database opens while image/document vectors, file sources and fingerprints survive. PP-OCRv6 small remains available for screenshots and explicit OCR operations. Search groups document chunk hits by file using the highest similarity; replacement, copying and deletion must include every chunk. Normal indexing uses at most 1024 tokens per chunk, including title/prompt/special tokens, with 48 tokens of content overlap. Text batches contain at most four items and a quadratic attention budget allows only one 1024-token item at a time. Normal text inputs are padded to power-of-two lengths up to 1024, with masks excluding padding, to reuse a small set of arena shapes. The model still accepts up to 8192 tokens for explicit calls. The model ID includes the chunking policy so old vectors are replaced during incremental indexing while file sources and fingerprints are preserved.

## Storage and benchmarks

- The current vector/index database is `index.db` under that root. Do not reintroduce the legacy `search-rag.db` path into the current index store.
- EmbeddingGemma 2 Q4 assets live under `EmbeddingGemma2`. Queries use `task: search result | query: `; documents use `title: {title or none} | text: {content}`; images have no text prompt. Older vector dimensions are invalidated while managed file sources and file fingerprints remain for incremental rebuilding.
- Image retrieval benchmarks reuse the README feature images under `docs/assets/README/`, selected by the screenshot annotations in `tests/KitopiaBenchmark/EmbeddingComparison/image-queries*.json`.
