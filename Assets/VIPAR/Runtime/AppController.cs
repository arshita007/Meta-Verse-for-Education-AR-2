using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VipAr.Procedures;
using VipAr.UI;

namespace VipAr
{
    /// <summary>
    /// Top-level app flow: launch menu, then one of
    ///  - guidance: step-by-step instructions, checked against what the AI sees,
    ///  - explore: highlight everything the general-purpose model knows,
    ///  - teach: record photos of a procedure's parts for training its AI model.
    /// </summary>
    public sealed class AppController : MonoBehaviour
    {
        [SerializeField] private ObjectDetectionPipeline m_pipeline;
        [SerializeField] private HighlightRenderer m_highlights;
        [Tooltip("All available AI models. Procedures pick one by id.")]
        [SerializeField] private DetectorProfile[] m_profiles = Array.Empty<DetectorProfile>();
        [SerializeField] private string m_exploreProfileId = "coco";

        [Header("Teach mode")]
        [SerializeField] private int m_photosPerRecording = 40;
        [SerializeField] private float m_secondsBetweenPhotos = 0.5f;

        /// <summary>Objects seen within this many seconds count as visible now.</summary>
        private const float VisibleWindowSeconds = 0.6f;

        private static readonly Color[] s_explorePalette =
        {
            new(0.20f, 0.90f, 1.00f), new(1.00f, 0.80f, 0.20f), new(0.45f, 1.00f, 0.45f), new(1.00f, 0.45f, 0.65f),
            new(0.70f, 0.60f, 1.00f), new(1.00f, 0.60f, 0.30f), new(0.40f, 0.85f, 0.75f), new(0.95f, 0.95f, 0.45f),
        };

        private enum Mode { Menu, Guidance, Explore, Teach }

        private readonly List<ProcedureDefinition> m_procedures = new();
        private readonly List<PartSighting> m_sightings = new();
        private readonly List<InstructionPanel.Row> m_rows = new();
        private Mode m_mode;
        private MenuPanel m_menu;
        private InstructionPanel m_instructions;
        private ExplorePanel m_explore;
        private TeachPanel m_teach;
        private Coroutine m_recording;

        private ProcedureDefinition m_procedure;
        private StepManager m_steps;
        private bool m_hasDetector;

        private void Awake()
        {
            LoadProcedures();

            m_menu = new MenuPanel(transform, m_procedures, StartGuidance, StartExplore, StartTeach);
            m_instructions = new InstructionPanel(transform);
            m_instructions.BackClicked += () => m_steps?.Back();
            m_instructions.NextClicked += OnNext;
            m_instructions.MenuClicked += ShowMenu;
            m_explore = new ExplorePanel(transform);
            m_explore.MenuClicked += ShowMenu;
        }

        private void Start() => ShowMenu();

        private void LoadProcedures()
        {
            foreach (var file in Resources.LoadAll<TextAsset>("Procedures"))
            {
                try
                {
                    m_procedures.Add(ProcedureDefinition.FromJson(file.text));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[VIPAR] Skipping procedure file '{file.name}': {exception.Message}");
                }
            }
            m_procedures.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.OrdinalIgnoreCase));
        }

        private DetectorProfile FindProfile(string id) =>
            string.IsNullOrEmpty(id) ? null : m_profiles.FirstOrDefault(p => p != null && p.id == id);

        // ---------------------------------------------------------------- Mode switching

        private void ShowMenu()
        {
            StopRecording();
            m_mode = Mode.Menu;
            m_pipeline.Stop();
            m_highlights.Hide();
            m_procedure = null;
            m_steps = null;
            SetPanels(menu: true);
        }

        private void StartGuidance(ProcedureDefinition procedure)
        {
            m_mode = Mode.Guidance;
            m_procedure = procedure;
            m_steps = new StepManager(procedure);

            var profile = FindProfile(procedure.detector);
            m_hasDetector = profile != null;
            if (m_hasDetector)
            {
                m_pipeline.KnownSizeMeters = classId => PartForClass(classId)?.sizeCm / 100f ?? 0f;
                m_pipeline.Run(profile);
                m_highlights.Show(m_pipeline.Tracker, StyleGuidance);
            }
            else
            {
                Debug.LogWarning($"[VIPAR] No detector '{procedure.detector}' installed for '{procedure.id}'. Steps advance manually.");
                m_pipeline.Stop();
                m_highlights.Hide();
            }
            SetPanels(instructions: true);
        }

        private void StartExplore()
        {
            m_mode = Mode.Explore;
            var profile = FindProfile(m_exploreProfileId);
            m_pipeline.KnownSizeMeters = null;
            if (profile != null)
            {
                m_pipeline.Run(profile);
                m_highlights.Show(m_pipeline.Tracker, StyleExplore);
            }
            SetPanels(explore: true);
        }

        private void StartTeach(ProcedureDefinition procedure)
        {
            m_mode = Mode.Teach;
            m_procedure = procedure;
            m_pipeline.Stop(); // the camera keeps running; the AI model isn't needed
            m_highlights.Hide();

            if (m_teach != null)
            {
                m_teach.SetVisible(false);
            }
            m_teach = new TeachPanel(transform, procedure);
            m_teach.PartClicked += partId => m_recording = StartCoroutine(Record(procedure, partId));
            m_teach.BackClicked += ShowMenu;
            m_teach.SetCounts(partId => CountPhotos(procedure, partId));
            m_teach.SetStatus("Pick a part to record.", 0f);
            SetPanels(teach: true);
        }

        private void SetPanels(bool menu = false, bool instructions = false, bool explore = false, bool teach = false)
        {
            m_menu.SetVisible(menu);
            m_instructions.SetVisible(instructions);
            m_explore.SetVisible(explore);
            m_teach?.SetVisible(teach);
        }

        // ---------------------------------------------------------------- Per frame

        private void Update()
        {
            // The Menu button on the left controller always returns to the launch menu.
            if (m_mode != Mode.Menu && OVRInput.GetDown(OVRInput.Button.Start))
            {
                ShowMenu();
                return;
            }

            switch (m_mode)
            {
                case Mode.Menu:
                    m_menu.SetStatus(m_pipeline.State is PerceptionState.WaitingForPermission or PerceptionState.Unsupported ? m_pipeline.StatusMessage : string.Empty);
                    break;
                case Mode.Guidance:
                    UpdateGuidance();
                    break;
                case Mode.Explore:
                    UpdateExplore();
                    break;
            }
        }

        private void UpdateGuidance()
        {
            CollectSightings();
            m_steps.Update(m_sightings, Time.time);

            m_rows.Clear();
            foreach (var partId in m_steps.PartsToShow)
            {
                var part = m_procedure.FindPart(partId);
                m_rows.Add(new InstructionPanel.Row { Label = part.name, Color = part.GetColor(), Checked = m_steps.SeenThisStep.Contains(partId) });
            }

            var (status, color) = DescribeStatus();
            m_instructions.Show(m_procedure, m_steps, m_rows, status, color, m_steps.Phase == StepPhase.Finished ? null : m_steps.CurrentStep.hint);
        }

        private (string, Color) DescribeStatus()
        {
            if (m_steps.Phase == StepPhase.Finished)
            {
                return ("All steps done. Press Restart to go again.", UiKit.Success);
            }
            if (m_steps.Phase == StepPhase.Verified)
            {
                return ("Step complete!", UiKit.Success);
            }
            if (!m_steps.IsAutomatic)
            {
                return ("Press Next when you're done.", UiKit.Accent);
            }
            if (!m_hasDetector)
            {
                return ("No AI model for this yet. Press Done when finished.", UiKit.Warning);
            }
            if (m_pipeline.State != PerceptionState.Running)
            {
                return (m_pipeline.StatusMessage, UiKit.Warning);
            }

            var check = m_steps.CurrentStep.check;
            string names = string.Join(", ", check.parts.Select(id => m_procedure.FindPart(id).name));
            switch (check.type)
            {
                case CheckDefinition.PartsSeen:
                    int found = check.parts.Count(m_steps.SeenThisStep.Contains);
                    return ($"Found {found} of {check.parts.Length} parts", UiKit.Accent);
                case CheckDefinition.PartsVisible:
                case CheckDefinition.PartsTogether:
                    return m_steps.HoldProgress > 0f
                        ? ("Hold it steady...", UiKit.Success)
                        : ($"When done, show the headset: {names}", UiKit.Accent);
                default:
                    return (string.Empty, UiKit.Accent);
            }
        }

        private void OnNext()
        {
            if (m_steps == null)
            {
                return;
            }
            if (m_steps.Phase == StepPhase.Finished)
            {
                m_steps.Restart();
            }
            else
            {
                m_steps.Next();
            }
        }

        private void UpdateExplore()
        {
            if (m_pipeline.State != PerceptionState.Running)
            {
                m_explore.SetStatus(m_pipeline.StatusMessage);
                return;
            }
            int count = m_pipeline.Tracker.Tracks.Count(m_pipeline.Tracker.IsConfirmed);
            m_explore.SetStatus($"{count} {(count == 1 ? "object" : "objects")}  |  {m_pipeline.DetectionsPerSecond:0} detections/s");
        }

        // ---------------------------------------------------------------- AI results -> procedure parts

        private PartDefinition PartForClass(int classId)
        {
            var labels = m_pipeline.Labels;
            if (m_procedure == null || classId < 0 || classId >= labels.Count)
            {
                return null;
            }
            string label = labels[classId];
            return m_procedure.parts.FirstOrDefault(part => part.labels.Contains(label));
        }

        private void CollectSightings()
        {
            m_sightings.Clear();
            if (!m_hasDetector)
            {
                return;
            }
            foreach (var track in m_pipeline.Tracker.Tracks)
            {
                if (!m_pipeline.Tracker.IsConfirmed(track) || Time.time - track.LastSeen > VisibleWindowSeconds)
                {
                    continue;
                }
                var part = PartForClass(track.ClassId);
                if (part != null)
                {
                    m_sightings.Add(new PartSighting { PartId = part.id, Position = track.Position, Size = track.Size });
                }
            }
        }

        private HighlightStyle? StyleGuidance(TrackedObject track)
        {
            var part = PartForClass(track.ClassId);
            if (part == null || m_steps == null || !m_steps.PartsToShow.Contains(part.id))
            {
                return null;
            }
            var color = m_steps.Phase == StepPhase.Verified ? UiKit.Success : part.GetColor();
            return new HighlightStyle(part.name, color);
        }

        private HighlightStyle? StyleExplore(TrackedObject track)
        {
            var labels = m_pipeline.Labels;
            string name = track.ClassId < labels.Count ? PrettyLabel(labels[track.ClassId]) : $"Class {track.ClassId}";
            return new HighlightStyle($"{name}  {track.Score * 100f:0}%", s_explorePalette[track.ClassId % s_explorePalette.Length]);
        }

        private static readonly Dictionary<string, string> s_labelNames = new()
        {
            ["tvmonitor"] = "Monitor", ["diningtable"] = "Table", ["pottedplant"] = "Plant", ["sofa"] = "Couch",
            ["motorbike"] = "Motorbike", ["aeroplane"] = "Airplane",
        };

        private static string PrettyLabel(string label)
        {
            if (s_labelNames.TryGetValue(label, out var name))
            {
                return name;
            }
            label = label.Replace('_', ' ');
            return label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label.Substring(1);
        }

        // ---------------------------------------------------------------- Teach mode

        private static string PhotoFolder(ProcedureDefinition procedure, string partId) =>
            Path.Combine(FrameCapture.Root, procedure.id, partId);

        private static int CountPhotos(ProcedureDefinition procedure, string partId)
        {
            var folder = PhotoFolder(procedure, partId);
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jpg").Length : 0;
        }

        private IEnumerator Record(ProcedureDefinition procedure, string partId)
        {
            string name = partId == TeachPanel.BackgroundId ? "background" : procedure.FindPart(partId).name;
            var camera = m_pipeline.CameraAccess;
            if (camera == null || !camera.IsPlaying)
            {
                m_teach.SetStatus("The camera isn't ready yet. Check the camera permission, then try again.", 0f);
                yield break;
            }

            m_teach.SetButtonsEnabled(false);
            for (int seconds = 3; seconds > 0; seconds--)
            {
                m_teach.SetStatus(partId == TeachPanel.BackgroundId
                    ? $"Put the parts away and look around your work area... {seconds}"
                    : $"Hold the {name} up in front of you... {seconds}", 0f);
                yield return new WaitForSeconds(1f);
            }

            string folder = PhotoFolder(procedure, partId);
            string session = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            int saved = 0;
            for (int i = 0; i < m_photosPerRecording; i++)
            {
                if (camera.IsPlaying)
                {
                    FrameCapture.SaveJpg(camera.GetTexture(), Path.Combine(folder, $"{session}_{i:000}.jpg"), ok => saved += ok ? 1 : 0);
                }
                m_teach.SetStatus(partId == TeachPanel.BackgroundId
                    ? $"Recording background {i + 1}/{m_photosPerRecording}. Look around slowly."
                    : $"Recording {name} {i + 1}/{m_photosPerRecording}. Slowly turn it and move it around.", (i + 1f) / m_photosPerRecording);
                yield return new WaitForSeconds(m_secondsBetweenPhotos);
            }

            yield return new WaitForSeconds(0.5f); // let the last saves finish
            m_teach.SetCounts(id => CountPhotos(procedure, id));
            m_teach.SetStatus($"Saved {saved} photos of the {name}. Pick another, or go back.", 1f);
            m_teach.SetButtonsEnabled(true);
            m_recording = null;
        }

        private void StopRecording()
        {
            if (m_recording != null)
            {
                StopCoroutine(m_recording);
                m_recording = null;
            }
        }
    }
}
