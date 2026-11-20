"""Run open-vocabulary detectors on headset photos and save annotated copies.

Usage: python eval_prompts.py <image_dir> <out_dir> [model ...]

Prints every detection so prompts and models can be compared side by side.
"""
import sys
import time
from pathlib import Path

import cv2
from ultralytics import YOLO, YOLOE

# Several phrasings per part: open-vocabulary models are sensitive to wording, and the
# best phrasing for each part is what we're trying to find.
PROMPTS = [
    "pen",
    "ballpoint pen",
    "ink refill",
    "pen refill",
    "ink cartridge",
    "pen barrel",
    "pen tip",
    "plastic tube",
    "pen cap",
    "spring",
]

DEFAULT_MODELS = ["yoloe-26s-seg.pt", "yoloe-26n-seg.pt", "yoloe-11s-seg.pt", "yolov8s-worldv2.pt"]


def load(name: str):
    model = YOLOE(name) if name.startswith("yoloe") else YOLO(name)
    model.set_classes(PROMPTS) if name.startswith("yolov8") else model.set_classes(PROMPTS, model.get_text_pe(PROMPTS))
    return model


def main() -> None:
    image_dir, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
    models = sys.argv[3:] or DEFAULT_MODELS
    images = sorted(p for p in image_dir.iterdir() if p.suffix.lower() in {".png", ".jpg", ".jpeg"})
    out_dir.mkdir(parents=True, exist_ok=True)

    for name in models:
        model = load(name)
        tag = Path(name).stem
        print(f"\n===== {name} =====")
        start = time.perf_counter()
        for image in images:
            # Same 4:3 input the headset camera gives.
            result = model.predict(str(image), imgsz=(480, 640), conf=0.1, iou=0.5, verbose=False)[0]
            found = [
                f"{result.names[int(c)]}:{s:.2f}"
                for c, s in sorted(zip(result.boxes.cls.tolist(), result.boxes.conf.tolist()), key=lambda t: -t[1])
            ]
            print(f"{image.name}: {', '.join(found) if found else '(nothing)'}")
            annotated = result.plot(masks=False)
            cv2.imwrite(str(out_dir / f"{image.stem}__{tag}.jpg"), annotated)
        print(f"({(time.perf_counter() - start) / max(len(images), 1) * 1000:.0f} ms/image on PC, includes first-run warmup)")


if __name__ == "__main__":
    main()
