using UnityEngine;
using DG.Tweening;

namespace DOTweenAnimationSystem
{
    /// <summary>
    /// 单个关键 Transform 数据
    /// </summary>
    [System.Serializable]
    public class TransformData
    {
        [Header("Transform属性")]
        public Vector3 position = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;

        [Header("动画设置")]
        public float duration = 1f;
        public Ease easeType = Ease.OutQuad;

        [Header("激活控制")]
        public bool enableActiveControl = false;
        public bool activeState = true;

        public TransformData()
        {
            Reset();
        }

        public TransformData(Vector3 pos, Vector3 rot, Vector3 scl, float dur = 1f, Ease ease = Ease.OutQuad)
        {
            position = pos;
            rotation = rot;
            scale = scl;
            duration = dur;
            easeType = ease;
        }

        /// <summary>
        /// 安全时长（负数归零）
        /// </summary>
        public float SafeDuration => duration < 0f ? 0f : duration;

        /// <summary>
        /// 规范化（Clamp），返回是否修改
        /// </summary>
        public bool Sanitize()
        {
            bool changed = false;
            if (duration < 0f) { duration = 0f; changed = true; }
            return changed;
        }

        public static TransformData FromTransform(Transform transform, bool useLocalSpace = true, float duration = 1f)
        {
            var data = new TransformData();
            if (useLocalSpace)
            {
                data.position = transform.localPosition;
                data.rotation = transform.localEulerAngles;
            }
            else
            {
                data.position = transform.position;
                data.rotation = transform.eulerAngles;
            }
            data.scale = transform.localScale;
            data.duration = duration;
            return data;
        }

        public void ApplyToTransform(Transform transform, bool useLocalSpace = true)
        {
            if (useLocalSpace)
            {
                transform.localPosition = position;
                transform.localEulerAngles = rotation;
            }
            else
            {
                transform.position = position;
                transform.eulerAngles = rotation;
            }
            transform.localScale = scale;
        }

        public TransformData Clone()
        {
            var c = new TransformData(position, rotation, scale, duration, easeType)
            {
                enableActiveControl = this.enableActiveControl,
                activeState = this.activeState
            };
            return c;
        }

        public static TransformData Lerp(TransformData from, TransformData to, float t)
        {
            // 欧拉角插值潜在跳变后续批次可选优化
            return new TransformData(
                Vector3.Lerp(from.position, to.position, t),
                Vector3.Lerp(from.rotation, to.rotation, t),
                Vector3.Lerp(from.scale, to.scale, t),
                Mathf.Lerp(from.duration, to.duration, t),
                from.easeType
            );
        }

        public void Reset()
        {
            position = Vector3.zero;
            rotation = Vector3.zero;
            scale = Vector3.one;
            duration = 1f;
            easeType = Ease.OutQuad;
            enableActiveControl = false;
            activeState = true;
        }

        public override string ToString()
        {
            return $"Pos:{position}, Rot:{rotation}, Scale:{scale}, Duration:{duration}s, Ease:{easeType}";
        }
    }
}
