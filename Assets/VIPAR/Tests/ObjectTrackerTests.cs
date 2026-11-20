using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VipAr.Tests
{
    public class ObjectTrackerTests
    {
        private const int Cup = 41;
        private const int Bottle = 39;

        private static WorldObservation See(int classId, Vector3 position, float size = 0.2f) => new()
        {
            ClassId = classId,
            Score = 0.8f,
            Position = position,
            Rotation = Quaternion.identity,
            Size = Vector2.one * size,
        };

        private static int ConfirmedCount(ObjectTracker tracker) => tracker.Tracks.Count(tracker.IsConfirmed);

        [Test]
        public void NewObject_IsNotConfirmedOnFirstSighting()
        {
            var tracker = new ObjectTracker();
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            Assert.That(tracker.Tracks.Count, Is.EqualTo(1));
            Assert.That(ConfirmedCount(tracker), Is.EqualTo(0));
        }

        [Test]
        public void RepeatedSighting_ConfirmsAndKeepsSameId()
        {
            var tracker = new ObjectTracker();
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            int id = tracker.Tracks[0].Id;
            tracker.Update(new[] { See(Cup, new Vector3(0.05f, 0f, 0f)) }, 0.1f);

            Assert.That(tracker.Tracks.Count, Is.EqualTo(1));
            Assert.That(tracker.Tracks[0].Id, Is.EqualTo(id));
            Assert.That(ConfirmedCount(tracker), Is.EqualTo(1));
        }

        [Test]
        public void Update_SmoothsPositionTowardObservation()
        {
            var tracker = new ObjectTracker { Smoothing = 0.5f };
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            tracker.Update(new[] { See(Cup, new Vector3(0.1f, 0f, 0f)) }, 0.1f);
            Assert.That(tracker.Tracks[0].Position.x, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void DistantSighting_OfSameClass_IsANewObject()
        {
            var tracker = new ObjectTracker();
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            tracker.Update(new[] { See(Cup, new Vector3(2f, 0f, 0f)) }, 0.1f);
            Assert.That(tracker.Tracks.Count, Is.EqualTo(2));
        }

        [Test]
        public void TwoNearbyObjects_InOneImage_StaySeparate()
        {
            var tracker = new ObjectTracker();
            var first = See(Cup, Vector3.zero);
            var second = See(Cup, new Vector3(0.1f, 0f, 0f));
            tracker.Update(new[] { first, second }, 0f);
            tracker.Update(new[] { first, second }, 0.1f);

            Assert.That(tracker.Tracks.Count, Is.EqualTo(2));
            Assert.That(ConfirmedCount(tracker), Is.EqualTo(2));
        }

        [Test]
        public void UnseenObject_SurvivesBriefMiss_ThenIsForgotten()
        {
            var tracker = new ObjectTracker { SecondsToForget = 1f };
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);

            tracker.Update(new WorldObservation[0], 0.9f);
            Assert.That(tracker.Tracks.Count, Is.EqualTo(1));

            tracker.Update(new WorldObservation[0], 1.1f);
            Assert.That(tracker.Tracks, Is.Empty);
        }

        [Test]
        public void ClassChange_InSamePlace_ReplacesOldObject()
        {
            var tracker = new ObjectTracker();
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            tracker.Update(new[] { See(Bottle, Vector3.zero) }, 0.1f);

            Assert.That(tracker.Tracks.Count, Is.EqualTo(1));
            Assert.That(tracker.Tracks[0].ClassId, Is.EqualTo(Bottle));
        }

        [Test]
        public void DifferentClasses_SeenTogether_BothSurvive()
        {
            var tracker = new ObjectTracker();
            var cup = See(Cup, Vector3.zero);
            var bottle = See(Bottle, new Vector3(0.02f, 0f, 0f));
            tracker.Update(new[] { cup, bottle }, 0f);
            tracker.Update(new[] { cup, bottle }, 0.1f);
            Assert.That(tracker.Tracks.Count, Is.EqualTo(2));
        }

        [Test]
        public void DifferentClass_FarAway_DoesNotRemoveExistingObject()
        {
            var tracker = new ObjectTracker();
            tracker.Update(new[] { See(Cup, Vector3.zero) }, 0f);
            tracker.Update(new[] { See(Bottle, new Vector3(3f, 0f, 0f)) }, 0.1f);
            Assert.That(tracker.Tracks.Count, Is.EqualTo(2));
        }
    }
}
