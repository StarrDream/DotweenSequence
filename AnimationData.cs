using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace DOTweenAnimationSystem
{
    /// <summary>
    /// 动画数据类
    /// </summary>
    [System.Serializable]
    public class AnimationData
    {
        [Header("基本设置")] public string animationName = "动画";
        [JsonIgnore] public GameObject targetObject;
        public string targetObjectPath;
        public bool useLocalSpace = true;

        [Header("Transform数据")] public TransformData startTransform = new TransformData();
        public List<TransformData> targetTransforms = new List<TransformData>();

        [Header("事件回调")]
        [JsonIgnore] public UnityEvent onAnimationStart = new UnityEvent();
        [JsonIgnore] public UnityEvent onAnimationUpdate = new UnityEvent();
        [JsonIgnore] public UnityEvent onAnimationComplete = new UnityEvent();

        public AnimationData()
        {
            animationName = "新动画";
            AddTargetTransform();
        }

        public AnimationData(string name)
        {
            animationName = name;
            AddTargetTransform();
        }

        public void AddTargetTransform()
        {
            targetTransforms.Add(new TransformData());
        }

        public void RemoveTargetTransform(int index)
        {
            if (index >= 0 && index < targetTransforms.Count && targetTransforms.Count > 1)
            {
                targetTransforms.RemoveAt(index);
            }
        }

        /// <summary>
        /// 使用安全时长累加
        /// </summary>
        public float GetTotalDuration()
        {
            float totalDuration = 0f;
            foreach (var target in targetTransforms)
                if (target != null)
                    totalDuration += target.SafeDuration;
            return totalDuration;
        }

        public void CaptureStartTransform()
        {
            if (targetObject == null) return;
            var transform = targetObject.transform;

            if (useLocalSpace)
            {
                startTransform.position = transform.localPosition;
                startTransform.rotation = transform.localEulerAngles;
            }
            else
            {
                startTransform.position = transform.position;
                startTransform.rotation = transform.eulerAngles;
            }
            startTransform.scale = transform.localScale;
        }

        public void ApplyStartTransform()
        {
            if (targetObject == null) return;
            startTransform.ApplyToTransform(targetObject.transform, useLocalSpace);
        }

        /// <summary>
        /// 校验合法性
        /// </summary>
        public bool IsValid()
        {
            if (targetObject == null) return false;
            if (targetTransforms == null || targetTransforms.Count == 0) return false;
            foreach (var t in targetTransforms)
            {
                if (t == null) return false;
                if (t.SafeDuration < 0f) return false;
            }
            return true;
        }

        /// <summary>
        /// 规范化时长 + 清除重复引用
        /// </summary>
        public void Sanitize()
        {
            if (startTransform != null) startTransform.Sanitize();
            if (targetTransforms == null) targetTransforms = new List<TransformData>();

            // 对每个 TransformData 做基础 Sanitize
            foreach (var t in targetTransforms)
                t?.Sanitize();

            // 确保列表非空
            if (targetTransforms.Count == 0)
                targetTransforms.Add(new TransformData());

            // 去重：如果同一引用对象被重复使用，克隆后替换，避免编辑器“前插/后插”旧逻辑遗留造成的共享引用
            EnsureDistinctTransformDataReferences();
        }

        /// <summary>
        /// 确保 targetTransforms 中每个元素引用唯一（若发现后续重复引用则 Clone）
        /// </summary>
        private void EnsureDistinctTransformDataReferences()
        {
            var seen = new HashSet<TransformData>();
            for (int i = 0; i < targetTransforms.Count; i++)
            {
                var td = targetTransforms[i];
                if (td == null)
                {
                    targetTransforms[i] = new TransformData();
                    continue;
                }
                if (seen.Contains(td))
                {
                    // 发现重复引用 -> 克隆
                    targetTransforms[i] = td.Clone();
                }
                else
                {
                    seen.Add(td);
                }
            }
        }

        public AnimationData Clone()
        {
            var clone = new AnimationData(animationName + "_Copy")
            {
                targetObject = this.targetObject,
                useLocalSpace = this.useLocalSpace,
                startTransform = this.startTransform.Clone()
            };
            clone.targetTransforms.Clear();
            foreach (var target in this.targetTransforms)
                clone.targetTransforms.Add(target.Clone());
            return clone;
        }
    }
}
