using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Meta.XR;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using VipAr.UI;

namespace VipAr.EditorTools
{
    /// <summary>
    /// Rebuilds the project's Quest settings and scene from code, so the setup is repeatable and
    /// can run from the command line: -executeMethod VipAr.EditorTools.ProjectBuilder.SetupAndBuild
    /// </summary>
    public static class ProjectBuilder
    {
        private const string ScenePath = "Assets/VIPAR/Scenes/ObjectDetection.unity";
        private const string MaterialPath = "Assets/VIPAR/Materials/HighlightFrame.mat";
        private const string ShaderPath = "Assets/VIPAR/Shaders/HighlightFrame.shader";
        private const string ModelPath = "Assets/VIPAR/Model/yolov9sentis.sentis";
        private const string LabelsPath = "Assets/VIPAR/Model/SentisYoloClasses.txt";
        private const string CocoProfilePath = "Assets/VIPAR/Model/coco.asset";
        private const string PointerMaterialPath = "Assets/VIPAR/Materials/PointerLine.mat";
        private const string MobileRenderPipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string CameraRigPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string ApkPath = "Builds/VIPAR-Guidance.apk";

        [MenuItem("VIP AR/1. Apply Quest Project Settings")]
        public static void ApplyProjectSettings()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "VIP AR";
            PlayerSettings.productName = "VIP AR Guidance";
            PlayerSettings.SetApplicationIdentifier(android, "com.vipar.objecthighlighter");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

            // A splash screen covers the real world on launch, which is jarring in a passthrough app.
            PlayerSettings.SplashScreen.show = false;

            ConfigureRenderPipeline();
            ConfigureXr();
            AssetDatabase.SaveAssets();
            Debug.Log("[VIPAR] Project settings applied");
        }

        // Passthrough shows through the alpha channel of the rendered image. HDR rendering would
        // discard that alpha, leaving a black background instead of the real world.
        private static void ConfigureRenderPipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRenderPipelinePath);
            pipeline.supportsHDR = false;
            pipeline.renderScale = 1f;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 0f;

            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        private static void ConfigureXr()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null || settings.Manager == null)
            {
                throw new InvalidOperationException("No XR settings for Android. Check Assets/XR.");
            }

            // The copied configuration also references a loader from a package this project doesn't use.
            var manager = settings.Manager;
            var loaders = manager.activeLoaders.Where(loader => loader != null).ToList();
            if (!manager.TrySetLoaders(loaders))
            {
                throw new InvalidOperationException("Could not set XR loaders");
            }
            EditorUtility.SetDirty(manager);
            Debug.Log($"[VIPAR] XR loaders: {string.Join(", ", loaders.Select(l => l.name))}");
            if (!loaders.Any(l => l.name.Contains("OpenXR")))
            {
                throw new InvalidOperationException("OpenXR loader is not active");
            }

            // Foveation and the subsampled layout need extra URP setup. A passthrough app
            // draws few pixels and gains little from them, so they're off.
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            foreach (var feature in openXr.GetFeatures())
            {
                if (feature == null)
                {
                    continue;
                }
                if (feature.name.Contains("SubsampledLayout") || feature.name.Contains("Foveation"))
                {
                    feature.enabled = false;
                    EditorUtility.SetDirty(feature);
                }
            }
            Debug.Log("[VIPAR] OpenXR features on: " + string.Join(", ", openXr.GetFeatures().Where(f => f != null && f.enabled).Select(f => f.name)));
        }

        [MenuItem("VIP AR/2. Rebuild Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "OVRCameraRig";

            var manager = rig.GetComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            managerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var passthrough = rig.AddComponent<OVRPassthroughLayer>();
            passthrough.overlayType = OVROverlay.OverlayType.Underlay;

            var eye = rig.GetComponentInChildren<OVRCameraRig>().centerEyeAnchor;
            if (eye == null)
            {
                eye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            }
            var camera = eye.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.allowHDR = false;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;

            var cameraAccessObject = new GameObject("Passthrough Camera");
            var cameraAccess = cameraAccessObject.AddComponent<PassthroughCameraAccess>();
            cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            cameraAccess.RequestedResolution = new Vector2Int(1280, 960);

            // Switched on by the pipeline once the spatial data permission is granted.
            var depthObject = new GameObject("Environment Depth");
            var depth = depthObject.AddComponent<EnvironmentRaycastManager>();
            depthObject.SetActive(false);

            // Hand tracking, for pointing at panels and pinching to click. The real hands show
            // through passthrough, so no hand meshes are drawn.
            var trackingSpace = rig.transform.Find("TrackingSpace");
            var leftHand = AddHand(trackingSpace, "LeftHandAnchor", OVRHand.Hand.HandLeft);
            var rightHand = AddHand(trackingSpace, "RightHandAnchor", OVRHand.Hand.HandRight);

            var perceptionObject = new GameObject("Perception");
            var pipeline = perceptionObject.AddComponent<ObjectDetectionPipeline>();
            var highlights = perceptionObject.AddComponent<HighlightRenderer>();
            SetReference(pipeline, "m_camera", cameraAccess);
            SetReference(pipeline, "m_depth", depth);
            SetReference(highlights, "m_frameMaterial", GetOrCreateFrameMaterial());

            var pointer = new GameObject("Pointer").AddComponent<RayPointer>();
            SetReference(pointer, "m_rig", rig.GetComponent<OVRCameraRig>());
            SetReference(pointer, "m_leftHand", leftHand);
            SetReference(pointer, "m_rightHand", rightHand);
            SetReference(pointer, "m_lineMaterial", GetOrCreatePointerMaterial());

            var app = new GameObject("App").AddComponent<AppController>();
            SetReference(app, "m_pipeline", pipeline);
            SetReference(app, "m_highlights", highlights);
            var profiles = FindDetectorProfiles();
            var profilesProperty = new SerializedObject(app);
            var array = profilesProperty.FindProperty("m_profiles");
            array.arraySize = profiles.Length;
            for (int i = 0; i < profiles.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];
            }
            profilesProperty.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[VIPAR] Detector profiles: {string.Join(", ", profiles.Select(p => p.id))}");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[VIPAR] Scene saved to {ScenePath}");
        }

        private static OVRHand AddHand(Transform trackingSpace, string anchorName, OVRHand.Hand handType)
        {
            var anchor = trackingSpace.Find(anchorName);
            var hand = anchor.gameObject.AddComponent<OVRHand>();
            var serialized = new SerializedObject(hand);
            serialized.FindProperty("HandType").intValue = (int)handType;
            // The pointer pose is reported relative to tracking space.
            serialized.FindProperty("_pointerPoseRoot").objectReferenceValue = trackingSpace;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return hand;
        }

        /// <summary>Every model profile in the project. The general-purpose COCO one is created if missing.</summary>
        private static DetectorProfile[] FindDetectorProfiles()
        {
            if (AssetDatabase.LoadAssetAtPath<DetectorProfile>(CocoProfilePath) == null)
            {
                var coco = ScriptableObject.CreateInstance<DetectorProfile>();
                coco.id = "coco";
                coco.displayName = "general object model";
                coco.model = Require<ModelAsset>(ModelPath);
                coco.labels = Require<TextAsset>(LabelsPath);
                coco.format = DetectorOutputFormat.BoxesClassesScores;
                coco.scoreThreshold = 0.35f;
                coco.iouThreshold = 0.4f;
                AssetDatabase.CreateAsset(coco, CocoProfilePath);
            }

            // Custom models trained with the scripts in Tools/ml are dropped in as
            // Assets/VIPAR/Model/<id>.onnx plus <id>_labels.txt, and get a profile automatically.
            foreach (var onnxPath in Directory.GetFiles("Assets/VIPAR/Model", "*.onnx"))
            {
                string id = Path.GetFileNameWithoutExtension(onnxPath);
                string profilePath = $"Assets/VIPAR/Model/{id}.asset";
                string labelsPath = $"Assets/VIPAR/Model/{id}_labels.txt";
                if (AssetDatabase.LoadAssetAtPath<DetectorProfile>(profilePath) != null || !File.Exists(labelsPath))
                {
                    continue;
                }
                var profile = ScriptableObject.CreateInstance<DetectorProfile>();
                profile.id = id;
                profile.displayName = $"{id.Replace('_', ' ')} model";
                profile.model = Require<ModelAsset>(onnxPath.Replace('\\', '/'));
                profile.labels = Require<TextAsset>(labelsPath);
                profile.format = DetectorOutputFormat.UltralyticsRaw;
                profile.scoreThreshold = 0.5f;
                profile.iouThreshold = 0.5f;
                AssetDatabase.CreateAsset(profile, profilePath);
                Debug.Log($"[VIPAR] Created detector profile '{id}' from {onnxPath}");
            }

            return AssetDatabase.FindAssets("t:DetectorProfile")
                .Select(guid => AssetDatabase.LoadAssetAtPath<DetectorProfile>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(profile => profile != null && profile.model != null && profile.labels != null)
                .ToArray();
        }

        private static Material GetOrCreatePointerMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PointerMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", new Color(0.75f, 0.95f, 1f, 1f));
                AssetDatabase.CreateAsset(material, PointerMaterialPath);
            }
            return material;
        }

        private static Material GetOrCreateFrameMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
                material = new Material(Require<Shader>(ShaderPath));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            return material;
        }

        private static T Require<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Missing {typeof(T).Name}", path);
            }
            return asset;
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new MissingFieldException(target.GetType().Name, field);
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Logs what Meta's Project Setup Tool still flags, so nothing is fixed behind your back.</summary>
        [MenuItem("VIP AR/Report Meta Project Setup Issues")]
        public static void ReportMetaSetupIssues()
        {
            // This API is internal to Meta's SDK, hence reflection. A future SDK may change it,
            // which should only cost this report, never the build.
            const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var group = BuildTargetGroup.Android;
            var setupType = Type.GetType("OVRProjectSetup, Oculus.VR.Editor");
            var getTasks = setupType?.GetMethod("GetTasks", anyStatic);
            if (getTasks == null)
            {
                Debug.LogWarning("[VIPAR] Meta setup report unavailable in this SDK version");
                return;
            }

            int outstanding = 0;
            foreach (var task in (IEnumerable)getTasks.Invoke(null, new object[] { group }))
            {
                try
                {
                    if (!(bool)ReadTaskValue(task, "Valid", group) || (bool)CallTask(task, "IsIgnored", group) || (bool)ReadIsDone(task, group))
                    {
                        continue;
                    }
                    outstanding++;
                    Debug.Log($"[VIPAR] Meta setup [{ReadTaskValue(task, "Level", group)}] {ReadTaskValue(task, "Message", group)}");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[VIPAR] Could not evaluate a Meta setup task: {exception.GetBaseException().Message}");
                }
            }
            Debug.Log($"[VIPAR] Meta setup: {outstanding} item(s) outstanding");
        }

        private static object ReadTaskValue(object task, string property, BuildTargetGroup group)
        {
            var value = task.GetType().GetProperty(property).GetValue(task);
            return value.GetType().GetMethod("GetValue", new[] { typeof(BuildTargetGroup) }).Invoke(value, new object[] { group });
        }

        private static object CallTask(object task, string method, BuildTargetGroup group) =>
            task.GetType().GetMethod(method, new[] { typeof(BuildTargetGroup) }).Invoke(task, new object[] { group });

        private static object ReadIsDone(object task, BuildTargetGroup group) =>
            ((Delegate)task.GetType().GetProperty("IsDone").GetValue(task)).DynamicInvoke(group);

        [MenuItem("VIP AR/3. Build APK")]
        public static void BuildApk()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"[VIPAR] Build {summary.result}: {summary.totalErrors} errors, {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Build failed: {summary.result}");
            }
        }

        public static void SetupOnly()
        {
            ApplyProjectSettings();
            BuildScene();
            ReportMetaSetupIssues();
        }

        public static void SetupAndBuild()
        {
            SetupOnly();
            BuildApk();
        }
    }
}
