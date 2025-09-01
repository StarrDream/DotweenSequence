#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using System.IO;
using System;

namespace DOTweenAnimationSystem.Editor
{
    /// <summary>
    /// 动画编辑器窗口（本批次修改点：
    /// 1) “前插 / 后插” 使用 Clone() 避免重复引用
    /// 2) 增加重复引用扫描并自动修复按钮（辅助清理历史数据）
    /// </summary>
    public class DOTweenAnimationEditorWindow : EditorWindow
    {
        private DOTweenAnimationController selectedController;
        private SerializedObject controllerSO;

        private Vector2 leftPanelScrollPos;
        private Vector2 rightPanelScrollPos;
        private Vector2 sequenceScrollPos;
        private Vector2 animationScrollPos;

        private int selectedSequenceIndex = -1;
        private int selectedAnimationIndex = -1;
        private int selectedTargetIndex = -1;

        private float leftPanelWidth = 320f;
        private bool isResizingLeftPanel = false;

        private GUIStyle headerStyle;
        private GUIStyle buttonStyle;
        private GUIStyle miniButtonStyle;
        private bool stylesInitialized = false;

        private static Dictionary<string, bool> animationEventFoldouts = new Dictionary<string, bool>();
        private static Dictionary<string, bool> transformFoldouts = new Dictionary<string, bool>();

        [MenuItem("Window/DOTween Animation Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<DOTweenAnimationEditorWindow>("DOTween Animation Controller");
            window.minSize = new Vector2(1100, 650);
            window.Show();
        }

        private void OnEnable()
        {
            FindAnimationController();
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fixedHeight = 24
            };

            miniButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                fixedHeight = 18,
                margin = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(4, 4, 2, 2)
            };

            stylesInitialized = true;
        }

        private void UpdateSerializedObject()
        {
            if (selectedController != null)
            {
                if (controllerSO == null || controllerSO.targetObject != selectedController)
                    controllerSO = new SerializedObject(selectedController);
                else
                    controllerSO.Update();
            }
        }

        private void OnGUI()
        {
            InitializeStyles();
            UpdateSerializedObject();

            try
            {
                EditorGUILayout.BeginVertical();
                DrawToolbar();
                DrawMainContent();
                EditorGUILayout.EndVertical();
                HandleEvents();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"DOTweenAnimationEditorWindow GUI Error: {e.Message}");
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("DOTween Animation Controller", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("导入JSON", EditorStyles.toolbarButton, GUILayout.Width(80)))
                ImportFromJSON();

            if (GUILayout.Button("导出JSON", EditorStyles.toolbarButton, GUILayout.Width(80)))
                ExportToJSON();

            if (Application.isPlaying && selectedController != null)
            {
                if (GUILayout.Button("整体播放▶", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    selectedController.PlayAllSequences();

                if (GUILayout.Button("停止所有", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    selectedController.StopAllSequences();

                if (GUILayout.Button("全部重置", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    selectedController.StopAllExceptAndResetState();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawMainContent()
        {
            if (selectedController == null)
            {
                DrawNoControllerMessage();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawVerticalSplitter();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(leftPanelWidth));
            DrawSequenceList();
            EditorGUILayout.Space();
            if (selectedSequenceIndex >= 0)
                DrawSelectedSequenceAnimations();
            EditorGUILayout.EndVertical();
        }

        private void DrawSequenceList()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("动画序列列表", headerStyle);

            if (GUILayout.Button("+", GUILayout.Width(25), GUILayout.Height(20)))
                CreateNewSequence();

            if (GUILayout.Button("-", GUILayout.Width(25), GUILayout.Height(20)))
                DeleteSelectedSequence();

            EditorGUILayout.EndHorizontal();

            sequenceScrollPos = EditorGUILayout.BeginScrollView(sequenceScrollPos, GUILayout.Height(220));

            for (int i = 0; i < selectedController.animationSequences.Count; i++)
            {
                var sequence = selectedController.animationSequences[i];
                bool isSelected = (i == selectedSequenceIndex);

                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.7f, 1f, 0.6f) : Color.white;

                EditorGUILayout.BeginHorizontal("box");

                if (GUILayout.Button($"{sequence.sequenceName} ({sequence.animations.Count})",
                        isSelected ? EditorStyles.whiteBoldLabel : EditorStyles.label))
                {
                    selectedSequenceIndex = i;
                    selectedAnimationIndex = -1;
                    selectedTargetIndex = -1;
                }

                if (Application.isPlaying)
                {
                    bool isPlaying = selectedController.IsSequencePlaying(sequence.sequenceName);
                    GUI.enabled = !isPlaying;

                    if (GUILayout.Button("▶", GUILayout.Width(25)))
                        selectedController.PlaySequence(sequence.sequenceName);

                    if (GUILayout.Button("■", GUILayout.Width(25)))
                        selectedController.StopSequence(sequence.sequenceName);

                    GUI.enabled = true;

                    if (isPlaying)
                        EditorGUILayout.LabelField("播放中", GUILayout.Width(50));
                }

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedSequenceAnimations()
        {
            if (selectedSequenceIndex < 0 || selectedSequenceIndex >= selectedController.animationSequences.Count)
                return;

            var sequence = selectedController.animationSequences[selectedSequenceIndex];

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"序列: {sequence.sequenceName}", headerStyle);

            if (GUILayout.Button("+", GUILayout.Width(25), GUILayout.Height(20)))
                AddAnimationToSequence();

            if (GUILayout.Button("-", GUILayout.Width(25), GUILayout.Height(20)))
                RemoveAnimationFromSequence();

            if (GUILayout.Button("复制序列", GUILayout.Width(70)))
                selectedController.DuplicateSequence(sequence.sequenceName);

            if (GUILayout.Button("清空动画", GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("确认", $"确定清空序列 '{sequence.sequenceName}' 的所有动画吗？", "确定", "取消"))
                {
                    sequence.Clear();
                    selectedAnimationIndex = -1;
                    selectedTargetIndex = -1;
                    MarkControllerDirty();
                }
            }

            EditorGUILayout.EndHorizontal();

            animationScrollPos = EditorGUILayout.BeginScrollView(animationScrollPos, GUILayout.Height(180));

            for (int i = 0; i < sequence.animations.Count; i++)
            {
                var animation = sequence.animations[i];
                bool isSelected = (i == selectedAnimationIndex);

                GUI.backgroundColor = isSelected ? new Color(1f, 0.9f, 0.4f, 0.7f) : Color.white;
                EditorGUILayout.BeginHorizontal("box");

                if (GUILayout.Button(animation.animationName,
                        isSelected ? EditorStyles.whiteBoldLabel : EditorStyles.label))
                {
                    selectedAnimationIndex = i;
                    selectedTargetIndex = -1;
                }

                if (GUILayout.Button("▲", miniButtonStyle, GUILayout.Width(22)))
                {
                    if (i > 0)
                    {
                        sequence.animations.RemoveAt(i);
                        sequence.animations.Insert(i - 1, animation);
                        selectedAnimationIndex = i - 1;
                        MarkControllerDirty();
                    }
                }

                if (GUILayout.Button("▼", miniButtonStyle, GUILayout.Width(22)))
                {
                    if (i < sequence.animations.Count - 1)
                    {
                        sequence.animations.RemoveAt(i);
                        sequence.animations.Insert(i + 1, animation);
                        selectedAnimationIndex = i + 1;
                        MarkControllerDirty();
                    }
                }

                if (GUILayout.Button("复", miniButtonStyle, GUILayout.Width(28)))
                {
                    var cloned = animation.Clone();
                    cloned.animationName += "_Copy";
                    sequence.AddAnimation(cloned);
                    selectedAnimationIndex = sequence.animations.Count - 1;
                    MarkControllerDirty();
                }

                if (Application.isPlaying && animation.targetObject != null)
                {
                    if (GUILayout.Button("▶", miniButtonStyle, GUILayout.Width(25)))
                        selectedController.PlaySingleAnimation(sequence.sequenceName, animation.animationName);
                }

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            rightPanelScrollPos = EditorGUILayout.BeginScrollView(rightPanelScrollPos);

            if (selectedSequenceIndex >= 0 && selectedSequenceIndex < selectedController.animationSequences.Count)
            {
                DrawSequenceProperties();
                EditorGUILayout.Space();
                if (selectedAnimationIndex >= 0)
                    DrawAnimationProperties();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSequenceProperties()
        {
            var sequence = selectedController.animationSequences[selectedSequenceIndex];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("序列属性", headerStyle);

            EditorGUI.BeginChangeCheck();
            sequence.sequenceName = EditorGUILayout.TextField("序列名称", sequence.sequenceName);
            sequence.isParallel = EditorGUILayout.Toggle("并行播放", sequence.isParallel);
            sequence.delay = EditorGUILayout.FloatField("延迟时间", sequence.delay);
            if (EditorGUI.EndChangeCheck())
                MarkControllerDirty();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"总时长(安全): {sequence.GetTotalDuration():F2} 秒");
            EditorGUILayout.LabelField($"动画数量: {sequence.animations.Count}");
            EditorGUILayout.LabelField($"有效动画: {sequence.GetValidAnimationCount()}");

            EditorGUILayout.Space();
            if (GUILayout.Button("扫描并修复动画内部重复 Transform 引用", GUILayout.Height(24)))
            {
                int fixedCount = 0;
                foreach (var anim in sequence.animations)
                    fixedCount += FixDuplicateTransformReferences(anim);
                if (fixedCount > 0)
                    Debug.Log($"[Editor] 已修复 {fixedCount} 个重复 TransformData 引用。");
                MarkControllerDirty();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationProperties()
        {
            var sequence = selectedController.animationSequences[selectedSequenceIndex];
            if (selectedAnimationIndex >= sequence.animations.Count) return;

            var animation = sequence.animations[selectedAnimationIndex];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("动画属性与编辑", headerStyle);

            EditorGUI.BeginChangeCheck();
            animation.animationName = EditorGUILayout.TextField("动画名称", animation.animationName);
            animation.targetObject = (GameObject)EditorGUILayout.ObjectField("目标对象", animation.targetObject, typeof(GameObject), true);
            animation.useLocalSpace = EditorGUILayout.Toggle("使用本地坐标", animation.useLocalSpace);
            if (EditorGUI.EndChangeCheck())
                MarkControllerDirty();

            EditorGUILayout.Space();
            DrawTransformDataWithInlineOps("起始 Transform", animation.startTransform, animation, isStart: true);

            EditorGUILayout.Space();
            DrawTargetTransforms(animation);

            EditorGUILayout.Space();
            DrawAnimationMetaInfo(animation);

            EditorGUILayout.Space();
            DrawAnimationEvents(animation);

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimationMetaInfo(AnimationData animation)
        {
            EditorGUILayout.LabelField("辅助操作", headerStyle);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("捕获起始", buttonStyle))
            {
                animation.CaptureStartTransform();
                MarkControllerDirty();
            }

            if (GUILayout.Button("应用起始", buttonStyle))
                animation.ApplyStartTransform();

            if (GUILayout.Button("复制动画", buttonStyle))
            {
                var cloned = animation.Clone();
                var sequence = selectedController.animationSequences[selectedSequenceIndex];
                sequence.AddAnimation(cloned);
                selectedAnimationIndex = sequence.animations.Count - 1;
                MarkControllerDirty();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"动画总时长(安全): {animation.GetTotalDuration():F2} 秒");
            EditorGUILayout.LabelField($"目标数量: {animation.targetTransforms.Count}");
            EditorGUILayout.LabelField($"有效性: {(animation.IsValid() ? "有效" : "无效")}");
        }

        private void DrawTransformDataWithInlineOps(string label, TransformData transformData, AnimationData animation, bool isStart, int targetIndex = -1)
        {
            string foldoutKey = BuildTransformFoldoutKey(isStart, targetIndex);
            if (!transformFoldouts.ContainsKey(foldoutKey))
                transformFoldouts[foldoutKey] = true;

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            transformFoldouts[foldoutKey] = EditorGUILayout.Foldout(transformFoldouts[foldoutKey], label, true);
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(animation.targetObject == null))
            {
                if (GUILayout.Button("捕获", miniButtonStyle, GUILayout.Width(38)))
                {
                    CaptureTransform(transformData, animation);
                    MarkControllerDirty();
                }

                if (GUILayout.Button("应用", miniButtonStyle, GUILayout.Width(38)))
                    ApplyTransform(transformData, animation);
            }

            if (!isStart && targetIndex >= 0)
            {
                // “复” 已经使用 Clone，不变
                if (GUILayout.Button("复", miniButtonStyle, GUILayout.Width(30)))
                {
                    var clone = transformData.Clone();
                    animation.targetTransforms.Insert(targetIndex + 1, clone);
                    selectedTargetIndex = targetIndex + 1;
                    MarkControllerDirty();
                }

                // “前插” 改为克隆：避免共享引用
                if (GUILayout.Button("前插", miniButtonStyle, GUILayout.Width(38)))
                {
                    var clone = transformData.Clone();
                    animation.targetTransforms.Insert(targetIndex, clone);
                    selectedTargetIndex = targetIndex;
                    MarkControllerDirty();
                }

                // “后插” 改为克隆
                if (GUILayout.Button("后插", miniButtonStyle, GUILayout.Width(38)))
                {
                    var clone = transformData.Clone();
                    animation.targetTransforms.Insert(targetIndex + 1, clone);
                    selectedTargetIndex = targetIndex + 1;
                    MarkControllerDirty();
                }

                if (GUILayout.Button("▲", miniButtonStyle, GUILayout.Width(24)))
                {
                    if (targetIndex > 0)
                    {
                        var item = animation.targetTransforms[targetIndex];
                        animation.targetTransforms.RemoveAt(targetIndex);
                        animation.targetTransforms.Insert(targetIndex - 1, item);
                        selectedTargetIndex = targetIndex - 1;
                        MarkControllerDirty();
                    }
                }

                if (GUILayout.Button("▼", miniButtonStyle, GUILayout.Width(24)))
                {
                    if (targetIndex < animation.targetTransforms.Count - 1)
                    {
                        var item = animation.targetTransforms[targetIndex];
                        animation.targetTransforms.RemoveAt(targetIndex);
                        animation.targetTransforms.Insert(targetIndex + 1, item);
                        selectedTargetIndex = targetIndex + 1;
                        MarkControllerDirty();
                    }
                }

                if (GUILayout.Button("删", miniButtonStyle, GUILayout.Width(28)))
                {
                    if (EditorUtility.DisplayDialog("删除确认", $"删除 目标Transform {targetIndex + 1} ?", "删除", "取消"))
                    {
                        animation.targetTransforms.RemoveAt(targetIndex);
                        selectedTargetIndex = -1;
                        MarkControllerDirty();
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        return;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            if (transformFoldouts[foldoutKey])
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                transformData.position = EditorGUILayout.Vector3Field("位置", transformData.position);
                transformData.rotation = EditorGUILayout.Vector3Field("旋转", transformData.rotation);
                transformData.scale = EditorGUILayout.Vector3Field("缩放", transformData.scale);
                transformData.duration = Mathf.Max(0f, EditorGUILayout.FloatField("持续时间", transformData.duration));
                transformData.easeType = (Ease)EditorGUILayout.EnumPopup("缓动类型", transformData.easeType);
                transformData.enableActiveControl = EditorGUILayout.Toggle("启用激活控制", transformData.enableActiveControl);
                if (transformData.enableActiveControl)
                {
                    EditorGUI.indentLevel++;
                    transformData.activeState = EditorGUILayout.Toggle("目标激活状态", transformData.activeState);
                    EditorGUI.indentLevel--;
                }

                if (EditorGUI.EndChangeCheck())
                    MarkControllerDirty();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private string BuildTransformFoldoutKey(bool isStart, int targetIndex)
        {
            return $"{(selectedController ? selectedController.GetInstanceID() : 0)}|{selectedSequenceIndex}|{selectedAnimationIndex}|{(isStart ? "START" : $"T{targetIndex}")}";
        }

        private void DrawTargetTransforms(AnimationData animation)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("目标 Transform 列表", headerStyle);
            if (GUILayout.Button("新增空目标", buttonStyle, GUILayout.Width(100)))
            {
                animation.AddTargetTransform();
                selectedTargetIndex = animation.targetTransforms.Count - 1;
                MarkControllerDirty();
            }

            if (GUILayout.Button("按持续时间排序", buttonStyle, GUILayout.Width(120)))
            {
                animation.targetTransforms = animation.targetTransforms.OrderBy(t => t.SafeDuration).ToList();
                selectedTargetIndex = -1;
                MarkControllerDirty();
            }

            if (GUILayout.Button("全部捕获当前", buttonStyle, GUILayout.Width(110)))
            {
                if (animation.targetObject != null)
                {
                    foreach (var td in animation.targetTransforms)
                        CaptureTransform(td, animation);
                    MarkControllerDirty();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (animation.targetTransforms == null || animation.targetTransforms.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无目标 Transform，点击 '新增空目标' 创建。", MessageType.Info);
                return;
            }

            for (int i = 0; i < animation.targetTransforms.Count; i++)
            {
                var td = animation.targetTransforms[i];
                bool isSelected = (i == selectedTargetIndex);
                GUI.backgroundColor = isSelected ? new Color(0.6f, 1f, 0.6f, 0.5f) : Color.white;

                DrawTransformDataWithInlineOps($"目标 Transform {i + 1}", td, animation, isStart: false, targetIndex: i);

                GUI.backgroundColor = Color.white;
            }
        }

        private void CaptureTransform(TransformData data, AnimationData animation)
        {
            if (animation.targetObject == null) return;
            var tr = animation.targetObject.transform;
            if (animation.useLocalSpace)
            {
                data.position = tr.localPosition;
                data.rotation = tr.localEulerAngles;
            }
            else
            {
                data.position = tr.position;
                data.rotation = tr.eulerAngles;
            }
            data.scale = tr.localScale;
        }

        private void ApplyTransform(TransformData data, AnimationData animation)
        {
            if (animation.targetObject == null) return;
            var tr = animation.targetObject.transform;
            Undo.RecordObject(tr, "Apply TransformData");
            if (animation.useLocalSpace)
            {
                tr.localPosition = data.position;
                tr.localEulerAngles = data.rotation;
            }
            else
            {
                tr.position = data.position;
                tr.eulerAngles = data.rotation;
            }
            tr.localScale = data.scale;
            if (data.enableActiveControl)
                tr.gameObject.SetActive(data.activeState);
        }

        private void DrawAnimationEvents(AnimationData animation)
        {
            if (controllerSO == null) return;
            controllerSO.Update();
            SerializedProperty sequencesProp = controllerSO.FindProperty("animationSequences");
            if (sequencesProp == null || selectedSequenceIndex >= sequencesProp.arraySize) return;
            SerializedProperty seqProp = sequencesProp.GetArrayElementAtIndex(selectedSequenceIndex);
            SerializedProperty animsProp = seqProp.FindPropertyRelative("animations");
            if (animsProp == null || selectedAnimationIndex >= animsProp.arraySize) return;
            SerializedProperty animProp = animsProp.GetArrayElementAtIndex(selectedAnimationIndex);

            SerializedProperty onStartProp = animProp.FindPropertyRelative("onAnimationStart");
            SerializedProperty onUpdateProp = animProp.FindPropertyRelative("onAnimationUpdate");
            SerializedProperty onCompleteProp = animProp.FindPropertyRelative("onAnimationComplete");

            string key = BuildAnimationEventKey();
            if (!animationEventFoldouts.ContainsKey(key))
                animationEventFoldouts[key] = false;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            animationEventFoldouts[key] = EditorGUILayout.Foldout(animationEventFoldouts[key], "动画事件 (UnityEvent)", true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(animationEventFoldouts[key] ? "收起" : "展开", miniButtonStyle, GUILayout.Width(50)))
                animationEventFoldouts[key] = !animationEventFoldouts[key];
            EditorGUILayout.EndHorizontal();

            if (animationEventFoldouts[key])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(onStartProp, new GUIContent("On Animation Start"));
                EditorGUILayout.PropertyField(onUpdateProp, new GUIContent("On Animation Update"));
                EditorGUILayout.PropertyField(onCompleteProp, new GUIContent("On Animation Complete"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            controllerSO.ApplyModifiedProperties();

            int startCount = animation.onAnimationStart?.GetPersistentEventCount() ?? 0;
            int updateCount = animation.onAnimationUpdate?.GetPersistentEventCount() ?? 0;
            int completeCount = animation.onAnimationComplete?.GetPersistentEventCount() ?? 0;
            EditorGUILayout.LabelField($"事件监听数: Start={startCount}  Update={updateCount}  Complete={completeCount}");
        }

        private string BuildAnimationEventKey()
        {
            return $"{(selectedController ? selectedController.GetInstanceID() : 0)}|{selectedSequenceIndex}|{selectedAnimationIndex}|EVENTS";
        }

        private void DrawVerticalSplitter()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(5));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            Rect splitterRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
                isResizingLeftPanel = true;

            if (isResizingLeftPanel)
            {
                leftPanelWidth = Event.current.mousePosition.x;
                leftPanelWidth = Mathf.Clamp(leftPanelWidth, 220f, position.width - 300f);
                Repaint();
            }
        }

        private void DrawNoControllerMessage()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(420));
            EditorGUILayout.HelpBox("未找到 DOTweenAnimationController 组件", MessageType.Warning);
            if (GUILayout.Button("查找 Controller", GUILayout.Height(30)))
                FindAnimationController();
            if (GUILayout.Button("创建新 Controller", GUILayout.Height(30)))
                CreateNewController();
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private void HandleEvents()
        {
            if (Event.current.type == EventType.MouseUp)
                isResizingLeftPanel = false;

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Delete)
                {
                    HandleDeleteKey();
                    Event.current.Use();
                }
            }
        }

        private void HandleDeleteKey()
        {
            if (selectedAnimationIndex >= 0)
                RemoveAnimationFromSequence();
            else if (selectedSequenceIndex >= 0)
                DeleteSelectedSequence();
        }

        private void FindAnimationController()
        {
            if (Selection.activeGameObject != null)
                selectedController = Selection.activeGameObject.GetComponent<DOTweenAnimationController>();

            if (selectedController == null)
                selectedController = FindObjectOfType<DOTweenAnimationController>();

            if (selectedController != null)
            {
                controllerSO = new SerializedObject(selectedController);
                Debug.Log($"找到 DOTweenAnimationController: {selectedController.name}");
            }
        }

        private void CreateNewController()
        {
            GameObject go = new GameObject("DOTweenAnimationController");
            selectedController = go.AddComponent<DOTweenAnimationController>();
            Selection.activeGameObject = go;
            controllerSO = new SerializedObject(selectedController);
            Debug.Log("创建新的 DOTweenAnimationController");
        }

        private void CreateNewSequence()
        {
            var newSequence = new AnimationSequence($"序列_{selectedController.animationSequences.Count + 1}");
            selectedController.animationSequences.Add(newSequence);
            selectedSequenceIndex = selectedController.animationSequences.Count - 1;
            selectedAnimationIndex = -1;
            selectedTargetIndex = -1;
            MarkControllerDirty();
        }

        private void DeleteSelectedSequence()
        {
            if (selectedSequenceIndex >= 0 && selectedSequenceIndex < selectedController.animationSequences.Count)
            {
                var sequence = selectedController.animationSequences[selectedSequenceIndex];
                if (EditorUtility.DisplayDialog("确认删除", $"确定要删除序列 '{sequence.sequenceName}' 吗？", "删除", "取消"))
                {
                    selectedController.animationSequences.RemoveAt(selectedSequenceIndex);
                    selectedSequenceIndex = -1;
                    selectedAnimationIndex = -1;
                    selectedTargetIndex = -1;
                    MarkControllerDirty();
                }
            }
        }

        private void AddAnimationToSequence()
        {
            if (selectedSequenceIndex >= 0 && selectedSequenceIndex < selectedController.animationSequences.Count)
            {
                var sequence = selectedController.animationSequences[selectedSequenceIndex];
                var newAnimation = new AnimationData($"动画_{sequence.animations.Count + 1}");
                sequence.AddAnimation(newAnimation);
                selectedAnimationIndex = sequence.animations.Count - 1;
                selectedTargetIndex = -1;
                MarkControllerDirty();
            }
        }

        private void RemoveAnimationFromSequence()
        {
            if (selectedSequenceIndex >= 0 && selectedAnimationIndex >= 0)
            {
                var sequence = selectedController.animationSequences[selectedSequenceIndex];
                if (selectedAnimationIndex < sequence.animations.Count)
                {
                    var animation = sequence.animations[selectedAnimationIndex];
                    if (EditorUtility.DisplayDialog("确认删除", $"确定要删除动画 '{animation.animationName}' 吗？", "删除", "取消"))
                    {
                        sequence.RemoveAnimation(selectedAnimationIndex);
                        selectedAnimationIndex = -1;
                        selectedTargetIndex = -1;
                        MarkControllerDirty();
                    }
                }
            }
        }

        private void ImportFromJSON()
        {
            if (selectedController == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择一个 DOTweenAnimationController", "确定");
                return;
            }

            string path = EditorUtility.OpenFilePanel("导入 JSON 配置", "", "json");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string jsonContent = File.ReadAllText(path);
                    selectedController.ImportFromJSON(jsonContent);
                    controllerSO = new SerializedObject(selectedController);
                    MarkControllerDirty();
                    Debug.Log("JSON 配置导入成功 (UnityEvent 未导入)");
                }
                catch (System.Exception e)
                {
                    EditorUtility.DisplayDialog("导入失败", $"导入 JSON 配置失败: {e.Message}", "确定");
                }
            }
        }

        private void ExportToJSON()
        {
            if (selectedController == null)
            {
                EditorUtility.DisplayDialog("错误", "请先选择一个 DOTweenAnimationController", "确定");
                return;
            }

            string path = EditorUtility.SaveFilePanel("导出 JSON 配置", "", "AnimationConfig", "json");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string jsonContent = selectedController.ExportToJSON();
                    File.WriteAllText(path, jsonContent);
                    Debug.Log($"JSON 配置导出成功: {path}");
                }
                catch (System.Exception e)
                {
                    EditorUtility.DisplayDialog("导出失败", $"导出 JSON 配置失败: {e.Message}", "确定");
                }
            }
        }

        private void MarkControllerDirty()
        {
            EditorUtility.SetDirty(selectedController);
            if (controllerSO != null) controllerSO.Update();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject != null)
            {
                var controller = Selection.activeGameObject.GetComponent<DOTweenAnimationController>();
                if (controller != null && controller != selectedController)
                {
                    selectedController = controller;
                    controllerSO = new SerializedObject(selectedController);
                    selectedSequenceIndex = -1;
                    selectedAnimationIndex = -1;
                    selectedTargetIndex = -1;
                    Repaint();
                }
            }
        }

        /// <summary>
        /// 扫描并克隆修复重复 TransformData 引用（返回修复数量）
        /// </summary>
        private int FixDuplicateTransformReferences(AnimationData anim)
        {
            if (anim == null || anim.targetTransforms == null) return 0;
            int fixedCount = 0;
            var seen = new HashSet<TransformData>();
            for (int i = 0; i < anim.targetTransforms.Count; i++)
            {
                var td = anim.targetTransforms[i];
                if (td == null)
                {
                    anim.targetTransforms[i] = new TransformData();
                    fixedCount++;
                    continue;
                }
                if (seen.Contains(td))
                {
                    anim.targetTransforms[i] = td.Clone();
                    fixedCount++;
                }
                else
                {
                    seen.Add(td);
                }
            }
            return fixedCount;
        }
    }
}
#endif
