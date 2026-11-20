using System;
using System.Linq;
using Unity.InferenceEngine;
using UnityEngine;

namespace VipAr
{
    public enum DetectorOutputFormat
    {
        /// <summary>Meta sample's converted YOLOv9: outputs boxes, class ids and scores separately.</summary>
        BoxesClassesScores,

        /// <summary>Ultralytics ONNX export without NMS: one [1, 4 + classes, candidates] tensor.</summary>
        UltralyticsRaw,
    }

    /// <summary>One AI model plus everything needed to run it. Procedures pick one by <see cref="id"/>.</summary>
    [CreateAssetMenu(menuName = "VIP AR/Detector Profile")]
    public sealed class DetectorProfile : ScriptableObject
    {
        public string id;
        public string displayName;
        public ModelAsset model;
        [Tooltip("One class name per line, in the model's class order.")]
        public TextAsset labels;
        public DetectorOutputFormat format;
        public BackendType backend = BackendType.CPU;
        [Range(0f, 1f)] public float scoreThreshold = 0.35f;
        [Range(0f, 1f)] public float iouThreshold = 0.45f;

        public string[] ReadLabels() =>
            labels.text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

        public IObjectDetector CreateDetector()
        {
            var names = ReadLabels();
            return format switch
            {
                DetectorOutputFormat.BoxesClassesScores => new YoloDetector(model, names, backend),
                DetectorOutputFormat.UltralyticsRaw => new YoloRawDetector(model, names, backend),
                _ => throw new NotSupportedException(format.ToString()),
            };
        }
    }
}
