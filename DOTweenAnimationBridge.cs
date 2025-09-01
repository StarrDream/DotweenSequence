using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace DOTweenAnimationSystem
{
    [Serializable]
    public class BridgeAniData
    {
        [Header("对外逻辑名 (PlayAni 传入)")] public string aniName;

        [Header("单序列模式（groupSequences 为空才会使用）")]
        public string singleSequence;

        [Header("组合序列（非空则优先生效）")] public List<string> groupSequences = new();

        [Header("组合时是否并行（false=串行）")] public bool playParallel = false;

        [TextArea] public string comment;
    }

    public enum BridgePlayState
    {
        Idle,
        Playing
    }

    internal class ActiveContext
    {
        public string aniName;
        public float startTime;
        public float estimatedTotal;
        public Sequence trackingSequence; // 单序列模式下真实 DOTween Sequence 引用（用于精确进度）
    }

    /// <summary>
    /// 桥接播放：记录真实开始时间用于进度
    /// </summary>
    public class DOTweenAnimationBridge : MonoBehaviour
    {
        [Header("底层控制器引用")] public DOTweenAnimationController controller;
        [Header("逻辑动画配置")] public List<BridgeAniData> configs = new();
        [Header("Awake 时自动校验")] public bool validateOnAwake = true;
        [Header("Start 时自动播放")] public bool autoPlayOnStart = false;
        [Header("自动播放的 aniName")] public string autoAniName;

        private Dictionary<string, BridgeAniData> _map;
        private ActiveContext _active;
        private bool _mapDirty; // 标记配置变更需要重建

        public BridgePlayState PlayState => _active == null ? BridgePlayState.Idle : BridgePlayState.Playing;

        private void Awake()
        {
            RebuildMap();
            if (validateOnAwake) ValidateAll();
        }

        private void Start()
        {
            if (autoPlayOnStart && !string.IsNullOrEmpty(autoAniName))
                PlayAni(autoAniName);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器修改后标记需刷新
            _mapDirty = true;
        }
#endif

        /// <summary>
        /// 确保字典最新（延迟到真正使用时再重建，减少不必要开销）
        /// </summary>
        private void EnsureMap()
        {
            if (_map == null || _mapDirty)
            {
                RebuildMap();
                _mapDirty = false;
            }
        }

        #region Public API

        public bool PlayAni(string aniName, Action onStart = null, Action onComplete = null)
        {
            EnsureMap();
            if (!TryGetConfig(aniName, out var cfg))
            {
                // 尝试一次强制重建（防止动态修改后未触发 OnValidate 的极端情况）
                RebuildMap();
                if (!TryGetConfig(aniName, out cfg))
                    return false;
            }

            var list = GetEffectiveSequenceList(cfg);
            if (list.Count == 0)
            {
                Debug.LogWarning($"[Bridge] aniName={aniName} 没有可播放的序列");
                return false;
            }

            float estimated = CalculateRealDuration(cfg, list);

            _active = new ActiveContext
            {
                aniName = aniName,
                startTime = -1f,            // 等待真正开始
                estimatedTotal = estimated,
                trackingSequence = null
            };

            if (IsGroupMode(cfg))
            {
                // 组合模式暂保留估算式进度（后续批次可扩充真实聚合）
                controller.PlayMultipleSequences(
                    list,
                    parallel: cfg.playParallel,
                    onFirstStart: () =>
                    {
                        if (_active != null)
                            _active.startTime = Time.time;
                        Debug.Log($"[Bridge] Start ani={aniName} mode={(cfg.playParallel ? "Parallel" : "Serial")} estimated={estimated:F2}s");
                        onStart?.Invoke();
                    },
                    onComplete: () =>
                    {
                        Debug.Log($"[Bridge] Complete ani={aniName}");
                        onComplete?.Invoke();
                        _active = null;
                    });
                return true;
            }

            // 单序列模式：使用真实 DOTween Sequence 追踪进度
            var seq = controller.PlaySequence(cfg.singleSequence, () =>
            {
                if (_active != null)
                    _active.startTime = Time.time;
                Debug.Log($"[Bridge] Start Single ani={aniName} estimated={estimated:F2}s");
                onStart?.Invoke();
            });

            if (seq == null)
            {
                _active = null;
                return false;
            }

            _active.trackingSequence = seq;

            seq.OnComplete(() =>
            {
                Debug.Log($"[Bridge] Complete Single ani={aniName}");
                onComplete?.Invoke();
                _active = null;
            });

            return true;
        }

        public IEnumerator PlayAniCoroutine(string aniName)
        {
            bool finished = false;
            if (!PlayAni(aniName, null, () => finished = true))
                yield break;
            while (!finished) yield return null;
        }

        public void StopCurrent()
        {
            if (PlayState == BridgePlayState.Playing)
            {
                controller.StopAllSequences();
                Debug.Log($"[Bridge] StopCurrent ani={_active.aniName}");
            }
            _active = null;
        }

        /// <summary>
        /// 获取当前进度：
        /// - 单序列模式：使用真实 DOTween Sequence Elapsed/Duration
        /// - 多序列组合：仍使用估算（后续批次可升级）
        /// </summary>
        public float GetCurrentProgress()
        {
            if (_active == null) return 0f;

            // 精确：单序列
            if (_active.trackingSequence != null && _active.trackingSequence.IsActive())
            {
                float dur = _active.trackingSequence.Duration(false);
                if (dur <= 0f) return 0f;
                return Mathf.Clamp01(_active.trackingSequence.Elapsed(false) / dur);
            }

            // 估算：组合
            if (_active.estimatedTotal <= 0f) return 0f;
            if (_active.startTime < 0f) return 0f;
            float elapsed = Time.time - _active.startTime;
            return Mathf.Clamp01(elapsed / _active.estimatedTotal);
        }

        public bool HasAni(string aniName)
        {
            EnsureMap();
            return _map != null && _map.ContainsKey(aniName);
        }

        public List<string> GetAllAniNames()
        {
            EnsureMap();
            return _map.Keys.ToList();
        }

        [ContextMenu("Validate All")]
        public void ValidateAll()
        {
            if (controller == null)
            {
                Debug.LogWarning("[Bridge] controller 为空，无法校验");
                return;
            }
            EnsureMap();

            var allSeqNames = controller.GetAllSequenceNames();

            foreach (var kv in _map)
            {
                var cfg = kv.Value;
                var list = GetEffectiveSequenceList(cfg);
                if (list.Count == 0)
                {
                    Debug.LogWarning($"[Bridge][Validate] aniName={cfg.aniName} 没有有效序列");
                    continue;
                }

                foreach (var seq in list)
                {
                    if (!allSeqNames.Contains(seq))
                        Debug.LogWarning($"[Bridge][Validate] aniName={cfg.aniName} 序列不存在: {seq}");
                }
            }
        }

        #endregion

        #region Internal

        private void RebuildMap()
        {
            _map = new Dictionary<string, BridgeAniData>(StringComparer.Ordinal);
            foreach (var c in configs)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.aniName)) continue;
                if (_map.ContainsKey(c.aniName))
                    Debug.LogWarning($"[Bridge] 重复 aniName={c.aniName}，后者覆盖前者");
                _map[c.aniName] = c;
            }
        }

        private bool TryGetConfig(string aniName, out BridgeAniData cfg)
        {
            cfg = null;
            if (_map == null || _map.Count == 0)
            {
                Debug.LogWarning("[Bridge] 映射未初始化或为空");
                return false;
            }

            if (!_map.TryGetValue(aniName, out cfg))
            {
                Debug.LogWarning($"[Bridge] 未找到 aniName={aniName}");
                return false;
            }

            return true;
        }

        private bool IsGroupMode(BridgeAniData cfg)
        {
            return cfg.groupSequences != null &&
                   cfg.groupSequences.Count > 0 &&
                   cfg.groupSequences.Any(s => !string.IsNullOrWhiteSpace(s));
        }

        private List<string> GetEffectiveSequenceList(BridgeAniData cfg)
        {
            if (IsGroupMode(cfg))
            {
                return cfg.groupSequences
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(cfg.singleSequence))
                return new List<string> { cfg.singleSequence };
            return new List<string>();
        }

        /// <summary>
        /// 真实时长简单计算（含各序列自身 delay）：
        /// 组合模式下仍为估算值
        /// </summary>
        private float CalculateRealDuration(BridgeAniData cfg, List<string> list)
        {
            if (controller == null || list == null || list.Count == 0) return 0f;

            if (IsGroupMode(cfg))
            {
                if (cfg.playParallel)
                {
                    float max = 0f;
                    foreach (var n in list)
                    {
                        var seq = controller.GetSequence(n);
                        if (seq != null)
                        {
                            float d = seq.GetTotalDuration();
                            if (d > max) max = d;
                        }
                    }
                    return max;
                }
                else
                {
                    float total = 0f;
                    foreach (var n in list)
                    {
                        var seq = controller.GetSequence(n);
                        if (seq != null) total += seq.GetTotalDuration();
                    }
                    return total;
                }
            }
            else
            {
                var single = controller.GetSequence(cfg.singleSequence);
                return single != null ? single.GetTotalDuration() : 0f;
            }
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_active != null)
            {
                float p = GetCurrentProgress();
                UnityEditor.Handles.Label(transform.position,
                    $"ActiveAni: {_active.aniName}  Progress: {p:P0}");
            }
        }
#endif
    }
}
