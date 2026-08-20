from __future__ import annotations

import argparse
import json
import os
import sys
import traceback
from pathlib import Path
from time import perf_counter, sleep
from typing import Any


MODEL_PAIRS = {
    "PP-OCRv6_small_rec": "PP-OCRv6_small_det",
    "PP-OCRv6_tiny_rec": "PP-OCRv6_tiny_det",
}
SUPPORTED_DEVICES = {"cpu", "gpu:0"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run LilacMacro OCR within a selected crop.")
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--channel")
    parser.add_argument("--preload-model", choices=sorted(MODEL_PAIRS))
    parser.add_argument("--preload-device", choices=sorted(SUPPORTED_DEVICES))
    parser.add_argument("--input")
    parser.add_argument("--crop", nargs=4, type=int, metavar=("X", "Y", "WIDTH", "HEIGHT"))
    parser.add_argument("--crop-output")
    parser.add_argument("--scale", type=int, choices=(1, 2, 3, 4), default=1)
    parser.add_argument("--model", choices=sorted(MODEL_PAIRS))
    parser.add_argument("--device", choices=sorted(SUPPORTED_DEVICES), default="cpu")
    parser.add_argument("--output")
    args = parser.parse_args()
    if args.serve and not args.channel:
        parser.error("--channel is required with --serve")
    if bool(args.preload_model) != bool(args.preload_device):
        parser.error("--preload-model and --preload-device must be supplied together")
    if not args.serve and not all((args.input, args.model, args.output)):
        parser.error("--input, --model, and --output are required unless --serve is used")
    return args


def create_pipeline(model_name: str, device: str) -> Any:
    from paddleocr import PaddleOCR

    options: dict[str, Any] = {
        "text_detection_model_name": MODEL_PAIRS[model_name],
        "text_recognition_model_name": model_name,
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False,
        "use_textline_orientation": False,
        "device": device,
    }
    if device == "cpu":
        # PaddlePaddle 3.3.0's bundled CPU oneDNN path crashes in PP-OCRv6
        # detection on some Windows CPUs. Keep CPU inference on the plain
        # Paddle engine; GPU keeps its existing accelerated path.
        options["enable_mkldnn"] = False
    return PaddleOCR(**options)


def run_ocr(
    input_value: str,
    model_name: str,
    device: str,
    cache: dict[tuple[str, str], Any],
    crop: list[int] | tuple[int, int, int, int] | None = None,
    crop_output: str | None = None,
    scale: int = 1,
) -> dict[str, Any]:
    from paddleocr import __version__ as paddleocr_version

    if model_name not in MODEL_PAIRS:
        raise ValueError(f"Unsupported OCR model: {model_name}")
    if device not in SUPPORTED_DEVICES:
        raise ValueError(f"Unsupported OCR device: {device}")
    input_path = Path(input_value).resolve(strict=True)
    inference_path = prepare_crop(input_path, crop, crop_output, scale)
    cache_key = (model_name, device)
    cached = cache_key in cache
    load_ms = 0
    if not cached:
        load_started = perf_counter()
        cache[cache_key] = create_pipeline(model_name, device)
        load_ms = round((perf_counter() - load_started) * 1000)

    inference_started = perf_counter()
    results = list(cache[cache_key].predict(input=str(inference_path)))
    inference_ms = round((perf_counter() - inference_started) * 1000)
    if len(results) != 1:
        raise RuntimeError(f"Expected one OCR pipeline result, received {len(results)}.")

    raw = results[0].json
    result = raw.get("res", raw)
    boxes = result.get("rec_boxes", [])
    texts = result.get("rec_texts", [])
    recognition_scores = result.get("rec_scores", [])
    detection_scores = result.get("dt_scores", [])
    region_count = min(len(boxes), len(texts), len(recognition_scores))
    regions: list[dict[str, Any]] = []
    for index in range(region_count):
        left, top, right, bottom = (int(value) for value in boxes[index])
        if right <= left or bottom <= top:
            continue
        regions.append(
            {
                "x": left,
                "y": top,
                "width": right - left,
                "height": bottom - top,
                "text": str(texts[index]),
                "detection_confidence": float(detection_scores[index])
                if index < len(detection_scores)
                else None,
                "recognition_confidence": float(recognition_scores[index]),
            }
        )

    confidence = (
        sum(region["recognition_confidence"] for region in regions) / len(regions)
        if regions
        else 0.0
    )
    return {
        "detector_model_name": MODEL_PAIRS[model_name],
        "model_name": model_name,
        "device": device,
        "text": "\n".join(region["text"] for region in regions),
        "confidence": confidence,
        "model_load_milliseconds": load_ms,
        "inference_milliseconds": inference_ms,
        "paddleocr_version": paddleocr_version,
        "model_cached": cached,
        "regions": regions,
    }


def prepare_crop(
    input_path: Path,
    crop: list[int] | tuple[int, int, int, int] | None,
    crop_output: str | None,
    scale: int = 1,
) -> Path:
    if crop is None:
        return input_path
    if crop_output is None:
        raise ValueError("crop_output is required when crop is supplied")
    from PIL import Image

    x, y, width, height = (int(value) for value in crop)
    if x < 0 or y < 0 or width <= 0 or height <= 0:
        raise ValueError("crop must be a positive rectangle")
    output_path = Path(crop_output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(input_path) as source:
        if x + width > source.width or y + height > source.height:
            raise ValueError("crop is outside the input image")
        cropped = source.crop((x, y, x + width, y + height))
        if scale > 1:
            cropped = cropped.resize(
                (cropped.width * scale, cropped.height * scale),
                Image.Resampling.LANCZOS,
            )
        cropped.save(output_path, format="PNG")
    return output_path


def write_result(path_value: str, payload: dict[str, Any]) -> None:
    output_path = Path(path_value).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = output_path.with_suffix(output_path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    temporary.replace(output_path)


def read_request(path: Path, attempts: int = 8) -> dict[str, Any]:
    """Read an atomically published request through transient Windows access races."""
    for attempt in range(attempts):
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (PermissionError, OSError):
            if attempt + 1 == attempts:
                raise
            sleep(0.025 * (attempt + 1))
    raise RuntimeError("unreachable request retry state")


def serve(
    channel_value: str,
    preload_model: str | None = None,
    preload_device: str | None = None,
) -> int:
    channel = Path(channel_value).resolve(strict=True)
    cache: dict[tuple[str, str], Any] = {}
    if preload_model is not None and preload_device is not None:
        cache[(preload_model, preload_device)] = create_pipeline(
            preload_model,
            preload_device,
        )
    (channel / "ready").write_text("ready", encoding="utf-8")
    while not (channel / "stop").exists():
        requests = sorted(channel.glob("request-*.json"))
        if not requests:
            sleep(0.025)
            continue
        for request_path in requests:
            request_id = request_path.stem.removeprefix("request-")
            try:
                request = read_request(request_path)
                payload = run_ocr(
                    str(request["input"]),
                    str(request["model"]),
                    str(request.get("device", "cpu")),
                    cache,
                    request.get("crop"),
                    request.get("crop_output"),
                    int(request.get("scale", 1)),
                )
                payload["request_id"] = str(request.get("request_id", request_id))
            except Exception as error:  # The caller needs a bounded protocol error, not a hung worker.
                traceback.print_exc(file=sys.stderr)
                payload = {"request_id": request_id, "error": str(error)}
            finally:
                request_path.unlink(missing_ok=True)
            write_result(str(channel / f"response-{request_id}.json"), payload)
    return 0


def main() -> int:
    args = parse_args()
    os.environ.setdefault("PADDLE_PDX_MODEL_SOURCE", "BOS")
    if args.serve:
        return serve(
            str(args.channel),
            args.preload_model,
            args.preload_device,
        )
    payload = run_ocr(
        str(args.input),
        str(args.model),
        str(args.device),
        {},
        args.crop,
        args.crop_output,
        args.scale,
    )
    write_result(str(args.output), payload)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
