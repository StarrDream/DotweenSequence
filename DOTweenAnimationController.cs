using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace DOTweenAnimationSystem
{
    /// <summary>
    /// DOTween 动画控制器
    /// </summary>
    public class DOTweenAnimationController : MonoBehaviour
    {
        [Header("基础设置")]
        [SerializeField] private bool autoPlay = false;
        [SerializeField] private string autoPlaySequenceName = "";
        [SerializeField] private float autoPlayDelay = 0f;
        [SerializeField] private bool enableDebugLog = true;

        [Header("数据保存设置")]
        [SerializeField] private string saveFileName = "AnimationData";
        [SerializeField] private bool useCustomPath = false;
        [SerializeField] private string customSavePath = "";

        [Header("动画序列")]
        public List<AnimationSequence> animationSequences = new List<AnimationSequence>();

        private readonly Dictionary<string, Sequence> activeSequences = new Dictionary<string, Sequence>();
        private DOTweenAnimationDataService dataService;

        public event Action<bool> OnDataSaved;
        public event Action<bool> OnDataLoaded;

        private void Awake()
        {
            InitDataServiceIfNeeded();
            SanitizeAll(); // 启动时规范数据
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InitDataServiceIfNeeded();
            SanitizeAll();
        }
#endif

        private void Start()
        {
            if (autoPlay && !string.IsNullOrEmpty(autoPlaySequenceName))
            {
                if (autoPlayDelay > 0f)
                    Invoke(nameof(AutoPlaySequence), autoPlayDelay);
                else
                    AutoPlaySequence();
            }
        }

        private void OnDestroy()
        {
            StopAllSequences();
        }

        private void AutoPlaySequence()
        {
            PlaySequence(autoPlaySequenceName);
        }

        private void InitDataServiceIfNeeded()
        {
            if (dataService == null)
            {
                dataService = new DOTweenAnimationDataService(
                    () => animationSequences,
                    saveFileName,
                    useCustomPath,
                    customSavePath
                );
            }
        }

        /// <summary>
        /// 统一数据规范化：包含
        /// 1) 每序列内部 Sanitize
        /// 2) 去重序列名（避免同名导致播放查找引用不确定）
        /// </summary>
        private void SanitizeAll()
        {
            if (animationSequences == null)
                animationSequences = new List<AnimationSequence>();

            // 先内部 sanitize
            foreach (var seq in animationSequences)
                seq?.Sanitize();

            // 保证序列名唯一（简单重命名策略）
            var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var seq in animationSequences)
            {
                if (seq == null)
                    continue;

                string baseName = string.IsNullOrWhiteSpace(seq.sequenceName) ? "Sequence" : seq.sequenceName.Trim();
                if (!nameCount.ContainsKey(baseName))
                {
                    nameCount[baseName] = 0;
                    seq.sequenceName = baseName;
                }
                else
                {
                    nameCount[baseName]++;
                    seq.sequenceName = $"{baseName}_{nameCount[baseName]}";
                }
            }
        }

        #region 序列注册与状态管理

        private void RegisterSequence(string key, Sequence seq)
        {
            StopSequence(key);
            seq.OnKill(() =>
            {
                if (activeSequences.TryGetValue(key, out var s) && s == seq)
                    activeSequences.Remove(key);
            });
            activeSequences[key] = seq;
        }

        public void StopAllExceptAndResetState()
        {
            StopAllExceptAndResetState("__ALL__", 0);
        }

        private void StopAllExceptAndResetState(string keepKey, int currentSeqIndex)
        {
            foreach (var kv in activeSequences.Keys.ToList())
            {
                if (kv != keepKey)
                    StopSequence(kv);
            }

            // 基础跨序列状态策略：保持简单（后续批次再优化跨并行精细策略）
            if (currentSeqIndex >= 0 && currentSeqIndex < animationSequences.Count)
            {
                for (int i = 0; i < currentSeqIndex; i++)
                    SetSequenceToEnd(animationSequences[i]);

                for (int i = animationSequences.Count - 1; i > currentSeqIndex; i--)
                    ResetSequenceToStart(animationSequences[i]);

                ResetSequenceToStart(animationSequences[currentSeqIndex]); // 当前序列：全部动画初始化
            }
            else
            {
                for (int i = animationSequences.Count - 1; i >= 0; i--)
                    ResetSequenceToStart(animationSequences[i]);
            }
        }

        #endregion

        #region 播放控制

        public Sequence PlaySequence(string sequenceName, Action onSequenceStart = null)
        {
            var sequence = GetSequence(sequenceName);
            if (sequence == null)
            {
                LogWarning($"序列 '{sequenceName}' 不存在");
                return null;
            }

            int seqIndex = animationSequences.IndexOf(sequence);
            StopAllExceptAndResetState(sequenceName, seqIndex);

            // 并行内部重复目标检测（对象层级）
            if (sequence.isParallel)
            {
                var duplicateGroups = sequence.animations
                    .Where(a => a?.targetObject != null)
                    .GroupBy(a => a.targetObject)
                    .Where(g => g.Count() > 1)
                    .ToList();
                if (duplicateGroups.Count > 0)
                {
                    foreach (var g in duplicateGroups)
                        LogWarning($"并行序列 '{sequenceName}' 中对象 '{g.Key.name}' 被 {g.Count()} 个动画同时使用。");
                }
            }

            Sequence dotweenSequence = DOTween.Sequence();
            dotweenSequence.SetDelay(sequence.delay);

            if (sequence.isParallel)
            {
                foreach (var animation in sequence.animations)
                {
                    if (!ValidateAnimForPlay(animation, sequenceName)) continue;
                    var animSequence = CreateAnimationSequence(animation);
                    dotweenSequence.Join(animSequence);
                }
            }
            else
            {
                foreach (var animation in sequence.animations)
                {
                    if (!ValidateAnimForPlay(animation, sequenceName)) continue;

                    var animRef = animation;
                    dotweenSequence.AppendCallback(() =>
                    {
                        ApplyAnimationStart(animRef);
                    });

                    var animSequence = CreateAnimationSequence(animation);
                    dotweenSequence.Append(animSequence);
                }
            }

            if (onSequenceStart != null)
                dotweenSequence.OnStart(() => onSequenceStart.Invoke());

            RegisterSequence(sequenceName, dotweenSequence);
            dotweenSequence.Play();

            LogInfo($"播放序列: {sequenceName}");
            return dotweenSequence;
        }

        public void StopSequence(string sequenceName)
        {
            if (activeSequences.ContainsKey(sequenceName))
            {
                activeSequences[sequenceName]?.Kill();
                activeSequences.Remove(sequenceName);
                LogInfo($"停止序列: {sequenceName}");
            }
        }

        public void PauseSequence(string sequenceName)
        {
            if (activeSequences.ContainsKey(sequenceName))
            {
                activeSequences[sequenceName]?.Pause();
                LogInfo($"暂停序列: {sequenceName}");
            }
        }

        public void ResumeSequence(string sequenceName)
        {
            if (activeSequences.ContainsKey(sequenceName))
            {
                activeSequences[sequenceName]?.Play();
                LogInfo($"恢复序列: {sequenceName}");
            }
        }

        public void PlayAllSequences(Action onStart = null, Action onComplete = null)
        {
            if (animationSequences.Count == 0)
            {
                onStart?.Invoke();
                onComplete?.Invoke();
                return;
            }

            StopAllExceptAndResetState();
            onStart?.Invoke();

            int index = 0;
            Action playNext = null;
            playNext = () =>
            {
                if (index >= animationSequences.Count)
                {
                    onComplete?.Invoke();
                    return;
                }

                var seq = animationSequences[index];
                var s = PlaySequence(seq.sequenceName);
                if (s != null)
                {
                    s.OnComplete(() =>
                    {
                        index++;
                        playNext();
                    });
                }
                else
                {
                    index++;
                    playNext();
                }
            };
            playNext();
            LogInfo("按顺序播放所有序列");
        }

        public void PlaySingleAnimation(string sequenceName, string animationName)
        {
            var sequence = GetSequence(sequenceName);
            if (sequence == null)
            {
                LogWarning($"序列 '{sequenceName}' 不存在");
                return;
            }

            var animation = sequence.animations.Find(a => a.animationName == animationName);
            if (animation == null)
            {
                LogWarning($"动画 '{animationName}' 在序列 '{sequenceName}' 中不存在");
                return;
            }

            if (animation.targetObject == null)
            {
                LogWarning($"动画 '{animationName}' 的目标对象为空");
                return;
            }

            int seqIndex = animationSequences.IndexOf(sequence);
            string key = $"{sequenceName}_{animationName}";

            StopAllExceptAndResetState(key, seqIndex);

            ApplyAnimationStart(animation);

            var animSequence = CreateAnimationSequence(animation);
            RegisterSequence(key, animSequence);
            animSequence.Play();

            LogInfo($"播放单个动画: {sequenceName}.{animationName}");
        }

        public void PlayMultipleSequences(List<string> sequenceNames, bool parallel,
            Action onFirstStart = null, Action onComplete = null)
        {
            if (sequenceNames == null || sequenceNames.Count == 0)
            {
                LogWarning("PlayMultipleSequences: 序列名称列表为空");
                onFirstStart?.Invoke();
                onComplete?.Invoke();
                return;
            }

            var validNames = sequenceNames
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n.Trim())
                .Distinct()
                .Where(n => GetSequence(n) != null)
                .ToList();

            if (validNames.Count == 0)
            {
                LogWarning("PlayMultipleSequences: 没有有效的序列名称");
                onFirstStart?.Invoke();
                onComplete?.Invoke();
                return;
            }

            if (!parallel)
            {
                bool firedStart = false;
                int idx = 0;
                Action playNext = null;
                playNext = () =>
                {
                    if (idx >= validNames.Count)
                    {
                        onComplete?.Invoke();
                        return;
                    }

                    string name = validNames[idx];
                    var seqObj = PlaySequence(name, () =>
                    {
                        if (!firedStart)
                        {
                            firedStart = true;
                            onFirstStart?.Invoke();
                        }
                    });

                    if (seqObj != null)
                    {
                        seqObj.OnComplete(() =>
                        {
                            idx++;
                            playNext();
                        });
                    }
                    else
                    {
                        idx++;
                        playNext();
                    }
                };
                playNext();
                LogInfo($"串行播放指定序列集合（数量={validNames.Count}）");
                return;
            }

            // 并行
            foreach (var key in activeSequences.Keys.ToList())
                StopSequence(key);

            List<int> indices = validNames
                .Select(n => animationSequences.IndexOf(GetSequence(n)))
                .Where(i => i >= 0)
                .OrderBy(i => i)
                .ToList();

            if (indices.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            int minIndex = indices.First();
            int maxIndex = indices.Last();

            for (int i = 0; i < minIndex; i++)
                SetSequenceToEnd(animationSequences[i]);

            for (int i = animationSequences.Count - 1; i > maxIndex; i--)
                ResetSequenceToStart(animationSequences[i]);

            foreach (var idxSeq in indices)
                ResetSequenceToStart(animationSequences[idxSeq]);

            // 跨序列目标检测（已有对象级提醒）
            var crossTargets = new Dictionary<GameObject, List<string>>();
            foreach (var seqName in validNames)
            {
                var seqData = GetSequence(seqName);
                if (seqData?.animations == null) continue;
                foreach (var anim in seqData.animations)
                {
                    if (anim?.targetObject == null) continue;
                    if (!crossTargets.TryGetValue(anim.targetObject, out var list))
                    {
                        list = new List<string>();
                        crossTargets[anim.targetObject] = list;
                    }
                    if (!list.Contains(seqName)) list.Add(seqName);
                }
            }
            foreach (var kv in crossTargets)
            {
                if (kv.Value.Count > 1)
                    LogWarning($"并行多序列 - 对象 '{kv.Key.name}' 同时出现在序列: {string.Join(", ", kv.Value)} 可能造成起始状态冲突。");
            }

            int total = validNames.Count;
            int finished = 0;
            bool started = false;

            void TryFireStart()
            {
                if (!started)
                {
                    started = true;
                    onFirstStart?.Invoke();
                }
            }

            void TryComplete()
            {
                if (finished >= total)
                    onComplete?.Invoke();
            }

            foreach (var name in validNames)
            {
                var seqData = GetSequence(name);
                if (seqData == null)
                {
                    finished++;
                    TryComplete();
                    continue;
                }

                if (seqData.isParallel)
                {
                    var duplicateGroups = seqData.animations
                        .Where(a => a?.targetObject != null)
                        .GroupBy(a => a.targetObject)
                        .Where(g => g.Count() > 1)
                        .ToList();
                    if (duplicateGroups.Count > 0)
                    {
                        foreach (var g in duplicateGroups)
                            LogWarning($"并行序列 '{seqData.sequenceName}' 中对象 '{g.Key.name}' 被 {g.Count()} 个动画同时使用。");
                    }
                }

                Sequence groupSequence = DOTween.Sequence();
                groupSequence.SetDelay(seqData.delay);

                bool anyValidAnim = false;

                if (seqData.isParallel)
                {
                    foreach (var anim in seqData.animations)
                    {
                        if (!ValidateAnimForPlay(anim, name)) continue;
                        anyValidAnim = true;
                        var animSeq = CreateAnimationSequence(anim);
                        groupSequence.Join(animSeq);
                    }
                }
                else
                {
                    foreach (var anim in seqData.animations)
                    {
                        if (!ValidateAnimForPlay(anim, name)) continue;
                        anyValidAnim = true;

                        var animRef = anim;
                        groupSequence.AppendCallback(() =>
                        {
                            ApplyAnimationStart(animRef);
                        });

                        var animSeq = CreateAnimationSequence(anim);
                        groupSequence.Append(animSeq);
                    }
                }

                if (!anyValidAnim)
                {
                    finished++;
                    TryComplete();
                    continue;
                }

                groupSequence.OnStart(TryFireStart);
                groupSequence.OnComplete(() =>
                {
                    finished++;
                    TryComplete();
                });

                RegisterSequence(name, groupSequence);
                groupSequence.Play();
            }

            LogInfo($"并行播放指定序列集合（数量={validNames.Count}）");
        }

        public void StopAllSequences()
        {
            foreach (var kvp in activeSequences.ToList())
                StopSequence(kvp.Key);
        }

        public void PauseAllSequences()
        {
            foreach (var kvp in activeSequences)
                kvp.Value?.Pause();
            LogInfo("暂停所有序列");
        }

        public void ResumeAllSequences()
        {
            foreach (var kvp in activeSequences)
                kvp.Value?.Play();
            LogInfo("恢复所有序列");
        }

        #endregion

        #region 查询

        public bool IsSequencePlaying(string sequenceName)
        {
            return activeSequences.ContainsKey(sequenceName) &&
                   activeSequences[sequenceName] != null &&
                   activeSequences[sequenceName].IsActive() &&
                   activeSequences[sequenceName].IsPlaying();
        }

        public int GetActiveSequenceCount()
        {
            return activeSequences.Count(kvp => kvp.Value != null && kvp.Value.IsActive());
        }

        public List<string> GetAllSequenceNames()
        {
            return animationSequences.Select(seq => seq.sequenceName).ToList();
        }

        public AnimationSequence GetSequence(string sequenceName)
        {
            return animationSequences.Find(seq => seq.sequenceName == sequenceName);
        }

        public List<AnimationSequence> GetAllSequences()
        {
            return animationSequences;
        }

        #endregion

        #region 序列管理

        public void AddSequence(AnimationSequence sequence)
        {
            if (sequence != null && !animationSequences.Any(s => s.sequenceName == sequence.sequenceName))
                animationSequences.Add(sequence);
        }

        public void RemoveSequence(string sequenceName)
        {
            var sequence = GetSequence(sequenceName);
            if (sequence != null)
            {
                StopSequence(sequenceName);
                animationSequences.Remove(sequence);
            }
        }

        public AnimationSequence DuplicateSequence(string sequenceName)
        {
            var original = GetSequence(sequenceName);
            if (original == null)
            {
                LogWarning($"未找到要复制的序列: {sequenceName}");
                return null;
            }

            string newName = sequenceName + "_Copy";
            int counter = 1;
            while (animationSequences.Any(s => s.sequenceName == newName))
            {
                newName = $"{sequenceName}_Copy_{counter}";
                counter++;
            }

            var newSequence = new AnimationSequence(newName)
            {
                isParallel = original.isParallel,
                delay = original.delay
            };

            foreach (var anim in original.animations)
            {
                var newAnim = new AnimationData(anim.animationName + "_Copy")
                {
                    targetObject = anim.targetObject,
                    targetObjectPath = anim.targetObjectPath,
                    useLocalSpace = anim.useLocalSpace,
                    startTransform = anim.startTransform.Clone(),
                    targetTransforms = anim.targetTransforms.Select(t => t.Clone()).ToList()
                };
                newSequence.animations.Add(newAnim);
            }

            AddSequence(newSequence);
            LogInfo($"已复制序列: {sequenceName} -> {newName}");
            return newSequence;
        }

        public void ClearAllSequences()
        {
            StopAllSequences();
            animationSequences.Clear();
        }

        #endregion

        #region 动画构建

        private Sequence CreateAnimationSequence(AnimationData animation)
        {
            Sequence animSequence = DOTween.Sequence();
            Transform target = animation.targetObject.transform;

            foreach (var targetTransform in animation.targetTransforms)
            {
                if (targetTransform == null) continue;

                float d = targetTransform.SafeDuration;

                if (d <= 0f)
                {
                    var tdSnapshot = targetTransform;
                    animSequence.AppendCallback(() =>
                    {
                        if (target == null) return;
                        ApplyTransformInstant(target, tdSnapshot, animation.useLocalSpace);
                        if (tdSnapshot.enableActiveControl)
                            target.gameObject.SetActive(tdSnapshot.activeState);
                    });
                    continue;
                }

                Sequence targetSequence = DOTween.Sequence();

                if (animation.useLocalSpace)
                {
                    targetSequence.Join(
                        target.DOLocalMove(targetTransform.position, d)
                            .SetEase(targetTransform.easeType));
                    targetSequence.Join(
                        target.DOLocalRotate(targetTransform.rotation, d)
                            .SetEase(targetTransform.easeType));
                }
                else
                {
                    targetSequence.Join(
                        target.DOMove(targetTransform.position, d)
                            .SetEase(targetTransform.easeType));
                    targetSequence.Join(
                        target.DORotate(targetTransform.rotation, d)
                            .SetEase(targetTransform.easeType));
                }

                targetSequence.Join(
                    target.DOScale(targetTransform.scale, d)
                        .SetEase(targetTransform.easeType));

                animSequence.Append(targetSequence);

                if (targetTransform.enableActiveControl)
                {
                    bool activeValue = targetTransform.activeState;
                    Transform cachedTarget = target;
                    animSequence.AppendCallback(() =>
                    {
                        if (cachedTarget != null)
                            cachedTarget.gameObject.SetActive(activeValue);
                    });
                }
            }

            if (animation.onAnimationStart != null)
                animSequence.OnStart(() => animation.onAnimationStart.Invoke());
            if (animation.onAnimationUpdate != null)
                animSequence.OnUpdate(() => animation.onAnimationUpdate.Invoke());
            if (animation.onAnimationComplete != null)
                animSequence.OnComplete(() => animation.onAnimationComplete.Invoke());

            return animSequence;
        }

        private void ApplyAnimationStart(AnimationData anim)
        {
            if (anim == null || anim.targetObject == null) return;
            anim.startTransform.ApplyToTransform(anim.targetObject.transform, anim.useLocalSpace);
            if (anim.startTransform.enableActiveControl)
                anim.targetObject.SetActive(anim.startTransform.activeState);
        }

        private void ApplyTransformInstant(Transform target, TransformData transformData, bool useLocalSpace)
        {
            transformData.ApplyToTransform(target, useLocalSpace);
        }

        private bool ValidateAnimForPlay(AnimationData animation, string sequenceName)
        {
            if (animation == null)
            {
                LogWarning($"序列 '{sequenceName}' 中存在空动画引用，已跳过");
                return false;
            }
            if (animation.targetObject == null)
            {
                LogWarning($"序列 '{sequenceName}' 的动画 '{animation.animationName}' 目标对象为空，已跳过");
                return false;
            }
            return true;
        }

        #endregion

        #region 序列状态预处理

        private void ResetSequenceToStart(AnimationSequence seq)
        {
            if (seq == null) return;
            foreach (var anim in seq.animations)
                SetAnimationToStart(anim);
        }

        private void SetSequenceToEnd(AnimationSequence seq)
        {
            if (seq == null) return;
            foreach (var anim in seq.animations)
                SetAnimationToEnd(anim);
        }

        private void SetAnimationToStart(AnimationData anim)
        {
            if (anim == null || anim.targetObject == null) return;
            anim.startTransform.ApplyToTransform(anim.targetObject.transform, anim.useLocalSpace);
            if (anim.startTransform.enableActiveControl)
                anim.targetObject.SetActive(anim.startTransform.activeState);
        }

        private void SetAnimationToEnd(AnimationData anim)
        {
            if (anim == null || anim.targetObject == null) return;

            TransformData targetData;
            bool lastHasActive = false;
            bool lastActiveState = true;

            if (anim.targetTransforms != null && anim.targetTransforms.Count > 0)
            {
                var last = anim.targetTransforms[anim.targetTransforms.Count - 1];
                targetData = last.Clone();
                if (last.enableActiveControl)
                {
                    lastHasActive = true;
                    lastActiveState = last.activeState;
                }
            }
            else
            {
                targetData = anim.startTransform;
            }

            targetData.ApplyToTransform(anim.targetObject.transform, anim.useLocalSpace);

            if (lastHasActive)
            {
                anim.targetObject.SetActive(lastActiveState);
            }
        }

        #endregion

        #region 数据持久化接口

        public void SaveAnimationData(string overrideFileName = null)
        {
            bool ok = dataService.SaveToFile(overrideFileName);
            if (ok) LogInfo("动画数据保存成功");
            OnDataSaved?.Invoke(ok);
        }

        public void LoadAnimationData(string overrideFileName = null)
        {
            bool ok = dataService.LoadFromFile(out var loaded, overrideFileName);
            if (ok && loaded != null)
            {
                animationSequences = loaded;
                SanitizeAll();
                LogInfo($"加载动画数据成功: {animationSequences.Count} 序列");
            }
            OnDataLoaded?.Invoke(ok);
        }

        public string ExportToJSON()
        {
            string json = dataService.ExportToJson();
            if (json != null)
                LogInfo("导出 JSON 成功");
            else
                LogWarning("导出 JSON 失败");
            return json;
        }

        public void ImportFromJSON(string jsonContent)
        {
            bool ok = dataService.ImportFromJson(jsonContent, out var loaded);
            if (ok && loaded != null)
            {
                animationSequences = loaded;
                SanitizeAll();
                LogInfo($"导入 JSON 成功: {animationSequences.Count} 序列 (UnityEvent 未导入)");
            }
            else
            {
                LogWarning("导入 JSON 失败");
            }
        }

        public bool SaveFileExists(string overrideFileName = null)
        {
            return dataService.SaveFileExists(overrideFileName);
        }

        public List<string> GetAllSaveFiles()
        {
            return dataService.GetAllSaveFiles();
        }

        public string GetSaveDirectory()
        {
            return dataService.GetSaveDirectory();
        }

        #endregion

        #region 日志

        private void LogInfo(string message)
        {
            if (enableDebugLog)
                Debug.Log($"[DOTweenAnimationController] {message}");
        }

        private void LogWarning(string message)
        {
            if (enableDebugLog)
                Debug.LogWarning($"[DOTweenAnimationController] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[DOTweenAnimationController] {message}");
        }

        #endregion
    }
}
