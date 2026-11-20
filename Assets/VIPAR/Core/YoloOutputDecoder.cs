using System;
using System.Collections.Generic;
using UnityEngine;

namespace VipAr
{
    /// <summary>
    /// Decodes the raw output of an Ultralytics-style YOLO model (YOLOv8/11, YOLO-World, YOLOE):
    /// one column per candidate box, with rows [cx, cy, w, h, class scores..., extra...].
    /// Coordinates are in input pixels; class scores are already 0..1.
    /// </summary>
    public static class YoloOutputDecoder
    {
        /// <param name="output">Flattened [channels, candidates] data from a [1, channels, candidates] tensor.</param>
        /// <param name="channels">4 + number of classes + any extra rows (e.g. mask coefficients), which are ignored.</param>
        /// <param name="inputWidth">Model input width in pixels.</param>
        /// <param name="inputHeight">Model input height in pixels.</param>
        /// <param name="results">Receives candidates above the threshold, with boxes normalized to 0..1, top-left origin. Not cleared.</param>
        public static void Decode(ReadOnlySpan<float> output, int channels, int classCount, float inputWidth, float inputHeight, float scoreThreshold, List<Detection> results)
        {
            if (classCount <= 0 || channels < 4 + classCount)
            {
                throw new ArgumentException($"Output has {channels} channels, too few for 4 box values and {classCount} classes.");
            }
            if (output.Length % channels != 0)
            {
                throw new ArgumentException($"Output length {output.Length} is not a multiple of {channels} channels.");
            }

            int candidates = output.Length / channels;
            for (int i = 0; i < candidates; i++)
            {
                int bestClass = -1;
                float bestScore = scoreThreshold;
                for (int c = 0; c < classCount; c++)
                {
                    float score = output[(4 + c) * candidates + i];
                    if (score >= bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }
                if (bestClass < 0)
                {
                    continue;
                }

                float cx = output[i];
                float cy = output[candidates + i];
                float w = output[2 * candidates + i];
                float h = output[3 * candidates + i];
                var box = Rect.MinMaxRect(
                    (cx - 0.5f * w) / inputWidth,
                    (cy - 0.5f * h) / inputHeight,
                    (cx + 0.5f * w) / inputWidth,
                    (cy + 0.5f * h) / inputHeight);
                results.Add(new Detection(bestClass, bestScore, box));
            }
        }
    }
}
