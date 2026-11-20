using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace VipAr.Tests
{
    public class DetectionFilterTests
    {
        private static Detection Make(int classId, float score, float x, float y, float size = 0.2f) =>
            new(classId, score, new Rect(x, y, size, size));

        private static List<Detection> Run(float scoreThreshold, float iouThreshold, params Detection[] input)
        {
            var results = new List<Detection>();
            DetectionFilter.NonMaxSuppression(new List<Detection>(input), scoreThreshold, iouThreshold, results);
            return results;
        }

        [Test]
        public void IoU_IsOneForIdenticalBoxes() =>
            Assert.That(DetectionFilter.IoU(new Rect(0, 0, 1, 1), new Rect(0, 0, 1, 1)), Is.EqualTo(1f).Within(0.0001f));

        [Test]
        public void IoU_IsZeroForSeparateBoxes() =>
            Assert.That(DetectionFilter.IoU(new Rect(0, 0, 1, 1), new Rect(2, 2, 1, 1)), Is.EqualTo(0f));

        [Test]
        public void IoU_IsZeroForBoxesThatOnlyTouch() =>
            Assert.That(DetectionFilter.IoU(new Rect(0, 0, 1, 1), new Rect(1, 0, 1, 1)), Is.EqualTo(0f));

        [Test]
        public void IoU_MatchesHandCalculation() =>
            // Overlap 1, union 2 + 2 - 1 = 3.
            Assert.That(DetectionFilter.IoU(new Rect(0, 0, 2, 1), new Rect(1, 0, 2, 1)), Is.EqualTo(1f / 3f).Within(0.0001f));

        [Test]
        public void Nms_DropsScoresBelowThreshold()
        {
            var results = Run(0.5f, 0.4f, Make(0, 0.49f, 0f, 0f), Make(1, 0.5f, 0.5f, 0.5f));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].ClassId, Is.EqualTo(1));
        }

        [Test]
        public void Nms_KeepsBestOfOverlappingBoxes()
        {
            var results = Run(0.1f, 0.4f, Make(0, 0.6f, 0f, 0f), Make(0, 0.9f, 0.01f, 0.01f));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Score, Is.EqualTo(0.9f));
        }

        [Test]
        public void Nms_SuppressesAcrossClasses()
        {
            var results = Run(0.1f, 0.4f, Make(1, 0.9f, 0f, 0f), Make(2, 0.8f, 0f, 0f));
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].ClassId, Is.EqualTo(1));
        }

        [Test]
        public void Nms_KeepsSeparateObjectsInScoreOrder()
        {
            var results = Run(0.1f, 0.4f, Make(0, 0.5f, 0f, 0f), Make(0, 0.9f, 0.5f, 0.5f));
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Score, Is.EqualTo(0.9f));
        }

        [Test]
        public void Nms_ClearsPreviousResultsWhenInputIsEmpty()
        {
            var results = new List<Detection> { Make(0, 1f, 0f, 0f) };
            DetectionFilter.NonMaxSuppression(new List<Detection>(), 0.1f, 0.4f, results);
            Assert.That(results, Is.Empty);
        }
    }
}
