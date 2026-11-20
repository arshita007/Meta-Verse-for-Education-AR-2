using System;
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace VipAr
{
    /// <summary>
    /// Runs the YOLO model from Meta's sample, which outputs boxes, class ids and scores as
    /// separate tensors. Knows nothing about the headset, so it can also run in the Editor
    /// against a test image.
    /// </summary>
    public sealed class YoloDetector : IObjectDetector
    {
        private readonly Worker m_worker;
        private readonly string[] m_labels;
        private readonly int m_inputHeight;
        private readonly int m_inputWidth;
        private readonly List<Detection> m_candidates = new();

        public YoloDetector(ModelAsset modelAsset, string[] labels, BackendType backend)
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
            for (int i = 0; i < 3; i++)
            {
                m_worker.PeekOutput(i).CompleteAllPendingOperations();
            }
        }

        public IEnumerator Detect(Texture image, float scoreThreshold, float iouThreshold, List<Detection> results)
        {
            results.Clear();

            var transform = new TextureTransform().SetDimensions(image.width, image.height, 3);
            using var input = new Tensor<float>(new TensorShape(1, 3, m_inputHeight, m_inputWidth));
            TextureConverter.ToTensor(image, input, transform);
            m_worker.Schedule(input);

            var boxesAwaiter = ((Tensor<float>)m_worker.PeekOutput(0)).ReadbackAndCloneAsync().GetAwaiter();
            while (!boxesAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var boxes = boxesAwaiter.GetResult();

            // The model reports "nothing found" as empty outputs.
            int count = boxes.shape[0];
            if (count == 0)
            {
                yield break;
            }

            var classesAwaiter = ((Tensor<int>)m_worker.PeekOutput(1)).ReadbackAndCloneAsync().GetAwaiter();
            while (!classesAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var classes = classesAwaiter.GetResult();

            var scoresAwaiter = ((Tensor<float>)m_worker.PeekOutput(2)).ReadbackAndCloneAsync().GetAwaiter();
            while (!scoresAwaiter.IsCompleted)
            {
                yield return null;
            }
            using var scores = scoresAwaiter.GetResult();

            m_candidates.Clear();
            for (int i = 0; i < count; i++)
            {
                var box = Rect.MinMaxRect(
                    boxes[i, 0] / m_inputWidth,
                    boxes[i, 1] / m_inputHeight,
                    boxes[i, 2] / m_inputWidth,
                    boxes[i, 3] / m_inputHeight);
                m_candidates.Add(new Detection(classes[i], scores[i], box));
            }

            DetectionFilter.NonMaxSuppression(m_candidates, scoreThreshold, iouThreshold, results);
        }

        public void Dispose()
        {
            for (int i = 0; i < 3; i++)
            {
                m_worker.PeekOutput(i)?.CompleteAllPendingOperations();
            }
            m_worker.Dispose();
        }
    }
}
