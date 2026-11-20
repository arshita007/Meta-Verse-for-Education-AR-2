using System;
using System.Collections.Generic;
using UnityEngine;

namespace VipAr.Procedures
{
    /// <summary>Where a recognized part currently is, as reported by the perception pipeline.</summary>
    public struct PartSighting
    {
        public string PartId;
        public Vector3 Position;
        public Vector2 Size;
    }

    public enum StepPhase
    {
        /// <summary>Waiting for the user to do the step.</summary>
        Working,

        /// <summary>The AI confirmed the step; showing success briefly before moving on.</summary>
        Verified,

        /// <summary>Every step is done.</summary>
        Finished,
    }

    /// <summary>
    /// Runs a procedure one step at a time: show the instruction, check the AI's sightings against
    /// the step's completion rule, and advance when it's met. The user can always go back, or
    /// press Next to move on when the AI can't confirm a step.
    /// </summary>
    public sealed class StepManager
    {
        /// <summary>How long the success state shows before the next step appears.</summary>
        public float VerifiedDisplaySeconds = 1.5f;

        /// <summary>Extra slack, in meters, when deciding whether two parts are touching.</summary>
        public float TouchToleranceMeters = 0.03f;

        public event Action StepChanged;

        private readonly HashSet<string> m_seenThisStep = new();
        private readonly HashSet<string> m_visibleNow = new();
        private float m_conditionMetSince = -1f;
        private float m_verifiedAt;

        public StepManager(ProcedureDefinition procedure)
        {
            Procedure = procedure ?? throw new ArgumentNullException(nameof(procedure));
        }

        public ProcedureDefinition Procedure { get; }
        public int StepIndex { get; private set; }
        public StepPhase Phase { get; private set; }
        public StepDefinition CurrentStep => Procedure.steps[Mathf.Min(StepIndex, Procedure.steps.Length - 1)];
        public bool IsFirstStep => StepIndex == 0;

        /// <summary>0..1 progress toward the current step's hold time. 1 once verified.</summary>
        public float HoldProgress { get; private set; }

        /// <summary>Parts recognized at least once during this step (for checklists).</summary>
        public IReadOnlyCollection<string> SeenThisStep => m_seenThisStep;

        /// <summary>Parts recognized in the most recent update.</summary>
        public IReadOnlyCollection<string> VisibleNow => m_visibleNow;

        /// <summary>Parts the overlay should highlight for the current step.</summary>
        public IReadOnlyList<string> PartsToShow
        {
            get
            {
                var step = CurrentStep;
                if (step.showParts != null && step.showParts.Length > 0)
                {
                    return step.showParts;
                }
                return step.check.parts ?? Array.Empty<string>();
            }
        }

        public bool IsAutomatic => CurrentStep.check.type != CheckDefinition.Manual;

        /// <summary>Feed the latest sightings. Call once per processed camera image, and also every frame (with the last sightings) so timers advance.</summary>
        public void Update(IReadOnlyList<PartSighting> sightings, float now)
        {
            if (Phase == StepPhase.Finished)
            {
                return;
            }

            if (Phase == StepPhase.Verified)
            {
                if (now - m_verifiedAt >= VerifiedDisplaySeconds)
                {
                    Advance();
                }
                return;
            }

            m_visibleNow.Clear();
            foreach (var sighting in sightings)
            {
                m_visibleNow.Add(sighting.PartId);
                m_seenThisStep.Add(sighting.PartId);
            }

            var check = CurrentStep.check;
            bool met = check.type switch
            {
                CheckDefinition.PartsSeen => AllIn(check.parts, m_seenThisStep),
                CheckDefinition.PartsVisible => AllIn(check.parts, m_visibleNow),
                CheckDefinition.PartsTogether => AllTouching(check.parts, sightings),
                _ => false,
            };

            if (!met)
            {
                m_conditionMetSince = -1f;
                HoldProgress = 0f;
                return;
            }

            if (m_conditionMetSince < 0f)
            {
                m_conditionMetSince = now;
            }

            float hold = check.type == CheckDefinition.PartsSeen ? 0f : Mathf.Max(0f, check.holdSeconds);
            HoldProgress = hold <= 0f ? 1f : Mathf.Clamp01((now - m_conditionMetSince) / hold);
            if (HoldProgress >= 1f)
            {
                Phase = StepPhase.Verified;
                m_verifiedAt = now;
                StepChanged?.Invoke();
            }
        }

        /// <summary>The user confirms the step themselves. Works for every step type.</summary>
        public void Next() => Advance();

        public void Back()
        {
            if (StepIndex == 0 && Phase != StepPhase.Finished)
            {
                return;
            }
            StepIndex = Phase == StepPhase.Finished ? Procedure.steps.Length - 1 : StepIndex - 1;
            EnterStep();
        }

        public void Restart()
        {
            StepIndex = 0;
            EnterStep();
        }

        private void Advance()
        {
            if (Phase == StepPhase.Finished)
            {
                return;
            }

            if (StepIndex + 1 >= Procedure.steps.Length)
            {
                Phase = StepPhase.Finished;
                HoldProgress = 1f;
                StepChanged?.Invoke();
                return;
            }

            StepIndex++;
            EnterStep();
        }

        private void EnterStep()
        {
            Phase = StepPhase.Working;
            m_seenThisStep.Clear();
            m_visibleNow.Clear();
            m_conditionMetSince = -1f;
            HoldProgress = 0f;
            StepChanged?.Invoke();
        }

        private static bool AllIn(string[] parts, HashSet<string> set)
        {
            if (parts == null || parts.Length == 0)
            {
                return false;
            }
            foreach (var part in parts)
            {
                if (!set.Contains(part))
                {
                    return false;
                }
            }
            return true;
        }

        private bool AllTouching(string[] parts, IReadOnlyList<PartSighting> sightings)
        {
            if (parts == null || parts.Length < 2)
            {
                return false;
            }

            // Every listed part must be visible, and each one must touch at least one other.
            // For two parts that simply means they touch each other.
            foreach (var part in parts)
            {
                if (!TryFind(sightings, part, out var a))
                {
                    return false;
                }

                bool touchesAnother = false;
                foreach (var other in parts)
                {
                    if (other != part && TryFind(sightings, other, out var b) && AreTouching(a, b))
                    {
                        touchesAnother = true;
                        break;
                    }
                }
                if (!touchesAnother)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Treats each part as a sphere the size of its longest side.</summary>
        public bool AreTouching(in PartSighting a, in PartSighting b)
        {
            float radiusA = 0.5f * Mathf.Max(a.Size.x, a.Size.y);
            float radiusB = 0.5f * Mathf.Max(b.Size.x, b.Size.y);
            return Vector3.Distance(a.Position, b.Position) <= radiusA + radiusB + TouchToleranceMeters;
        }

        private static bool TryFind(IReadOnlyList<PartSighting> sightings, string partId, out PartSighting found)
        {
            foreach (var sighting in sightings)
            {
                if (sighting.PartId == partId)
                {
                    found = sighting;
                    return true;
                }
            }
            found = default;
            return false;
        }
    }
}
