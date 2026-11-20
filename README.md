# Meta-Verse-for-Education-AR-2

Demo for AR machine guidance on Meta Quest 3 / 3S.

The headset watches your work area through its color camera, recognizes the parts of an object with
an AI model that runs on the headset, and walks you through assembling it step by step. Each step is
checked against what the AI sees, and the app moves on by itself when the step is done.

The included example walks you through assembling a retractable gel pen from its three parts.

## What's in the app

The app opens to a **launch menu** with these options:

| Mode | What it does |
|---|---|
| **Pen assembly** (and any other procedure) | Step-by-step instructions, highlights on the parts each step needs, and automatic step checking |
| **Object highlighter** | Highlights everything a general-purpose model knows (the 80 COCO classes) |
| **Teach parts** | Records photos of a procedure's parts from the headset camera, for training its AI model |

To use the menus, point and pinch with your hand, or point a controller and pull the trigger. The
Menu button on the left controller returns to the launch menu.

## How it works

```
Quest camera -> AI detector -> depth / size -> tracker -> Step manager -> instruction panel + highlights
                (on headset)                                show instruction -> check AI result -> next step
```

| Part | File(s) |
|---|---|
| Procedures (data) | `Assets/VIPAR/Resources/Procedures/*.json`, one file per procedure |
| Step manager | `Assets/VIPAR/Core/Procedures/StepManager.cs`: completion checks, auto-advance, back/next |
| Detectors | `Assets/VIPAR/Core/YoloDetector.cs` (COCO model), `YoloRawDetector.cs` + `YoloOutputDecoder.cs` (custom Ultralytics-format models), `DetectorProfile.cs` |
| Perception | `Assets/VIPAR/Runtime/ObjectDetectionPipeline.cs`: camera, model, depth, tracking. Uses each part's known size when depth misses small handheld parts |
| UI | `Assets/VIPAR/Runtime/UI/`: panels, buttons, hand/controller pointer, head-following panels |
| App flow | `Assets/VIPAR/Runtime/AppController.cs` |
| Scene and settings | `Assets/VIPAR/Editor/ProjectBuilder.cs`, which adds the **VIP AR** menu in Unity |
| Model training | `Tools/ml/`: auto-labeling, training and ONNX export |

### Adding a procedure

Add a JSON file to `Assets/VIPAR/Resources/Procedures/`. It appears in the launch menu automatically.

- **`parts`:** each entry has a name, the detector labels that count as that part, a size in
  centimeters, and a highlight color.
- **`steps`:** each entry has a title, an instruction, a hint, which parts to highlight, and a `check`.

The available checks are:

| Check | Completes when |
|---|---|
| `manual` | The user presses Next |
| `partsSeen` | Each listed part has been recognized at least once during the step |
| `partsVisible` | All listed parts are visible together for `holdSeconds`. Use it with parts that stand for an assembly state, such as `refill_in_tip` |
| `partsTogether` | All listed parts are visible and touching for `holdSeconds` |

See `pen_assembly.json` for a full example.

### Training a model for new parts

Off-the-shelf models don't know specific parts, such as a pen's tip housing versus its click
housing, so each procedure gets a small custom detector.

1. In the headset, choose **Teach parts**. Record each part and each assembly state for 20 seconds,
   twice each, in different lighting. Also record **Background**.
2. Pull the photos to your PC:
   `adb pull /sdcard/Android/data/com.vipar.objecthighlighter/files/captures/<procedure> captures/`
3. Auto-label them. `Tools/ml/autolabel.py` uses an open-vocabulary model, keeping the pen-like
   object that touches a hand. Check the contact sheets it writes:
   `python Tools/ml/autolabel.py captures/<procedure> dataset <class1,class2,...>`
4. Train and export: `python Tools/ml/train_export.py dataset/data.yaml <name> 100`
5. Copy `best.onnx` to `Assets/VIPAR/Model/<detector id>.onnx`, and write the class names, one per
   line, to `<detector id>_labels.txt`. The build creates the detector profile automatically.

The scripts need `torch`, `ultralytics` and `onnx`.

## Build and install

Requirements: Unity 6000.3 with Android Build Support, and a Quest 3 or 3S on Horizon OS v74 or later
with Developer Mode on.

In Unity: **VIP AR > 1. Apply Quest Project Settings**, **2. Rebuild Scene**, **3. Build APK**. The
active platform must be Android. Or run the build from a terminal with the editor closed:

```powershell
& "<Unity install>\Editor\Unity.exe" -batchmode -quit -projectPath "<this folder>" `
  -buildTarget Android -executeMethod VipAr.EditorTools.ProjectBuilder.SetupAndBuild -logFile build.log
adb install -r -g Builds/VIPAR-Guidance.apk
```

The camera API doesn't work in Meta XR Simulator, so test on a headset.

## Tests

Use **Window > General > Test Runner > EditMode**. There are 38 tests, covering:

- the step manager and procedure validation
- the output decoder and distance estimation
- duplicate removal and tracking
- the COCO model, run on a photo
- the custom pen model, run on held-out headset photos

## Licenses

- **Pen model:** `Assets/VIPAR/Model/pen_parts.onnx` was fine-tuned from Ultralytics YOLO11n and
  trained with the Ultralytics library, both under **AGPL-3.0**. Check what that means before
  distributing it in a closed-source product.
- **COCO model:** the YOLOv9 model `yolov9sentis.sentis` comes from Meta's
  [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)
  and is [MIT licensed](https://github.com/MultimediaTechLab/YOLO/blob/main/LICENSE).
- **Meta XR SDK:** a dependency of this project, under the Oculus SDK License.
- **Test photo:** `Tests/Data/coco_000000039769.jpg` is from the [COCO](https://cocodataset.org)
  dataset.
