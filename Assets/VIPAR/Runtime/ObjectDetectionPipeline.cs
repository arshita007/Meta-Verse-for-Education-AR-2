using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Meta.XR;
using UnityEngine;

namespace VipAr
{
    public enum PerceptionState
    {
        Starting,
        WaitingForPermission,
        Unsupported,
        LoadingModel,
        Running,
        Idle,
    }

    /// <summary>
    /// Camera image -> AI model -> depth -> tracker. Modes (guidance, explore) pick which model
    /// runs and read the tracked objects; this class knows nothing about procedures or UI.
    /// </summary>
    public sealed class ObjectDetectionPipeline : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_camera;
        [SerializeField] private EnvironmentRaycastManager m_depth;

        [Tooltip("Upper limit on images processed per second. Lower it to save CPU and battery.")]
        [SerializeField, Range(1f, 30f)] private float m_maxDetectionsPerSecond = 15f;
        [SerializeField] private float m_maxDepthMeters = 8f;

        // Fractions of the box half-size. Sampling several points stops a ray that passes
        // through a gap in the object (a chair back, a mug handle) from placing it on the far wall.
        private static readonly Vector2[] s_depthSamples =
        {
            new(0f, 0f), new(-0.3f, 0f), new(0.3f, 0f), new(0f, -0.3f), new(0f, 0.3f),
        };

        private readonly List<Detection> m_detections = new();
        private readonly List<WorldObservation> m_observations = new();
        private readonly List<float> m_depthHits = new();
        private DetectorProfile m_profile;
        private IObjectDetector m_detector;
        private Coroutine m_loop;
        private bool m_permissionsGranted;

        public event Action FrameProcessed;

        public PerceptionState State { get; private set; } = PerceptionState.Starting;
        public string StatusMessage { get; private set; } = "Starting...";
        public ObjectTracker Tracker { get; } = new();
        public IReadOnlyList<string> Labels => m_detector?.Labels ?? Array.Empty<string>();
        public float DetectionsPerSecond { get; private set; }
        public PassthroughCameraAccess CameraAccess => m_camera;
        public DetectorProfile ActiveProfile => m_profile;

        /// <summary>
        /// Real-world size (longest side, meters) of a class, or 0 if unknown. Lets small parts
        /// held in the hand be placed correctly when depth sensing misses them.
        /// </summary>
        public Func<int, float> KnownSizeMeters { get; set; }

        private IEnumerator Start()
        {
            if (!PassthroughCameraAccess.IsSupported)
            {
                SetState(PerceptionState.Unsupported, "This headset doesn't support the Passthrough Camera API.\nQuest 3 or 3S on Horizon OS v74+ is required.");
                yield break;
            }

            SetState(PerceptionState.WaitingForPermission, "Please allow the camera and spatial data permissions.");
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.Scene,
                OVRPermissionsRequester.Permission.PassthroughCameraAccess,
            });
            while (!OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene) ||
                   !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            {
                yield return null;
            }

            // Depth needs the spatial data permission, so it stays off until that's granted.
            m_depth.gameObject.SetActive(true);
            m_permissionsGranted = true;
            SetState(PerceptionState.Idle, "Ready");
            if (m_profile != null)
            {
                Run(m_profile);
            }
        }

        /// <summary>Starts detecting with the given model, replacing any running one.</summary>
        public void Run(DetectorProfile profile)
        {
            bool sameModel = profile == m_profile && m_detector != null;
            m_profile = profile;
            Tracker.Clear();
            if (!m_permissionsGranted)
            {
                return; // Start() picks this up once permissions are granted.
            }

            if (m_loop != null)
            {
                StopCoroutine(m_loop);
            }
            if (!sameModel)
            {
                m_detector?.Dispose();
                m_detector = null;
            }
            m_loop = StartCoroutine(RunLoop());
        }

        /// <summary>Stops detecting and frees the model's memory.</summary>
        public void Stop()
        {
            if (m_loop != null)
            {
                StopCoroutine(m_loop);
                m_loop = null;
            }
            m_detector?.Dispose();
            m_detector = null;
            m_profile = null;
            Tracker.Clear();
            if (m_permissionsGranted)
            {
                SetState(PerceptionState.Idle, "Ready");
            }
        }

        private IEnumerator RunLoop()
        {
            if (m_detector == null)
            {
                SetState(PerceptionState.LoadingModel, $"Loading {m_profile.displayName}...");
                yield return null;
                yield return null; // let the message render before the slow load
                m_detector = m_profile.CreateDetector();
                m_detector.WarmUp();
            }

            SetState(PerceptionState.LoadingModel, "Starting camera...");
            while (!m_camera.IsPlaying)
            {
                yield return null;
            }

            if (!EnvironmentRaycastManager.IsSupported)
            {
                Debug.LogWarning("[VIPAR] Depth unavailable; placing objects by known size only.");
            }

            SetState(PerceptionState.Running, "Running");
            float minInterval = 1f / m_maxDetectionsPerSecond;
            while (true)
            {
                float started = Time.realtimeSinceStartup;

                if (m_camera.IsPlaying && IsHeadPoseValid())
                {
                    // Taken before the image is processed: objects are placed where the headset
                    // was when it saw them, not where it is by the time the model finishes.
                    var cameraPose = m_camera.GetCameraPose();
                    yield return m_detector.Detect(m_camera.GetTexture(), m_profile.scoreThreshold, m_profile.iouThreshold, m_detections);

                    if (m_camera.IsPlaying)
                    {
                        PlaceInWorld(cameraPose);
                        Tracker.Update(m_observations, Time.time);
                        float elapsed = Mathf.Max(Time.realtimeSinceStartup - started, 0.001f);
                        DetectionsPerSecond = Mathf.Lerp(DetectionsPerSecond, 1f / elapsed, 0.1f);
                        FrameProcessed?.Invoke();
                    }
                }

                float remaining = minInterval - (Time.realtimeSinceStartup - started);
                if (remaining > 0f)
                {
                    yield return new WaitForSecondsRealtime(remaining);
                }
                else
                {
                    yield return null;
                }
            }
        }

        private void PlaceInWorld(Pose cameraPose)
        {
            m_observations.Clear();
            foreach (var detection in m_detections)
            {
                if (TryPlace(detection, cameraPose, out var observation))
                {
                    m_observations.Add(observation);
                }
            }
        }

        private bool TryPlace(in Detection detection, Pose cameraPose, out WorldObservation observation)
        {
            observation = default;
            var box = detection.Box;
            var centerRay = m_camera.ViewportPointToRay(ToViewport(box.center), cameraPose);

            float depthDistance = MeasureDepth(box, cameraPose);
            float sizeDistance = EstimateDistanceFromSize(detection.ClassId, box, cameraPose);

            // Depth is coarse: small parts held in the hand are often missed and the ray lands on
            // the table or wall behind. If the part's real size says it must be much closer, trust that.
            float distance = depthDistance;
            if (sizeDistance > 0f && (depthDistance <= 0f || depthDistance > 1.5f * sizeDistance))
            {
                distance = sizeDistance;
            }
            if (distance <= 0f)
            {
                return false;
            }
            distance = Mathf.Clamp(distance, 0.15f, m_maxDepthMeters);

            // Stand a rectangle up at that distance, facing the camera, and size it by where
            // the rays through two opposite corners of the box cross its plane.
            var center = centerRay.GetPoint(distance);
            var plane = new Plane(centerRay.direction, center);
            var cornerRayA = m_camera.ViewportPointToRay(ToViewport(box.min), cameraPose);
            var cornerRayB = m_camera.ViewportPointToRay(ToViewport(box.max), cameraPose);
            if (!plane.Raycast(cornerRayA, out float cornerDistanceA) || !plane.Raycast(cornerRayB, out float cornerDistanceB))
            {
                return false;
            }

            var rotation = Quaternion.LookRotation(centerRay.direction, Vector3.up);
            var diagonal = Quaternion.Inverse(rotation) * (cornerRayB.GetPoint(cornerDistanceB) - cornerRayA.GetPoint(cornerDistanceA));
            observation = new WorldObservation
            {
                ClassId = detection.ClassId,
                Score = detection.Score,
                Position = center,
                Rotation = rotation,
                Size = new Vector2(Mathf.Abs(diagonal.x), Mathf.Abs(diagonal.y)),
            };
            return true;
        }

        /// <summary>Median distance of several depth rays through the box, or 0 if none hit.</summary>
        private float MeasureDepth(Rect box, Pose cameraPose)
        {
            if (!EnvironmentRaycastManager.IsSupported || !m_depth.isActiveAndEnabled)
            {
                return 0f;
            }

            m_depthHits.Clear();
            foreach (var sample in s_depthSamples)
            {
                var ray = m_camera.ViewportPointToRay(ToViewport(box.center + Vector2.Scale(sample, box.size)), cameraPose);
                if (m_depth.Raycast(ray, out var hit, m_maxDepthMeters))
                {
                    m_depthHits.Add(Vector3.Distance(cameraPose.position, hit.point));
                }
            }
            if (m_depthHits.Count == 0)
            {
                return 0f;
            }
            m_depthHits.Sort();
            return m_depthHits[m_depthHits.Count / 2];
        }

        /// <summary>Distance implied by how large the box looks, if the class's real size is known.</summary>
        private float EstimateDistanceFromSize(int classId, Rect box, Pose cameraPose)
        {
            float size = KnownSizeMeters?.Invoke(classId) ?? 0f;
            if (size <= 0f)
            {
                return 0f;
            }

            var center = box.center;
            float horizontal = Vector3.Angle(
                m_camera.ViewportPointToRay(ToViewport(new Vector2(box.xMin, center.y)), cameraPose).direction,
                m_camera.ViewportPointToRay(ToViewport(new Vector2(box.xMax, center.y)), cameraPose).direction);
            float vertical = Vector3.Angle(
                m_camera.ViewportPointToRay(ToViewport(new Vector2(center.x, box.yMin)), cameraPose).direction,
                m_camera.ViewportPointToRay(ToViewport(new Vector2(center.x, box.yMax)), cameraPose).direction);

            // A part seen at an angle looks shorter than it is, so the longest side of the box is
            // the best match for its real length. Diagonal parts look longest along the diagonal.
            float diagonal = Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
            float angle = Mathf.Max(horizontal, vertical) > 0.85f * diagonal ? Mathf.Max(horizontal, vertical) : diagonal;
            return DistanceEstimate.FromAngularSize(angle * Mathf.Deg2Rad, size);
        }

        // Detections have their origin at the top-left; viewport space is at the bottom-left.
        private static Vector2 ToViewport(Vector2 imagePoint) => new(imagePoint.x, 1f - imagePoint.y);

        private void SetState(PerceptionState state, string message)
        {
            State = state;
            StatusMessage = message;
        }

        // GetCameraPose() is unreliable while head tracking is unavailable, for example when
        // the headset has just been put on or woken up.
        private static bool IsHeadPoseValid()
        {
            [DllImport("OVRPlugin", CallingConvention = CallingConvention.Cdecl)]
            static extern OVRPlugin.Result ovrp_GetNodePoseStateAtTime(double time, OVRPlugin.Node nodeId, out OVRPlugin.PoseStatef nodePoseState);

            return ovrp_GetNodePoseStateAtTime(OVRPlugin.GetTimeInSeconds(), OVRPlugin.Node.Head, out _).IsSuccess();
        }

        private void OnDestroy() => m_detector?.Dispose();
    }
}
