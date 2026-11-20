using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VipAr.Procedures;

namespace VipAr.Tests
{
    public class StepManagerTests
    {
        private static ProcedureDefinition Procedure(params StepDefinition[] steps) => new()
        {
            id = "test",
            title = "Test",
            parts = new[]
            {
                new PartDefinition { id = "a", name = "Part A", labels = new[] { "a" } },
                new PartDefinition { id = "b", name = "Part B", labels = new[] { "b" } },
                new PartDefinition { id = "ab", name = "A in B", labels = new[] { "ab" } },
            },
            steps = steps,
        };

        private static StepDefinition Step(string type, params string[] parts) =>
            new() { title = type, instruction = "", check = new CheckDefinition { type = type, parts = parts, holdSeconds = 1f } };

        private static PartSighting At(string part, float x, float size = 0.1f) =>
            new() { PartId = part, Position = new Vector3(x, 0f, 1f), Size = new Vector2(size, size) };

        private static readonly PartSighting[] None = Array.Empty<PartSighting>();

        private static StepManager Run(StepManager steps, IReadOnlyList<PartSighting> sightings, float from, float to)
        {
            for (float t = from; t <= to + 0.0001f; t += 0.1f)
            {
                steps.Update(sightings, t);
            }
            return steps;
        }

        [Test]
        public void PartsSeen_AccumulatesAcrossFrames_ThenVerifiesAndAdvances()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.PartsSeen, "a", "b"), Step(CheckDefinition.Manual))) { VerifiedDisplaySeconds = 0.5f };

            steps.Update(new[] { At("a", 0f) }, 0f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working), "only one of two parts seen so far");

            steps.Update(new[] { At("b", 1f) }, 0.1f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Verified), "both parts now seen, even though not at the same time");

            Run(steps, None, 0.2f, 0.7f);
            Assert.That(steps.StepIndex, Is.EqualTo(1));
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working));
        }

        [Test]
        public void PartsVisible_RequiresContinuousHold()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.PartsVisible, "ab"), Step(CheckDefinition.Manual)));
            var seen = new[] { At("ab", 0f) };

            Run(steps, seen, 0f, 0.6f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working));
            Assert.That(steps.HoldProgress, Is.GreaterThan(0.4f));

            steps.Update(None, 0.7f);
            Assert.That(steps.HoldProgress, Is.EqualTo(0f), "losing sight of the part resets the hold");

            Run(steps, seen, 0.8f, 1.9f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Verified));
        }

        [Test]
        public void PartsTogether_NeedsBothPartsTouching()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.PartsTogether, "a", "b"), Step(CheckDefinition.Manual)));

            Run(steps, new[] { At("a", 0f), At("b", 0.5f) }, 0f, 2f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working), "50 cm apart is not touching");

            Run(steps, new[] { At("a", 0f), At("b", 0.08f) }, 2.1f, 3.2f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Verified));
        }

        [Test]
        public void PartsTogether_MissingPart_IsNotMet()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.PartsTogether, "a", "b"), Step(CheckDefinition.Manual)));
            Run(steps, new[] { At("a", 0f) }, 0f, 2f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working));
        }

        [Test]
        public void ManualStep_NeverAutoAdvances_ButNextWorks()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.Manual), Step(CheckDefinition.Manual)));
            Run(steps, new[] { At("a", 0f), At("b", 0f) }, 0f, 5f);
            Assert.That(steps.StepIndex, Is.EqualTo(0));

            steps.Next();
            Assert.That(steps.StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void Next_OnLastStep_Finishes_AndBackReturnsToLastStep()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.Manual), Step(CheckDefinition.Manual)));
            steps.Next();
            steps.Next();
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Finished));

            steps.Back();
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working));
            Assert.That(steps.StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void Back_OnFirstStep_StaysPut_AndRestartReturnsToStart()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.Manual), Step(CheckDefinition.Manual)));
            steps.Back();
            Assert.That(steps.StepIndex, Is.EqualTo(0));

            steps.Next();
            steps.Restart();
            Assert.That(steps.StepIndex, Is.EqualTo(0));
        }

        [Test]
        public void EnteringAStep_ClearsWhatWasSeen()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.Manual), Step(CheckDefinition.PartsSeen, "a", "b")));
            steps.Update(new[] { At("a", 0f) }, 0f);
            steps.Next();
            steps.Update(new[] { At("b", 0f) }, 0.1f);
            Assert.That(steps.Phase, Is.EqualTo(StepPhase.Working), "part A was seen during the previous step only");
        }

        [Test]
        public void StepChanged_FiresOnVerifyAndAdvance()
        {
            var steps = new StepManager(Procedure(Step(CheckDefinition.PartsSeen, "a"), Step(CheckDefinition.Manual))) { VerifiedDisplaySeconds = 0.2f };
            int changes = 0;
            steps.StepChanged += () => changes++;
            steps.Update(new[] { At("a", 0f) }, 0f);
            Run(steps, None, 0.1f, 0.4f);
            Assert.That(changes, Is.EqualTo(2));
        }
    }

    public class ProcedureDefinitionTests
    {
        [Test]
        public void PenAssembly_LoadsAndValidates()
        {
            var file = Resources.Load<TextAsset>("Procedures/pen_assembly");
            Assert.That(file, Is.Not.Null);
            var procedure = ProcedureDefinition.FromJson(file.text);

            Assert.That(procedure.id, Is.EqualTo("pen_assembly"));
            Assert.That(procedure.steps.Length, Is.EqualTo(4));
            Assert.That(procedure.parts.Length, Is.EqualTo(5));
            Assert.That(procedure.steps[0].check.type, Is.EqualTo(CheckDefinition.PartsSeen));
            Assert.That(procedure.FindPart("refill").sizeCm, Is.EqualTo(11f));
        }

        [Test]
        public void UnknownPartInStep_IsRejectedWithClearMessage()
        {
            const string json = "{\"id\":\"x\",\"title\":\"X\",\"parts\":[{\"id\":\"a\",\"labels\":[\"a\"]}],\"steps\":[{\"title\":\"s\",\"check\":{\"type\":\"partsSeen\",\"parts\":[\"nope\"]}}]}";
            var error = Assert.Throws<FormatException>(() => ProcedureDefinition.FromJson(json));
            Assert.That(error.Message, Does.Contain("nope"));
        }

        [Test]
        public void UnknownCheckType_IsRejected()
        {
            const string json = "{\"id\":\"x\",\"title\":\"X\",\"parts\":[],\"steps\":[{\"title\":\"s\",\"check\":{\"type\":\"magic\"}}]}";
            Assert.Throws<FormatException>(() => ProcedureDefinition.FromJson(json));
        }

        [Test]
        public void MissingCheck_DefaultsToManual()
        {
            const string json = "{\"id\":\"x\",\"title\":\"X\",\"steps\":[{\"title\":\"s\"}]}";
            var procedure = ProcedureDefinition.FromJson(json);
            Assert.That(procedure.steps[0].check.type, Is.EqualTo(CheckDefinition.Manual));
        }
    }

    public class YoloOutputDecoderTests
    {
        [Test]
        public void Decode_PicksBestClass_AndNormalizesBox()
        {
            // 2 classes, 3 candidates, laid out [channel][candidate].
            float[] output =
            {
                320f, 100f, 50f,   // cx
                240f, 100f, 50f,   // cy
                64f, 20f, 10f,     // w
                48f, 20f, 10f,     // h
                0.2f, 0.9f, 0.05f, // class 0
                0.7f, 0.1f, 0.05f, // class 1
            };
            var results = new List<Detection>();
            YoloOutputDecoder.Decode(output, 6, 2, 640f, 480f, 0.5f, results);

            Assert.That(results.Count, Is.EqualTo(2), "third candidate is below threshold");
            Assert.That(results[0].ClassId, Is.EqualTo(1));
            Assert.That(results[0].Score, Is.EqualTo(0.7f));
            Assert.That(results[0].Box.center.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(results[0].Box.center.y, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(results[0].Box.width, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(results[0].Box.height, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(results[1].ClassId, Is.EqualTo(0));
        }

        [Test]
        public void Decode_IgnoresExtraChannels()
        {
            // 1 class + 2 extra mask-coefficient rows, 1 candidate.
            float[] output = { 10f, 10f, 4f, 4f, 0.8f, 99f, 99f };
            var results = new List<Detection>();
            YoloOutputDecoder.Decode(output, 7, 1, 20f, 20f, 0.5f, results);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Score, Is.EqualTo(0.8f));
        }

        [Test]
        public void Decode_RejectsMismatchedShape() =>
            Assert.Throws<ArgumentException>(() => YoloOutputDecoder.Decode(new float[10], 6, 2, 1f, 1f, 0.5f, new List<Detection>()));
    }

    public class DistanceEstimateTests
    {
        [Test]
        public void KnownSize_GivesExpectedDistance()
        {
            // A 14 cm pen spanning 10 degrees is about 80 cm away.
            float distance = DistanceEstimate.FromAngularSize(10f * Mathf.Deg2Rad, 0.14f);
            Assert.That(distance, Is.EqualTo(0.80f).Within(0.01f));
        }

        [Test]
        public void UnknownSize_GivesZero() =>
            Assert.That(DistanceEstimate.FromAngularSize(0.1f, 0f), Is.EqualTo(0f));
    }
}
