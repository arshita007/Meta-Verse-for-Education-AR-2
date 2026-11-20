using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VipAr.Procedures;

namespace VipAr.UI
{
    /// <summary>A panel that floats in front of the user. Plain classes: <see cref="AppController"/> drives them.</summary>
    public abstract class FloatingPanel
    {
        protected readonly GameObject Root;
        protected readonly RectTransform Canvas;
        private readonly HeadFollower m_follower;

        protected FloatingPanel(Transform parent, string name, Vector2 size, Vector3 offset)
        {
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            m_follower = Root.AddComponent<HeadFollower>();
            m_follower.Offset = offset;
            Canvas = UiKit.CreatePanel(Root.transform, "Canvas", size);
            Root.SetActive(false);
        }

        public bool Visible => Root.activeSelf;

        public void SetVisible(bool visible)
        {
            if (visible && !Root.activeSelf)
            {
                m_follower.SnapNow();
            }
            Root.SetActive(visible);
        }

        protected void Resize(float height) => Canvas.sizeDelta = new Vector2(Canvas.sizeDelta.x, height);

        protected Text AddText(string name, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = UiKit.CreateText(Canvas, name, fontSize, color, alignment, style);
            UiKit.Place(text.rectTransform, position, size);
            return text;
        }
    }

    public sealed class MenuPanel : FloatingPanel
    {
        private const float Width = 640f;
        private readonly Text m_status;

        public MenuPanel(Transform parent, IReadOnlyList<ProcedureDefinition> procedures, Action<ProcedureDefinition> startGuidance, Action startExplore, Action<ProcedureDefinition> startTeach)
            : base(parent, "Main Menu", new Vector2(Width, 400f), new Vector3(0f, -0.04f, 0.7f))
        {
            AddText("Title", new Vector2(32f, 26f), new Vector2(576f, 48f), 40, UiKit.TextColor, FontStyle.Bold).text = "VIP AR Guidance";
            AddText("Subtitle", new Vector2(32f, 78f), new Vector2(576f, 34f), 24, UiKit.MutedText).text = "Choose a guided procedure, or explore.";
            m_status = AddText("Status", new Vector2(32f, 112f), new Vector2(576f, 34f), 22, UiKit.Warning);

            float y = 152f;
            foreach (var procedure in procedures)
            {
                var captured = procedure;
                UiKit.CreateButton(Canvas, $"{procedure.title}   ·   {procedure.steps.Length} steps", new Vector2(32f, y), new Vector2(576f, 64f), () => startGuidance(captured), primary: true, fontSize: 28);
                y += 76f;
            }

            UiKit.CreateButton(Canvas, "Object highlighter (free explore)", new Vector2(32f, y), new Vector2(576f, 56f), startExplore);
            y += 68f;

            foreach (var procedure in procedures)
            {
                var captured = procedure;
                UiKit.CreateButton(Canvas, $"Teach parts: {procedure.title}", new Vector2(32f, y), new Vector2(576f, 56f), () => startTeach(captured));
                y += 68f;
            }

            AddText("Footer", new Vector2(32f, y + 4f), new Vector2(576f, 30f), 20, UiKit.MutedText).text =
                "Point and pinch, or point a controller and pull the trigger.";
            Resize(y + 48f);
        }

        public void SetStatus(string message) => m_status.text = message ?? string.Empty;
    }

    public sealed class InstructionPanel : FloatingPanel
    {
        private const int MaxRows = 3;

        private readonly Text m_procedureTitle;
        private readonly Text m_stepCounter;
        private readonly RectTransform m_progressFill;
        private readonly Text m_title;
        private readonly Text m_instruction;
        private readonly Image[] m_rowBoxes = new Image[MaxRows];
        private readonly Image[] m_rowChecks = new Image[MaxRows];
        private readonly Text[] m_rowLabels = new Text[MaxRows];
        private readonly Text m_status;
        private readonly RectTransform m_holdFill;
        private readonly Text m_hint;
        private readonly PanelButton m_back;
        private readonly PanelButton m_next;
        private readonly PanelButton m_menu;

        public event Action BackClicked;
        public event Action NextClicked;
        public event Action MenuClicked;

        public InstructionPanel(Transform parent)
            // To the left of straight ahead, so it doesn't cover the parts in your hands.
            : base(parent, "Instructions", new Vector2(640f, 540f), new Vector3(-0.26f, 0.02f, 0.62f))
        {
            m_procedureTitle = AddText("Procedure", new Vector2(28f, 20f), new Vector2(380f, 30f), 22, UiKit.MutedText);
            m_stepCounter = AddText("Counter", new Vector2(412f, 20f), new Vector2(200f, 30f), 22, UiKit.Accent, FontStyle.Bold, TextAnchor.UpperRight);

            var track = UiKit.CreateImage(Canvas, "Progress", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Place(track.rectTransform, new Vector2(28f, 56f), new Vector2(584f, 8f));
            var fill = UiKit.CreateImage(track.rectTransform, "Fill", UiKit.Accent);
            m_progressFill = UiKit.Place(fill.rectTransform, Vector2.zero, new Vector2(0f, 8f));

            m_title = AddText("Title", new Vector2(28f, 78f), new Vector2(584f, 46f), 36, UiKit.TextColor, FontStyle.Bold);
            m_instruction = AddText("Instruction", new Vector2(28f, 130f), new Vector2(584f, 130f), 27, UiKit.TextColor);

            for (int i = 0; i < MaxRows; i++)
            {
                float y = 268f + i * 36f;
                m_rowBoxes[i] = UiKit.CreateImage(Canvas, $"Box {i}", new Color(1f, 1f, 1f, 0.25f));
                UiKit.Place(m_rowBoxes[i].rectTransform, new Vector2(30f, y + 4f), new Vector2(24f, 24f));
                m_rowChecks[i] = UiKit.CreateImage(m_rowBoxes[i].rectTransform, "Check", UiKit.Success);
                UiKit.Place(m_rowChecks[i].rectTransform, new Vector2(4f, 4f), new Vector2(16f, 16f));
                m_rowLabels[i] = AddText($"Row {i}", new Vector2(66f, y), new Vector2(546f, 32f), 25, UiKit.TextColor);
            }

            m_status = AddText("Status", new Vector2(28f, 382f), new Vector2(584f, 34f), 25, UiKit.Accent, FontStyle.Bold);
            var holdTrack = UiKit.CreateImage(Canvas, "Hold", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Place(holdTrack.rectTransform, new Vector2(28f, 420f), new Vector2(584f, 6f));
            var holdFill = UiKit.CreateImage(holdTrack.rectTransform, "Fill", UiKit.Success);
            m_holdFill = UiKit.Place(holdFill.rectTransform, Vector2.zero, new Vector2(0f, 6f));
            m_hint = AddText("Hint", new Vector2(28f, 432f), new Vector2(584f, 40f), 20, UiKit.MutedText, FontStyle.Italic);

            m_back = UiKit.CreateButton(Canvas, "< Back", new Vector2(28f, 478f), new Vector2(170f, 50f), () => BackClicked?.Invoke());
            m_menu = UiKit.CreateButton(Canvas, "Menu", new Vector2(235f, 478f), new Vector2(170f, 50f), () => MenuClicked?.Invoke());
            m_next = UiKit.CreateButton(Canvas, "Next >", new Vector2(442f, 478f), new Vector2(170f, 50f), () => NextClicked?.Invoke(), primary: true);
        }

        public struct Row
        {
            public string Label;
            public Color Color;
            public bool Checked;
        }

        public void Show(ProcedureDefinition procedure, StepManager steps, IReadOnlyList<Row> rows, string status, Color statusColor, string hint)
        {
            int total = procedure.steps.Length;
            bool finished = steps.Phase == StepPhase.Finished;

            m_procedureTitle.text = procedure.title;
            m_stepCounter.text = finished ? "DONE" : $"STEP {steps.StepIndex + 1} OF {total}";
            float completed = finished ? total : steps.StepIndex + (steps.Phase == StepPhase.Verified ? 1 : 0);
            m_progressFill.sizeDelta = new Vector2(584f * completed / total, 8f);

            if (finished)
            {
                m_title.text = "Assembly complete!";
                m_instruction.text = $"Nice work. You finished \"{procedure.title}\".";
            }
            else
            {
                m_title.text = steps.CurrentStep.title;
                m_instruction.text = steps.CurrentStep.instruction;
            }

            for (int i = 0; i < MaxRows; i++)
            {
                bool used = !finished && i < rows.Count;
                m_rowBoxes[i].gameObject.SetActive(used);
                m_rowLabels[i].gameObject.SetActive(used);
                if (used)
                {
                    m_rowLabels[i].text = rows[i].Label;
                    m_rowLabels[i].color = Color.Lerp(rows[i].Color, Color.white, 0.25f);
                    m_rowChecks[i].enabled = rows[i].Checked;
                }
            }

            m_status.text = status;
            m_status.color = statusColor;
            m_holdFill.sizeDelta = new Vector2(584f * (finished ? 0f : steps.HoldProgress), 6f);
            m_hint.text = finished ? string.Empty : hint ?? string.Empty;

            m_back.Interactable = finished || !steps.IsFirstStep;
            m_next.Label = finished ? "Restart" : steps.IsAutomatic ? "Done >" : "Next >";
        }
    }

    public sealed class TeachPanel : FloatingPanel
    {
        private readonly Text m_status;
        private readonly RectTransform m_progressFill;
        private readonly List<(PanelButton button, string name, string id)> m_buttons = new();

        public event Action<string> PartClicked;
        public event Action BackClicked;

        public const string BackgroundId = "_background";

        public TeachPanel(Transform parent, ProcedureDefinition procedure)
            : base(parent, $"Teach {procedure.id}", new Vector2(640f, 600f), new Vector3(-0.22f, 0.02f, 0.65f))
        {
            AddText("Title", new Vector2(28f, 22f), new Vector2(584f, 44f), 34, UiKit.TextColor, FontStyle.Bold).text = $"Teach parts: {procedure.title}";
            AddText("Instruction", new Vector2(28f, 72f), new Vector2(584f, 92f), 23, UiKit.MutedText).text =
                "Pick what to record, then hold it 30 to 50 cm in front of you. Slowly turn it and move it around for 20 seconds. Record each part a few times, in different spots and lighting.";

            var items = new List<(string id, string name)>();
            foreach (var part in procedure.parts)
            {
                items.Add((part.id, part.name));
            }
            items.Add((BackgroundId, "Background (no parts)"));

            float y = 172f;
            for (int i = 0; i < items.Count; i++)
            {
                var (id, name) = items[i];
                float x = i % 2 == 0 ? 28f : 324f;
                var button = UiKit.CreateButton(Canvas, name, new Vector2(x, y), new Vector2(288f, 56f), () => PartClicked?.Invoke(id), fontSize: 22);
                m_buttons.Add((button, name, id));
                if (i % 2 == 1 || i == items.Count - 1)
                {
                    y += 66f;
                }
            }

            m_status = AddText("Status", new Vector2(28f, y + 8f), new Vector2(584f, 64f), 24, UiKit.Accent, FontStyle.Bold);
            var track = UiKit.CreateImage(Canvas, "Progress", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Place(track.rectTransform, new Vector2(28f, y + 78f), new Vector2(584f, 8f));
            var fill = UiKit.CreateImage(track.rectTransform, "Fill", UiKit.Success);
            m_progressFill = UiKit.Place(fill.rectTransform, Vector2.zero, new Vector2(0f, 8f));
            UiKit.CreateButton(Canvas, "< Back to menu", new Vector2(28f, y + 102f), new Vector2(260f, 52f), () => BackClicked?.Invoke());
            Resize(y + 174f);
        }

        public void SetCounts(Func<string, int> countFor)
        {
            foreach (var (button, name, id) in m_buttons)
            {
                int count = countFor(id);
                button.Label = count > 0 ? $"{name}  ({count})" : name;
            }
        }

        public void SetButtonsEnabled(bool enabled)
        {
            foreach (var (button, _, _) in m_buttons)
            {
                button.Interactable = enabled;
            }
        }

        public void SetStatus(string message, float progress)
        {
            m_status.text = message;
            m_progressFill.sizeDelta = new Vector2(584f * Mathf.Clamp01(progress), 8f);
        }
    }

    public sealed class ExplorePanel : FloatingPanel
    {
        private readonly Text m_status;

        public event Action MenuClicked;

        public ExplorePanel(Transform parent)
            // Low in the view, out of the way of what you're looking at.
            : base(parent, "Explore HUD", new Vector2(560f, 92f), new Vector3(0f, -0.38f, 0.75f))
        {
            m_status = AddText("Status", new Vector2(24f, 26f), new Vector2(370f, 44f), 26, UiKit.TextColor, FontStyle.Normal, TextAnchor.MiddleLeft);
            UiKit.CreateButton(Canvas, "Menu", new Vector2(404f, 20f), new Vector2(136f, 52f), () => MenuClicked?.Invoke());
        }

        public void SetStatus(string message) => m_status.text = message;
    }
}
