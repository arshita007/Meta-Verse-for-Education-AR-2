"""Turn Teach-mode photos into a YOLO training set.

Each recording contains a single known part (the folder name), so the only open question per
photo is *where* the part is. An open-vocabulary detector answers that: we take its most
confident pen-like box near the middle of the view.

Usage: python autolabel.py <captures/pen_assembly> <dataset_out> <class1,class2,...>

Writes images/ + labels/ split into train/val, data.yaml, and one contact sheet per class
for checking the boxes by eye. Photos where no box is found are listed and skipped.
"""
import random
import shutil
import sys
from pathlib import Path

import cv2
import numpy as np
from ultralytics import YOLOE

PROMPTS = ["pen", "ballpoint pen", "pen refill", "plastic tube", "marker", "stick"]
HAND = -1  # hands come from a separate model; no pen prompt has this index
BACKGROUND = "_background"
VAL_FRACTION = 0.15
SHEET_COLUMNS = 8
THUMB = 220


def blur_others(image, result, keep, min_score: float = 0.25) -> int:
    """Blur pen-like objects other than the labeled one.

    Other parts often lie on the desk in the background. Left unlabeled, they would teach the
    model that those parts are background; blurred, they're simply absent.
    """
    blurred = 0
    for (x1, y1, x2, y2), cls, score in zip(result.boxes.xyxy.tolist(), result.boxes.cls.tolist(), result.boxes.conf.tolist()):
        if int(cls) == HAND or score < min_score or (keep is not None and iou((x1, y1, x2, y2), keep) > 0.3):
            continue
        x1, y1 = max(int(x1) - 8, 0), max(int(y1) - 8, 0)
        x2, y2 = min(int(x2) + 8, image.shape[1]), min(int(y2) + 8, image.shape[0])
        if x2 > x1 and y2 > y1:
            image[y1:y2, x1:x2] = cv2.GaussianBlur(image[y1:y2, x1:x2], (0, 0), 25)
            blurred += 1
    return blurred


def iou(a, b) -> float:
    ix = max(0.0, min(a[2], b[2]) - max(a[0], b[0]))
    iy = max(0.0, min(a[3], b[3]) - max(a[1], b[1]))
    inter = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union > 0 else 0.0


HAND_PROMPTS = ["hand", "fingers", "human hand holding something"]
_hand_model = None


def hand_mask(image, min_score: float = 0.1):
    """Pixel mask of all hands, slightly grown so a part held between the fingers touches it.

    From a separate model instance: hands score poorly when they compete with pen prompts.
    """
    global _hand_model
    if _hand_model is None:
        _hand_model = YOLOE("yoloe-11s-seg.pt")
        _hand_model.set_classes(HAND_PROMPTS, _hand_model.get_text_pe(HAND_PROMPTS))
    result = _hand_model.predict(image, imgsz=640, conf=min_score, iou=0.5, retina_masks=True, verbose=False)[0]
    if result.masks is None or len(result.masks) == 0:
        return None
    mask = (result.masks.data.cpu().numpy().max(axis=0) > 0.5).astype(np.uint8)
    if mask.shape != image.shape[:2]:
        mask = cv2.resize(mask, (image.shape[1], image.shape[0]), interpolation=cv2.INTER_NEAREST)
    grow = max(6, int(0.015 * image.shape[1]))
    return cv2.dilate(mask, np.ones((grow, grow), np.uint8))


def hand_contact(box, mask) -> float:
    """Fraction of the box covered by the (grown) hand mask. 0 means not touching a hand."""
    x1, y1, x2, y2 = (int(round(v)) for v in box)
    x1, y1 = max(x1, 0), max(y1, 0)
    x2, y2 = min(x2, mask.shape[1]), min(y2, mask.shape[0])
    if x2 <= x1 or y2 <= y1:
        return 0.0
    return float(mask[y1:y2, x1:x2].mean())


def pick_box(result, image, width: int, height: int):
    """Most confident pen-like box touching a hand: the part being recorded is always held.

    Requiring a hand rules out look-alike objects lying on the desk (cables, headphone bands,
    other pen parts), which otherwise get picked surprisingly often.
    """
    mask = hand_mask(image)
    if mask is None:
        return None
    best, best_score = None, 0.0
    for (x1, y1, x2, y2), cls, score in zip(result.boxes.xyxy.tolist(), result.boxes.cls.tolist(), result.boxes.conf.tolist()):
        contact = hand_contact((x1, y1, x2, y2), mask)
        # A held part overlaps the fingers, but a box that's mostly hand is the hand itself.
        if int(cls) == HAND or contact < 0.02 or contact > 0.75:
            continue
        w, h = x2 - x1, y2 - y1
        area = (w * h) / (width * height)
        cx, cy = (x1 + x2) / 2 / width, (y1 + y2) / 2 / height
        if area < 0.002 or area > 0.5:
            continue
        if not (0.12 < cx < 0.88 and 0.12 < cy < 0.88):
            continue
        # Prefer confident boxes in firm contact with the hand.
        weighted = score * (0.5 + min(contact, 0.3) / 0.3 * 0.5)
        if weighted > best_score:
            best, best_score = (x1, y1, x2, y2), weighted
    return best


def main() -> None:
    source, out = Path(sys.argv[1]), Path(sys.argv[2])
    classes = sys.argv[3].split(",")
    random.seed(7)

    model = YOLOE("yoloe-11s-seg.pt")
    model.set_classes(PROMPTS, model.get_text_pe(PROMPTS))

    if out.exists():
        shutil.rmtree(out)
    for split in ("train", "val"):
        (out / "images" / split).mkdir(parents=True)
        (out / "labels" / split).mkdir(parents=True)
    (out / "sheets").mkdir()

    skipped, counts = [], {}
    folders = [f for f in sorted(source.iterdir()) if f.is_dir()]
    for folder in folders:
        is_background = folder.name == BACKGROUND
        if not is_background and folder.name not in classes:
            print(f"ignoring unknown folder {folder.name}")
            continue
        class_id = None if is_background else classes.index(folder.name)
        photos = sorted(folder.glob("*.jpg"))
        thumbs = []
        for photo in photos:
            image = cv2.imread(str(photo))
            if image is None:
                continue
            height, width = image.shape[:2]

            label_lines = []
            result = model.predict(image, imgsz=640, conf=0.05, iou=0.5, verbose=False)[0]
            box = None
            if not is_background:
                box = pick_box(result, image, width, height)
                if box is None:
                    skipped.append(photo.name)
                    continue
                x1, y1, x2, y2 = box
                label_lines.append(
                    f"{class_id} {(x1 + x2) / 2 / width:.6f} {(y1 + y2) / 2 / height:.6f} {(x2 - x1) / width:.6f} {(y2 - y1) / height:.6f}"
                )
            blur_others(image, result, box, min_score=0.4 if is_background else 0.25)

            split = "val" if random.random() < VAL_FRACTION else "train"
            stem = f"{folder.name}__{photo.stem}"
            cv2.imwrite(str(out / "images" / split / f"{stem}.jpg"), image, [cv2.IMWRITE_JPEG_QUALITY, 95])
            if box is not None:
                cv2.rectangle(image, (int(box[0]), int(box[1])), (int(box[2]), int(box[3])), (0, 255, 0), 4)
            (out / "labels" / split / f"{stem}.txt").write_text("\n".join(label_lines))
            counts[folder.name] = counts.get(folder.name, 0) + 1
            thumbs.append(cv2.resize(image, (THUMB, int(THUMB * height / width))))

        if thumbs:
            rows = [thumbs[i:i + SHEET_COLUMNS] for i in range(0, len(thumbs), SHEET_COLUMNS)]
            blank = np.zeros_like(thumbs[0])
            sheet = np.vstack([np.hstack(row + [blank] * (SHEET_COLUMNS - len(row))) for row in rows])
            cv2.imwrite(str(out / "sheets" / f"{folder.name}.jpg"), sheet)

    (out / "data.yaml").write_text(
        f"path: {out.resolve().as_posix()}\ntrain: images/train\nval: images/val\n"
        f"names:\n" + "".join(f"  {i}: {name}\n" for i, name in enumerate(classes))
    )
    print("labeled:", counts)
    print(f"skipped {len(skipped)} photos with no confident box: {skipped[:20]}")


if __name__ == "__main__":
    main()
