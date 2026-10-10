# PP-OCRv6 small

Official PaddlePaddle ONNX models, licensed under Apache 2.0 (see `LICENSE.txt`):

- Detector: https://huggingface.co/PaddlePaddle/PP-OCRv6_small_det_onnx/tree/28fe5895c24fd108c19eb3e8479f4ab385fbfc62
  - `ppocrv6_small_det.onnx`: 9,880,512 bytes
  - SHA256: `d73e0058b7a8086bbd57f3d10b8bcd4ff95363f67e06e2762b5e814fe9c9410e`
- Recognizer: https://huggingface.co/PaddlePaddle/PP-OCRv6_small_rec_onnx/tree/b8f84f0b80c529de40b4fbb3544b84fa7233a513
  - `ppocrv6_small_rec.onnx`: 21,159,378 bytes
  - SHA256: `5435fd747c9e0efe15a96d0b378d5bd157e9492ed8fd80edf08f30d02fa24634`

`ppocrv6_small_rec_dict.txt` contains the 18,708 entries from the recognizer's
`inference.yml` `PostProcess.character_dict`, followed by the ASCII space token.
CTC blank is output class 0; dictionary entry 0 corresponds to output class 1.
Together these match the recognizer's 18,710 output classes. Keep dictionary
ordering and whitespace intact.
Dictionary SHA256: `d051391881962ec266c4b4ee7b6a493fa2aeabaa2cd9ee369148cf3f6980af99`.

Both inputs use BGR NCHW float32. Detection uses ImageNet normalization and
dynamic spatial dimensions; recognition uses height 48, dynamic width and
normalization `(pixel / 255 - 0.5) / 0.5`.

The app installs these assets under `%LOCALAPPDATA%\Kitopia\Ocr`.
These models serve screenshot OCR and other explicit OCR operations. Search
indexing uses EG2 image embeddings without OCR; old OCR text vectors are removed
when the index database opens, preserving image/document vectors and file state.
