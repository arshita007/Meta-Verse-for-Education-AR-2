"""Fine-tune a small YOLO detector on the labeled dataset and export it for the headset.

Usage: python train_export.py <dataset/data.yaml> <run_name> [epochs]

Exports ONNX at 480x640 (height x width): the headset camera's 4:3 aspect ratio, so camera
frames go in without distortion. Output is the raw [1, 4 + classes, candidates] tensor, which
the app decodes itself (YoloOutputDecoder), so no NMS layers need to run on the headset.
"""
import sys
from pathlib import Path

from ultralytics import YOLO


def main() -> None:
    data, name = sys.argv[1], sys.argv[2]
    epochs = int(sys.argv[3]) if len(sys.argv) > 3 else 80

    model = YOLO("yolo11n.pt")
    model.train(
        data=data,
        epochs=epochs,
        imgsz=640,
        batch=16,
        device=0,
        # No dataloader worker processes: on Windows each one maps shared memory, and they
        # exhausted the page file (error 1455) on this machine.
        workers=0,
        project="runs",
        name=name,
        exist_ok=True,
        patience=25,
        # The parts are held at all angles, so rotate and flip more than the defaults.
        degrees=25,
        flipud=0.3,
        fliplr=0.5,
        plots=True,
        verbose=False,
    )
    metrics = model.val(data=data, imgsz=640, verbose=False)
    print(f"mAP50={metrics.box.map50:.3f} mAP50-95={metrics.box.map:.3f}")
    for i, name_ in model.names.items():
        print(f"  {name_}: AP50={metrics.box.ap50[i]:.3f}" if i < len(metrics.box.ap50) else f"  {name_}: n/a")

    onnx = model.export(format="onnx", imgsz=(480, 640), opset=15, simplify=True, dynamic=False)
    print("exported", Path(onnx).resolve())


if __name__ == "__main__":
    main()
