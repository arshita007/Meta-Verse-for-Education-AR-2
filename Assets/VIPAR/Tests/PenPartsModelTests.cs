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
    /// <summary>
    /// Runs the custom-trained pen model, through the same decoder the headset uses, on held-out
    /// headset photos (never seen in training). Catches export, import, and decoding mistakes.
    /// </summary>
    public class PenPartsModelTests
    {
        private const string ModelPath = "Assets/VIPAR/Model/pen_parts.onnx";
        private const string LabelsPath = "Assets/VIPAR/Model/pen_parts_labels.txt";
        private const string DataFolder = "Assets/VIPAR/Tests/Data/pen";

        private static IEnumerable<string> Photos() =>
            Directory.Exists(DataFolder) ? Directory.GetFiles(DataFolder, "*.jpg").OrderBy(p => p) : Enumerable.Empty<string>();

        [UnityTest]
        public IEnumerator Detect_FindsExpectedPartInEachPhoto()
        {
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            if (model == null)
            {
                Assert.Ignore("Pen model not installed");
            }
            var labels = AssetDatabase.LoadAssetAtPath<TextAsset>(LabelsPath).text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

            int correct = 0, total = 0;
            var misses = new List<string>();
            using (var detector = new YoloRawDetector(model, labels, BackendType.CPU))
            {
                detector.WarmUp();
                foreach (var path in Photos())
                {
                    // Files are named <expected class>__<anything>.jpg.
                    string expected = Path.GetFileNameWithoutExtension(path).Split(new[] { "__" }, System.StringSplitOptions.None)[0];
                    var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    image.LoadImage(File.ReadAllBytes(path));

                    var results = new List<Detection>();
                    yield return detector.Detect(image, 0.4f, 0.5f, results);
                    Object.DestroyImmediate(image);

                    total++;
                    var best = results.OrderByDescending(d => d.Score).FirstOrDefault();
                    string found = results.Count > 0 ? $"{labels[best.ClassId]} {best.Score:0.00}" : "nothing";
                    Debug.Log($"[VIPAR] {Path.GetFileName(path)}: expected {expected}, found {found}");
                    if (results.Any(d => labels[d.ClassId] == expected))
                    {
                        correct++;
                    }
                    else
                    {
                        misses.Add($"{Path.GetFileName(path)} -> {found}");
                    }
                }
            }

            if (total == 0)
            {
                Assert.Ignore("No pen test photos");
            }
            Debug.Log($"[VIPAR] Pen model: {correct}/{total} photos had the expected part. Misses: {string.Join("; ", misses)}");
            Assert.That(correct, Is.GreaterThanOrEqualTo((int)(0.7f * total)), $"too many misses: {string.Join("; ", misses)}");
        }
    }
}
