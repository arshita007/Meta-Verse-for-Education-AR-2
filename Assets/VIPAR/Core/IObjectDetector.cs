using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VipAr
{
    /// <summary>Anything that turns a camera image into labeled boxes.</summary>
    public interface IObjectDetector : IDisposable
    {
        /// <summary>Class names, indexed by <see cref="Detection.ClassId"/>.</summary>
        IReadOnlyList<string> Labels { get; }

        /// <summary>The first inference is slow, so do it once up front rather than mid-session.</summary>
        void WarmUp();

        /// <summary>Finds objects in <paramref name="image"/>. Spreads work over frames; yield on it from a coroutine.</summary>
        IEnumerator Detect(Texture image, float scoreThreshold, float iouThreshold, List<Detection> results);
    }

    public static class DistanceEstimate
    {
        /// <summary>
        /// How far away an object of known size must be to look as big as it does. For example, a
        /// 14 cm pen spanning 10 degrees of view is about 0.8 m away.
        /// </summary>
        public static float FromAngularSize(float angleRadians, float sizeMeters)
        {
            if (angleRadians <= 0f || sizeMeters <= 0f)
            {
                return 0f;
            }
            return sizeMeters / (2f * Mathf.Tan(0.5f * angleRadians));
        }
    }
}
