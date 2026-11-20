using System;
using UnityEngine;
using UnityEngine.UI;

namespace VipAr.UI
{
    /// <summary>
    /// Builds world-space panels in code. Positions are in canvas units measured from the
    /// panel's top-left corner, which keeps layout code readable.
    /// </summary>
    public static class UiKit
    {
        /// <summary>Unity's built-in "UI" layer. The pointer only raycasts against this layer.</summary>
        public const int Layer = 5;

        /// <summary>Meters per canvas unit. A 600-unit-wide panel is 48 cm wide.</summary>
        public const float Scale = 0.0008f;

        // Fully opaque: with passthrough, any transparency lets the real world show through the
        // text, which made the first build's panels hard to read.
        public static readonly Color PanelColor = new(0.05f, 0.06f, 0.08f, 1f);
        public static readonly Color TextColor = new(0.94f, 0.95f, 0.97f);
        public static readonly Color MutedText = new(0.62f, 0.66f, 0.72f);
        public static readonly Color Accent = new(0.2f, 0.85f, 1f);
        public static readonly Color Success = new(0.45f, 0.95f, 0.45f);
        public static readonly Color Warning = new(1f, 0.78f, 0.3f);

        private static Font s_font;
        public static Font Font => s_font != null ? s_font : s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform CreatePanel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * Scale;

            var background = CreateImage(rect, "Background", PanelColor);
            Stretch(background.rectTransform);
            return rect;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text CreateText(Transform parent, string name, int fontSize, Color color, TextAnchor alignment = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static PanelButton CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Action onClick, bool primary = false, int fontSize = 26)
        {
            var go = new GameObject($"Button {label}", typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(PanelButton));
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, position, size);

            var text = CreateText(go.transform, "Label", fontSize, TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform);

            var button = go.GetComponent<PanelButton>();
            button.Initialize(text, primary);
            button.Label = label;
            if (onClick != null)
            {
                button.Clicked += onClick;
            }
            return button;
        }

        /// <summary>Positions <paramref name="rect"/> by its top-left corner, measured from the parent's top-left.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(position.x, -position.y);
            rect.sizeDelta = size;
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
