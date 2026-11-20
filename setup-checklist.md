# Quest 3 AR Object Detection — Setup Checklist

_Last checked: Nov 2026. Versions move fast; re-check Meta's docs if something doesn't match._

## Core approach
Start from Meta's official **Unity-PassthroughCameraApiSamples** project (MultiObjectDetection scene) rather than from scratch. It already does: passthrough camera capture (MRUK `PassthroughCameraAccess`) → YOLO inference on-device (Unity Inference Engine, 80 COCO classes) → depth raycast (MRUK `EnvironmentRaycastManager`) to place 3D markers on real objects → spatial anchors to prevent drift.

## 1. Accounts & software (Windows PC recommended — Meta Horizon Link is Windows-only)
- [ ] Meta developer account (developers.meta.com/horizon/sign-up) — verify it
- [ ] Unity ID
- [ ] Meta Horizon mobile app on phone, headset paired
- [ ] Unity Hub + Unity 6 (6000.0.x, recent patch) with Android Build Support, OpenJDK, Android SDK & NDK
- [ ] Meta Quest Developer Hub (MQDH) — device management, APK install, logs
- [ ] Git + Git LFS (the sample repo needs LFS)
- [ ] Node.js LTS (for Meta's MCP server) and Claude Code CLI

## 2. Headset
- [ ] Horizon OS v74+ on the Quest 3
- [ ] Phone app → Devices → headset → Developer Mode ON
- [ ] USB-C data cable to PC → accept "Allow USB debugging" in headset (tick "always allow")
- [ ] Verify: `adb devices` shows the headset (or it appears in MQDH)

## 3. First build
- [ ] `git lfs install` then `git clone https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples`
- [ ] Open in Unity 6 via Unity Hub
- [ ] Meta > Tools > Project Setup Tool → Fix All
- [ ] File > Build Profiles → Meta Quest (or Android on Unity < 6.1) → Switch Platform
- [ ] Run Device = Quest 3 → Build And Run (APK installs and launches)
- [ ] In headset: accept camera + spatial data permissions
- Note: Passthrough Camera API does NOT work in Meta XR Simulator; test on the device.

## 4. Connect Claude Code (runs locally on the PC, in the Unity project folder)
Unity editor MCP — pick one:
- Official: install `com.unity.ai.assistant` (Unity 6+). Edit > Project Settings > AI > Unity MCP → Integrations → Claude Code → Configure. Or manually:
  `claude mcp add unity-mcp -- "C:\Users\<you>\.unity\relay\relay_win.exe" --mcp`
  First connection: Accept it under Pending Connections in that settings page.
- Community (free, open source): Package Manager → Add from git URL → `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`, then Window > MCP for Unity → Auto-Setup (needs Python `uv`).

Meta device/docs MCP (install APKs, logcat, screenshots, current Quest docs):
- `npx @meta-quest/hzdb mcp install claude-code` (hzdb is now also called Meta VR CLI / `metavr`)
- Or the `meta-quest/agentic-tools` bundle (MCP + Quest skills) — see its README.

Verify in Claude Code: `/mcp` lists both servers; ask it to read Unity console errors and to list connected Quest devices.

## 5. Roadmap after the sample runs
1. Change highlight visuals (outline/box/label) — replace marker prefab
2. Continuous detection instead of button-triggered; dedupe/track objects over time
3. Filter or rename classes; tune confidence + NMS thresholds
4. Custom classes: train YOLO on own dataset → convert with the sample's Sentis model converter
5. Performance: profile with Perfetto (via hzdb), lower inference resolution/frequency if needed
