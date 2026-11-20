using System;
using UnityEngine;

namespace VipAr.Procedures
{
    /// <summary>
    /// An assembly or operation procedure, loaded from a JSON file in Resources/Procedures.
    /// Adding a new procedure means adding a new JSON file; no code changes.
    /// </summary>
    [Serializable]
    public sealed class ProcedureDefinition
    {
        public string id;
        public string title;
        public string summary;

        [Tooltip("Which detector profile to load for this procedure (DetectorProfile.id).")]
        public string detector;

        public PartDefinition[] parts = Array.Empty<PartDefinition>();
        public StepDefinition[] steps = Array.Empty<StepDefinition>();

        public static ProcedureDefinition FromJson(string json)
        {
            var procedure = JsonUtility.FromJson<ProcedureDefinition>(json);
            procedure.Validate();
            return procedure;
        }

        public PartDefinition FindPart(string partId) => Array.Find(parts, p => p.id == partId);

        /// <summary>Throws with a readable message if the file references things that don't exist.</summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title))
            {
                throw new FormatException("A procedure needs an id and a title.");
            }
            if (steps == null || steps.Length == 0)
            {
                throw new FormatException($"Procedure '{id}' has no steps.");
            }

            foreach (var part in parts)
            {
                if (part.labels == null || part.labels.Length == 0)
                {
                    throw new FormatException($"Part '{part.id}' in '{id}' needs at least one detector label.");
                }
            }

            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                step.check ??= new CheckDefinition();
                if (!CheckDefinition.IsKnownType(step.check.type))
                {
                    throw new FormatException($"Step {i + 1} of '{id}' has unknown check type '{step.check.type}'.");
                }
                foreach (var partId in step.check.parts ?? Array.Empty<string>())
                {
                    RequirePart(partId, i);
                }
                foreach (var partId in step.showParts ?? Array.Empty<string>())
                {
                    RequirePart(partId, i);
                }
            }
        }

        private void RequirePart(string partId, int stepIndex)
        {
            if (FindPart(partId) == null)
            {
                throw new FormatException($"Step {stepIndex + 1} of '{id}' refers to unknown part '{partId}'.");
            }
        }
    }

    [Serializable]
    public sealed class PartDefinition
    {
        public string id;
        public string name;

        [Tooltip("Detector labels that count as this part.")]
        public string[] labels = Array.Empty<string>();

        [Tooltip("Longest dimension in centimeters. Used to judge distance when depth is unreliable, e.g. for small parts held in the hand. 0 = unknown.")]
        public float sizeCm;

        [Tooltip("Highlight color as #RRGGBB.")]
        public string color = "#33E6FF";

        public Color GetColor() => ColorUtility.TryParseHtmlString(color, out var parsed) ? parsed : Color.cyan;
    }

    [Serializable]
    public sealed class StepDefinition
    {
        public string title;
        [TextArea] public string instruction;
        [TextArea] public string hint;

        [Tooltip("Parts to highlight during this step. Defaults to the parts in the check.")]
        public string[] showParts = Array.Empty<string>();

        public CheckDefinition check = new();
    }

    [Serializable]
    public sealed class CheckDefinition
    {
        /// <summary>The user confirms by pressing Next.</summary>
        public const string Manual = "manual";

        /// <summary>Each listed part has been recognized at least once during the step.</summary>
        public const string PartsSeen = "partsSeen";

        /// <summary>
        /// All listed parts are visible at once, held for <see cref="holdSeconds"/>. Pair this with
        /// parts that represent an assembly state, such as "refill in tip housing", to verify a step.
        /// </summary>
        public const string PartsVisible = "partsVisible";

        /// <summary>All listed parts are visible and touching each other, held for <see cref="holdSeconds"/>.</summary>
        public const string PartsTogether = "partsTogether";

        public string type = Manual;
        public string[] parts = Array.Empty<string>();
        public float holdSeconds = 1f;

        public static bool IsKnownType(string type) => type is Manual or PartsSeen or PartsVisible or PartsTogether;
    }
}
