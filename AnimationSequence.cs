using System.Collections.Generic;
using UnityEngine;

namespace DOTweenAnimationSystem
{
    /// <summary>
    /// 动画序列
    /// </summary>
    [System.Serializable]
    public class AnimationSequence
    {
        [Header("序列设置")]
        public string sequenceName = "新序列";
        public bool isParallel = false;

        [Header("延迟设置")]
        public float delay = 0f;

        [Header("动画列表")]
        public List<AnimationData> animations = new List<AnimationData>();

        public AnimationSequence()
        {
            sequenceName = "新序列";
        }

        public AnimationSequence(string name)
        {
            sequenceName = name;
        }

        public void AddAnimation(AnimationData animation)
        {
            animations.Add(animation);
        }

        public void RemoveAnimation(int index)
        {
            if (index >= 0 && index < animations.Count)
                animations.RemoveAt(index);
        }

        public void RemoveAnimation(AnimationData animation)
        {
            animations.Remove(animation);
        }

        /// <summary>
        /// 获取序列总时长：并行取最大，顺序取和（使用安全时长）
        /// </summary>
        public float GetTotalDuration()
        {
            if (animations.Count == 0) return delay;
            if (isParallel)
            {
                float max = 0f;
                foreach (var a in animations)
                {
                    if (a == null) continue;
                    float d = a.GetTotalDuration();
                    if (d > max) max = d;
                }
                return max + delay;
            }
            else
            {
                float total = 0f;
                foreach (var a in animations)
                {
                    if (a == null) continue;
                    total += a.GetTotalDuration();
                }
                return total + delay;
            }
        }

        public bool IsValid()
        {
            if (animations.Count == 0) return false;
            foreach (var a in animations)
                if (a == null || !a.IsValid()) return false;
            return true;
        }

        public int GetValidAnimationCount()
        {
            int count = 0;
            foreach (var a in animations)
                if (a != null && a.IsValid()) count++;
            return count;
        }

        /// <summary>
        /// 统一规范化
        /// </summary>
        public void Sanitize()
        {
            if (animations == null) return;
            foreach (var a in animations)
                a?.Sanitize();
        }

        public AnimationSequence Clone()
        {
            var clone = new AnimationSequence(sequenceName + "_Copy")
            {
                isParallel = this.isParallel,
                delay = this.delay
            };
            foreach (var a in animations)
                clone.animations.Add(a.Clone());
            return clone;
        }

        public void Clear()
        {
            animations.Clear();
        }

        public override string ToString()
        {
            return $"{sequenceName} ({animations.Count}个动画, {(isParallel ? "并行" : "顺序")}, {GetTotalDuration():F1}s)";
        }
    }
}
