using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace VipAr
{
    /// <summary>
    /// Runs an Ultralytics-format YOLO model exported to ONNX without built-in NMS, such as an
    /// open-vocabulary YOLO-World or YOLOE model with a custom vocabulary baked in.
    /// </summary>
    public sealed class YoloRawDetector : IObjectDetector
    {
        private readonly Worker m_worker;
        private readonly string[] m_labels;
        private readonly int m_inputHeight;
        private readonly int m_inputWidth;
        private readonly List<Detection> m_candidates = new();

        public YoloRawDetector(ModelAsset modelAsset, string[] labels, BackendType backend)
        {
            m_labels = labels;
            var model = ModelLoader.Load(modelAsset);
            var inputShape = model.inputs[0].shape;
            m_inputHeight = inputShape.Get(2);
            m_inputWidth = inputShape.Get(3);
            m_worker = new Worker(model, backend);
        }

        public IReadOnlyList<string> Labels => m_labels;

        public void WarmUp()
        {
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputHeight, m_inputWidth));
            m_worker.Schedule(input);
            m_worker.PeekOutput(0).CompleteAllPendingOperations();
        }

        public IEnumerator Detect(Texture image, float scoreThreshold, float iouThreshold, List<Detection> results)
        {
            results.Clear();

            // The camera image is stretched to the model's input size. Exporting the model with the
            // camera's 4:3 aspect ratio avoids any distortion.
            var transform = new TextureTransform().SetDimensions(image.width, image.height, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputHeight, m_inputWidth));
            TextureConverter.ToTensor(image, input, transform);
            m_worker.Schedule(input);

            var awaiter = ((Tensor<float>)m_worker.PeekOutput(0)).ReadbackAndCloneAsync().GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }
            using var output = awaiter.GetResult();

            m_candidates.Clear();
            YoloOutputDecoder.Decode(output.AsReadOnlyNativeArray().AsReadOnlySpan(), output.shape[1], m_labels.Length, m_inputWidth, m_inputHeight, scoreThreshold, m_candidates);
            DetectionFilter.NonMaxSuppression(m_candidates, scoreThreshold, iouThreshold, results);
        }

        public void Dispose()
        {
            m_worker.PeekOutput(0)?.CompleteAllPendingOperations();
            m_worker.Dispose();
        }
    }
}
