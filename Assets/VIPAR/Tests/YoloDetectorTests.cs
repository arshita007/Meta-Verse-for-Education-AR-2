using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace VipAr.Tests
{
    /// <summary>Runs the real model on a real photo: two cats lying on a couch, with two remotes.</summary>
    public class YoloDetectorTests
    {
        private const string ModelPath = "Assets/VIPAR/Model/yolov9sentis.sentis";
        private const string LabelsPath = "Assets/VIPAR/Model/SentisYoloClasses.txt";
        private const string ImagePath = "Assets/VIPAR/Tests/Data/coco_000000039769.jpg";
        private const string AnnotatedOutputPath = "Logs/vipar-detections.png";

        [UnityTest]
        public IEnumerator Detect_FindsCatsInCorrectPartOfPhoto()
        {
            var labels = AssetDatabase.LoadAssetAtPath<TextAsset>(LabelsPath).text.Split('\n').Select(l => l.Trim()).ToArray();
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.That(model, Is.Not.Null, "model asset failed to import");

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(image.LoadImage(File.ReadAllBytes(ImagePath)), Is.True);

            var results = new List<Detection>();
            using (var detector = new YoloDetector(model, labels, BackendType.CPU))
            {
                detector.WarmUp();
                yield return detector.Detect(image, 0.35f, 0.4f, results);
            }

            foreach (var detection in results)
            {
                Debug.Log($"[VIPAR] {labels[detection.ClassId]} {detection.Score:0.00} {detection.Box}");
            }
            WriteAnnotatedImage(image, results);

            var cats = results.Where(d => labels[d.ClassId] == "cat").ToList();
            Assert.That(cats.Count, Is.EqualTo(2), "expected both cats");

            // The photo is known: one cat fills the left half and one the right half.
            // A wrong scale or a flipped axis would fail these.
            var left = cats.OrderBy(c => c.Box.center.x).First();
            var right = cats.OrderBy(c => c.Box.center.x).Last();
            Assert.That(left.Box.center.x, Is.LessThan(0.5f));
            Assert.That(right.Box.center.x, Is.GreaterThan(0.5f));

            // The right cat lies in the upper part of the photo. This pins the top-left origin
            // that the viewport conversion relies on when placing boxes in the world.
            Assert.That(right.Box.yMin, Is.LessThan(0.15f));
            Assert.That(right.Box.yMax, Is.LessThan(0.9f));
            foreach (var cat in cats)
            {
                Assert.That(cat.Box.xMin, Is.InRange(-0.02f, 1.02f));
                Assert.That(cat.Box.xMax, Is.InRange(-0.02f, 1.02f));
                Assert.That(cat.Box.height, Is.GreaterThan(0.4f), "each cat is most of the photo's height");
            }

            Object.DestroyImmediate(image);
        }

        // Logs/ isn't imported or shipped. Used for checking the boxes by eye.
        private static void WriteAnnotatedImage(Texture2D source, List<Detection> detections)
        {
            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            output.SetPixels32(source.GetPixels32());
            foreach (var detection in detections)
            {
                // Detections are top-left origin; texture rows are bottom-left origin.
                int xMin = Mathf.Clamp(Mathf.RoundToInt(detection.Box.xMin * source.width), 0, source.width - 1);
                int xMax = Mathf.Clamp(Mathf.RoundToInt(detection.Box.xMax * source.width), 0, source.width - 1);
                int yMin = Mathf.Clamp(Mathf.RoundToInt((1f - detection.Box.yMax) * source.height), 0, source.height - 1);
                int yMax = Mathf.Clamp(Mathf.RoundToInt((1f - detection.Box.yMin) * source.height), 0, source.height - 1);
                for (int t = 0; t < 3; t++)
                {
                    for (int x = xMin; x <= xMax; x++)
                    {
                        output.SetPixel(x, Mathf.Min(yMin + t, source.height - 1), Color.green);
                        output.SetPixel(x, Mathf.Max(yMax - t, 0), Color.green);
                    }
                    for (int y = yMin; y <= yMax; y++)
                    {
                        output.SetPixel(Mathf.Min(xMin + t, source.width - 1), y, Color.green);
                        output.SetPixel(Mathf.Max(xMax - t, 0), y, Color.green);
                    }
                }
            }
            File.WriteAllBytes(AnnotatedOutputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);
        }
    }
}
