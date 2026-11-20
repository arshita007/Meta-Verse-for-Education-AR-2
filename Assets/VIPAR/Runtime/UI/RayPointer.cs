using System.Collections.Generic;
using UnityEngine;

namespace VipAr.UI
{
    /// <summary>
    /// Point at panel buttons with a controller (trigger or A/X to click) or a tracked hand
    /// (pinch thumb and index to click). The ray only shows while it's on a panel, so it doesn't
    /// clutter the view while you work with your hands.
    /// </summary>
    public sealed class RayPointer : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig m_rig;
        [SerializeField] private OVRHand m_leftHand;
        [SerializeField] private OVRHand m_rightHand;
        [SerializeField] private Material m_lineMaterial;
        [SerializeField] private float m_maxDistance = 3f;

        private sealed class Source
        {
            public OVRInput.Controller Controller;
            public OVRHand Hand;
            public Transform ControllerAnchor;
            public LineRenderer Line;
            public bool WasPinching;
        }

        private Source[] m_sources;
        private readonly HashSet<PanelButton> m_hovered = new();
        private readonly HashSet<PanelButton> m_hoveredLastFrame = new();

        private void Start()
        {
            m_sources = new[]
            {
                CreateSource(OVRInput.Controller.LTouch, m_leftHand, m_rig.leftControllerAnchor, "Left Pointer"),
                CreateSource(OVRInput.Controller.RTouch, m_rightHand, m_rig.rightControllerAnchor, "Right Pointer"),
            };
        }

        private Source CreateSource(OVRInput.Controller controller, OVRHand hand, Transform anchor, string name)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = m_lineMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.004f;
            line.endWidth = 0.0015f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return new Source { Controller = controller, Hand = hand, ControllerAnchor = anchor, Line = line };
        }

        private void Update()
        {
            m_hovered.Clear();
            foreach (var source in m_sources)
            {
                UpdateSource(source);
            }

            foreach (var button in m_hoveredLastFrame)
            {
                if (button != null && !m_hovered.Contains(button))
                {
                    button.SetHovered(false);
                }
            }
            m_hoveredLastFrame.Clear();
            m_hoveredLastFrame.UnionWith(m_hovered);
        }

        private void UpdateSource(Source source)
        {
            bool usingController = OVRInput.IsControllerConnected(source.Controller);
            bool pinching = !usingController && source.Hand != null && source.Hand.IsTracked && source.Hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            bool pinchStarted = pinching && !source.WasPinching;
            source.WasPinching = pinching;

            if (!TryGetRay(source, usingController, out var ray) ||
                !Physics.Raycast(ray, out var hit, m_maxDistance, 1 << UiKit.Layer, QueryTriggerInteraction.Collide))
            {
                source.Line.enabled = false;
                return;
            }

            source.Line.enabled = true;
            source.Line.SetPosition(0, ray.origin);
            source.Line.SetPosition(1, hit.point);

            var button = hit.collider.GetComponent<PanelButton>();
            if (button == null)
            {
                return;
            }
            m_hovered.Add(button);
            button.SetHovered(true);

            bool clicked = usingController
                ? OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, source.Controller) || OVRInput.GetDown(OVRInput.Button.One, source.Controller)
                : pinchStarted;
            if (clicked)
            {
                button.Click();
            }
        }

        private static bool TryGetRay(Source source, bool usingController, out Ray ray)
        {
            if (usingController && source.ControllerAnchor != null)
            {
                ray = new Ray(source.ControllerAnchor.position, source.ControllerAnchor.forward);
                return true;
            }
            if (source.Hand != null && source.Hand.IsTracked && source.Hand.IsPointerPoseValid)
            {
                var pose = source.Hand.PointerPose;
                ray = new Ray(pose.position, pose.forward);
                return true;
            }
            ray = default;
            return false;
        }
    }
}
