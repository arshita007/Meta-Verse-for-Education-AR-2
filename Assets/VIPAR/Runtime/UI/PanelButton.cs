using System;
using UnityEngine;
using UnityEngine.UI;

namespace VipAr.UI
{
    /// <summary>A world-space button that <see cref="RayPointer"/> can hover and click.</summary>
    [RequireComponent(typeof(Image), typeof(BoxCollider))]
    public sealed class PanelButton : MonoBehaviour
    {
        private static readonly Color s_normal = new(0.16f, 0.19f, 0.24f, 1f);
        private static readonly Color s_primary = new(0.08f, 0.45f, 0.58f, 1f);
        private static readonly Color s_disabled = new(0.12f, 0.13f, 0.15f, 0.7f);
        private const float FlashSeconds = 0.15f;

        public event Action Clicked;

        private Image m_background;
        private Text m_label;
        private BoxCollider m_collider;
        private bool m_primary;
        private bool m_hovered;
        private bool m_interactable = true;
        private float m_flashUntil;

        public string Label
        {
            get => m_label.text;
            set => m_label.text = value;
        }

        public bool Interactable
        {
            get => m_interactable;
            set
            {
                m_interactable = value;
                m_collider.enabled = value;
                Refresh();
            }
        }

        internal void Initialize(Text label, bool primary)
        {
            m_label = label;
            m_primary = primary;
            m_background = GetComponent<Image>();
            m_background.raycastTarget = false;
            m_collider = GetComponent<BoxCollider>();
            m_collider.isTrigger = true;
            Refresh();
        }

        public void SetHovered(bool hovered)
        {
            if (m_hovered != hovered)
            {
                m_hovered = hovered;
                Refresh();
            }
        }

        public void Click()
        {
            if (!m_interactable || !isActiveAndEnabled)
            {
                return;
            }
            m_flashUntil = Time.unscaledTime + FlashSeconds;
            Refresh();
            Clicked?.Invoke();
        }

        private void LateUpdate()
        {
            // The collider follows the button's on-screen size, which layout code may change.
            var size = ((RectTransform)transform).rect.size;
            m_collider.size = new Vector3(size.x, size.y, 10f);
            m_collider.center = new Vector3((0.5f - ((RectTransform)transform).pivot.x) * size.x, (0.5f - ((RectTransform)transform).pivot.y) * size.y, 0f);

            if (m_flashUntil > 0f && Time.unscaledTime > m_flashUntil)
            {
                m_flashUntil = 0f;
                Refresh();
            }
        }

        private void OnDisable()
        {
            m_hovered = false;
            m_flashUntil = 0f;
            Refresh();
        }

        private void Refresh()
        {
            if (m_background == null)
            {
                return;
            }

            Color color = !m_interactable ? s_disabled : m_primary ? s_primary : s_normal;
            if (m_interactable && m_flashUntil > 0f)
            {
                color = UiKit.Accent;
            }
            else if (m_interactable && m_hovered)
            {
                color = Color.Lerp(color, UiKit.Accent, 0.45f);
            }
            m_background.color = color;
            m_label.color = m_interactable ? UiKit.TextColor : UiKit.MutedText;
        }
    }
}
