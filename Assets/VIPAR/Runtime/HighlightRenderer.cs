using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VipAr
{
    /// <summary>How a tracked object should be drawn. Return null from the styler to hide it.</summary>
    public readonly struct HighlightStyle
    {
        public readonly string Label;
        public readonly Color Color;

        public HighlightStyle(string label, Color color)
        {
            Label = label;
            Color = color;
        }
    }

    /// <summary>Draws a frame and a name label on every confirmed tracked object the styler chooses to show.</summary>
    public sealed class HighlightRenderer : MonoBehaviour
    {
        [SerializeField] private Material m_frameMaterial;
        [Tooltip("How quickly highlights glide to their newest position. Detections arrive far less often than frames are drawn.")]
        [SerializeField] private float m_followSpeed = 12f;
        [Tooltip("Frame line thickness, in meters per meter of distance, so lines look equally thick near and far.")]
        [SerializeField] private float m_lineThickness = 0.0035f;
        [Tooltip("Label scale, in meters per canvas unit per meter of distance.")]
        [SerializeField] private float m_labelScale = 0.0009f;

        private static readonly int s_color = Shader.PropertyToID("_Color");
        private static readonly int s_size = Shader.PropertyToID("_Size");
        private static readonly int s_thickness = Shader.PropertyToID("_Thickness");

        private sealed class Highlight
        {
            public GameObject Root;
            public MeshRenderer Frame;
            public RectTransform Canvas;
            public Text Label;
            public RectTransform Background;
            public MaterialPropertyBlock Properties;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector2 Size;
        }

        private readonly Dictionary<int, Highlight> m_active = new();
        private readonly Stack<Highlight> m_pool = new();
        private readonly List<int> m_stale = new();
        private readonly HashSet<int> m_seen = new();
        private ObjectTracker m_tracker;
        private Func<TrackedObject, HighlightStyle?> m_styler;
        private Mesh m_quad;
        private Transform m_head;

        /// <summary>Starts drawing <paramref name="tracker"/>'s objects, styled (or hidden) by <paramref name="styler"/>.</summary>
        public void Show(ObjectTracker tracker, Func<TrackedObject, HighlightStyle?> styler)
        {
            m_tracker = tracker;
            m_styler = styler;
        }

        public void Hide()
        {
            m_tracker = null;
            m_styler = null;
            ReleaseAll();
        }

        private void Awake() => m_quad = CreateQuad();

        private void LateUpdate()
        {
            if (m_tracker == null || m_styler == null)
            {
                return;
            }

            m_head = Camera.main != null ? Camera.main.transform : m_head;
            float blend = 1f - Mathf.Exp(-m_followSpeed * Time.deltaTime);

            m_seen.Clear();
            foreach (var track in m_tracker.Tracks)
            {
                if (!m_tracker.IsConfirmed(track))
                {
                    continue;
                }
                var style = m_styler(track);
                if (style == null)
                {
                    continue;
                }

                m_seen.Add(track.Id);
                if (!m_active.TryGetValue(track.Id, out var highlight))
                {
                    highlight = Acquire(track);
                    m_active.Add(track.Id, highlight);
                }
                Apply(highlight, track, style.Value, blend);
            }

            m_stale.Clear();
            foreach (var id in m_active.Keys)
            {
                if (!m_seen.Contains(id))
                {
                    m_stale.Add(id);
                }
            }
            foreach (var id in m_stale)
            {
                Release(id);
            }
        }

        private void Apply(Highlight highlight, TrackedObject track, HighlightStyle style, float blend)
        {
            highlight.Position = Vector3.Lerp(highlight.Position, track.Position, blend);
            highlight.Rotation = Quaternion.Slerp(highlight.Rotation, track.Rotation, blend);
            highlight.Size = Vector2.Lerp(highlight.Size, track.Size, blend);

            var frame = highlight.Frame.transform;
            frame.SetPositionAndRotation(highlight.Position, highlight.Rotation);
            frame.localScale = new Vector3(highlight.Size.x, highlight.Size.y, 1f);

            float distance = m_head != null ? Mathf.Max(Vector3.Distance(m_head.position, highlight.Position), 0.2f) : 1f;
            highlight.Properties.SetColor(s_color, style.Color);
            highlight.Properties.SetVector(s_size, highlight.Size);
            highlight.Properties.SetFloat(s_thickness, m_lineThickness * distance);
            highlight.Frame.SetPropertyBlock(highlight.Properties);

            // The label rides just above the frame, turned to face the viewer.
            highlight.Canvas.position = highlight.Position + highlight.Rotation * Vector3.up * (highlight.Size.y * 0.5f + 0.02f * distance);
            highlight.Canvas.localScale = Vector3.one * (m_labelScale * distance);
            if (m_head != null)
            {
                highlight.Canvas.rotation = Quaternion.LookRotation(highlight.Canvas.position - m_head.position, Vector3.up);
            }

            highlight.Label.text = style.Label;
            highlight.Label.color = style.Color;
            highlight.Background.sizeDelta = new Vector2(highlight.Label.preferredWidth + 28f, 48f);
        }

        private Highlight Acquire(TrackedObject track)
        {
            var highlight = m_pool.Count > 0 ? m_pool.Pop() : Create();
            highlight.Position = track.Position;
            highlight.Rotation = track.Rotation;
            highlight.Size = track.Size;
            highlight.Root.SetActive(true);
            return highlight;
        }

        private void Release(int id)
        {
            m_active[id].Root.SetActive(false);
            m_pool.Push(m_active[id]);
            m_active.Remove(id);
        }

        private void ReleaseAll()
        {
            m_stale.Clear();
            m_stale.AddRange(m_active.Keys);
            foreach (var id in m_stale)
            {
                Release(id);
            }
        }

        private Highlight Create()
        {
            var root = new GameObject("Highlight");
            root.transform.SetParent(transform, false);

            var frameObject = new GameObject("Frame", typeof(MeshFilter), typeof(MeshRenderer));
            frameObject.transform.SetParent(root.transform, false);
            frameObject.GetComponent<MeshFilter>().sharedMesh = m_quad;
            var frame = frameObject.GetComponent<MeshRenderer>();
            frame.sharedMaterial = m_frameMaterial;
            frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frame.receiveShadows = false;

            var canvasObject = new GameObject("Label", typeof(Canvas));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(10f, 10f);

            var backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            backgroundObject.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.04f, 1f);
            var background = backgroundObject.GetComponent<RectTransform>();
            background.pivot = new Vector2(0.5f, 0f);
            background.anchoredPosition = Vector2.zero;

            var label = UI.UiKit.CreateText(backgroundObject.transform, "Text", 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UI.UiKit.Stretch(label.rectTransform);
            label.gameObject.layer = 0;

            return new Highlight
            {
                Root = root,
                Frame = frame,
                Canvas = canvasRect,
                Label = label,
                Background = background,
                Properties = new MaterialPropertyBlock(),
            };
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Highlight Quad" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            // The quad gets resized and moved every frame, so don't let it be culled by a stale bound.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        private void OnDestroy()
        {
            if (m_quad != null)
            {
                Destroy(m_quad);
            }
        }
    }
}
