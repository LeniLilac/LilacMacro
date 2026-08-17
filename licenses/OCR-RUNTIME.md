# OCR Runtime Notices

LilacMacro's official Windows installer bundles the following CPU OCR runtime components:

- Python 3.12 from the Python Software Foundation: [PSF License](https://docs.python.org/3/license.html)
- PaddlePaddle 3.3.0: [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- PaddleOCR 3.7.0 and PaddleX dependencies: [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- PP-OCRv6 model assets: distributed by the PaddlePaddle project under the project model terms

The installer carries the Python license file beside the bundled runtime. Python package metadata and dependency licenses remain available in the package installation under `ocr\python\Lib\site-packages`. Optional GPU packages are downloaded from the official Paddle package feeds only after the user passes the first-run privacy choices.
