using System.Collections.Generic;
using UnityEngine;

namespace VipAr
{
    /// <summary>One object found in a single camera image.</summary>
    public readonly struct Detection
    {
        public readonly int ClassId;
        public readonly float Score;

        /// <summary>Normalized to 0..1, with (0,0) at the top-left of the image.</summary>
        public readonly Rect Box;

        public Detection(int classId, float score, Rect box)
        {
            ClassId = classId;
            Score = score;
            Box = box;
        }
    }

    public static class DetectionFilter
    {
        public static float IoU(Rect a, Rect b)
        {
            float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (width <= 0f || height <= 0f)
            {
                return 0f;
            }

            float intersection = width * height;
            float union = a.width * a.height + b.width * b.height - intersection;
            return union <= 0f ? 0f : intersection / union;
        }

        /// <summary>
        /// Keeps the best-scoring box out of every overlapping group, regardless of class,
        /// so one physical object can't be highlighted twice under two different labels.
        /// <paramref name="candidates"/> is reordered in place.
        /// </summary>
        public static void NonMaxSuppression(List<Detection> candidates, float scoreThreshold, float iouThreshold, List<Detection> results)
        {
            results.Clear();
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            foreach (var candidate in candidates)
            {
                if (candidate.Score < scoreThreshold)
                {
                    break;
                }

                bool overlapsKeptBox = false;
                foreach (var kept in results)
                {
                    if (IoU(candidate.Box, kept.Box) > iouThreshold)
                    {
                        overlapsKeptBox = true;
                        break;
                    }
                }

                if (!overlapsKeptBox)
                {
                    results.Add(candidate);
                }
            }
        }
    }
}
