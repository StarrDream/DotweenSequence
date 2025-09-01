#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace DOTweenAnimationSystem.Editor
{
    /// <summary>
    /// Inspector 扩展
    /// </summary>
    [CustomEditor(typeof(DOTweenAnimationController))]
    public class DOTweenAnimationControllerEditor : UnityEditor.Editor
    {
        private DOTweenAnimationController controller;
        private SerializedProperty animationSequencesProp;
        private SerializedProperty autoPlayProp;
        private SerializedProperty autoPlaySequenceNameProp;
        private SerializedProperty autoPlayDelayProp;
        private SerializedProperty enableDebugLogProp;

        private Vector2 scrollPosition;
        private int selectedSequenceIndex = -1;

        private bool showSequenceSettings = true;
        private bool showControlButtons = true;

        private void OnEnable()
        {
            controller = (DOTweenAnimationController)target;
            animationSequencesProp = serializedObject.FindProperty("animationSequences");
            autoPlayProp = serializedObject.FindProperty("autoPlay");
            autoPlaySequenceNameProp = serializedObject.FindProperty("autoPlaySequenceName");
            autoPlayDelayProp = serializedObject.FindProperty("autoPlayDelay");
            enableDebugLogProp = serializedObject.FindProperty("enableDebugLog");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            DrawTitle();
            EditorGUILayout.Space();

            DrawBasicSettings();
            EditorGUILayout.Space();

            DrawSequenceSettings();
            EditorGUILayout.Space();

            DrawControlButtons();
            EditorGUILayout.Space();

            if (GUILayout.Button("打开动画编辑器窗口", GUILayout.Height(28)))
            {
                DOTweenAnimationEditorWindow.ShowWindow();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTitle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("DOTween 动画控制器", titleStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBasicSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(autoPlayProp, new GUIContent("自动播放"));
            if (autoPlayProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(autoPlaySequenceNameProp, new GUIContent("自动播放序列名称"));
                EditorGUILayout.PropertyField(autoPlayDelayProp, new GUIContent("自动播放延迟"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(enableDebugLogProp, new GUIContent("启用调试日志"));
            EditorGUILayout.EndVertical();
        }

        private void DrawSequenceSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            showSequenceSettings = EditorGUILayout.Foldout(showSequenceSettings, "动画序列设置", true);

            if (GUILayout.Button("添加序列", GUILayout.Width(80)))
            {
                AddNewSequence();
            }
            EditorGUILayout.EndHorizontal();

            if (showSequenceSettings)
            {
                EditorGUI.indentLevel++;
                DrawSequenceList();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSequenceList()
        {
            if (animationSequencesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("没有动画序列。点击 '添加序列' 创建。", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(280));
            for (int i = 0; i < animationSequencesProp.arraySize; i++)
            {
                DrawSequenceItem(i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSequenceItem(int index)
        {
            var sequenceProp = animationSequencesProp.GetArrayElementAtIndex(index);
            var sequenceNameProp = sequenceProp.FindPropertyRelative("sequenceName");
            var animationsProp = sequenceProp.FindPropertyRelative("animations");
            var isParallelProp = sequenceProp.FindPropertyRelative("isParallel");
            var delayProp = sequenceProp.FindPropertyRelative("delay");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            bool isSelected = selectedSequenceIndex == index;
            Color oldColor = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.5f, 0.85f, 1f, 0.5f);

            if (GUILayout.Button($"{sequenceNameProp.stringValue} ({animationsProp.arraySize} 动画)", EditorStyles.label))
            {
                selectedSequenceIndex = isSelected ? -1 : index;
            }
            GUI.backgroundColor = oldColor;

            if (Application.isPlaying)
            {
                string seqName = sequenceNameProp.stringValue;
                if (controller.IsSequencePlaying(seqName))
                {
                    if (GUILayout.Button("停止", GUILayout.Width(40)))
                        controller.StopSequence(seqName);
                    if (GUILayout.Button("暂停", GUILayout.Width(40)))
                        controller.PauseSequence(seqName);
                }
                else
                {
                    if (GUILayout.Button("播放", GUILayout.Width(40)))
                        controller.PlaySequence(seqName);
                }
            }

            if (GUILayout.Button("↑", GUILayout.Width(25))) MoveSequence(index, -1);
            if (GUILayout.Button("↓", GUILayout.Width(25))) MoveSequence(index, 1);
            if (GUILayout.Button("×", GUILayout.Width(25))) RemoveSequence(index);

            EditorGUILayout.EndHorizontal();

            if (isSelected)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(sequenceNameProp, new GUIContent("名称"));
                EditorGUILayout.PropertyField(isParallelProp, new GUIContent("并行播放"));
                EditorGUILayout.PropertyField(delayProp, new GUIContent("延迟"));
                EditorGUILayout.LabelField("总时长(安全)", $"{ComputeTempDuration(sequenceProp):F2} s");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private float ComputeTempDuration(SerializedProperty sequenceProp)
        {
            var animationsProp = sequenceProp.FindPropertyRelative("animations");
            bool isParallel = sequenceProp.FindPropertyRelative("isParallel").boolValue;
            float delay = sequenceProp.FindPropertyRelative("delay").floatValue;

            float total = 0f;
            float max = 0f;
            for (int i = 0; i < animationsProp.arraySize; i++)
            {
                var animProp = animationsProp.GetArrayElementAtIndex(i);
                var targetsProp = animProp.FindPropertyRelative("targetTransforms");
                float animSum = 0f;
                for (int t = 0; t < targetsProp.arraySize; t++)
                {
                    var targetTD = targetsProp.GetArrayElementAtIndex(t);
                    float d = targetTD.FindPropertyRelative("duration").floatValue;
                    if (d < 0f) d = 0f;
                    animSum += d;
                }
                if (isParallel)
                {
                    if (animSum > max) max = animSum;
                }
                else
                {
                    total += animSum;
                }
            }
            return (isParallel ? max : total) + delay;
        }

        private void DrawControlButtons()
        {
            EditorGUILayout.BeginVertical("box");
            showControlButtons = EditorGUILayout.Foldout(showControlButtons, "控制面板", true);
            if (showControlButtons)
            {
                EditorGUILayout.BeginHorizontal();
                if (Application.isPlaying)
                {
                    if (GUILayout.Button("停止所有"))
                        controller.StopAllSequences();
                    if (GUILayout.Button("暂停所有"))
                        controller.PauseAllSequences();
                    if (GUILayout.Button("恢复所有"))
                        controller.ResumeAllSequences();
                }
                else
                {
                    if (GUILayout.Button("重置所有到起始位置"))
                        Debug.Log("（示意）可在此加入批量重置逻辑");
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        #region 操作

        private void AddNewSequence()
        {
            animationSequencesProp.arraySize++;
            var newSeq = animationSequencesProp.GetArrayElementAtIndex(animationSequencesProp.arraySize - 1);
            newSeq.FindPropertyRelative("sequenceName").stringValue = $"新序列 {animationSequencesProp.arraySize}";
            newSeq.FindPropertyRelative("isParallel").boolValue = false;
            newSeq.FindPropertyRelative("delay").floatValue = 0f;
            newSeq.FindPropertyRelative("animations").arraySize = 0;
            selectedSequenceIndex = animationSequencesProp.arraySize - 1;
            serializedObject.ApplyModifiedProperties();
        }

        private void RemoveSequence(int index)
        {
            if (EditorUtility.DisplayDialog("确认删除", "确定要删除该序列？", "删除", "取消"))
            {
                animationSequencesProp.DeleteArrayElementAtIndex(index);
                if (selectedSequenceIndex == index) selectedSequenceIndex = -1;
                else if (selectedSequenceIndex > index) selectedSequenceIndex--;
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void MoveSequence(int index, int direction)
        {
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < animationSequencesProp.arraySize)
            {
                animationSequencesProp.MoveArrayElement(index, newIndex);
                if (selectedSequenceIndex == index) selectedSequenceIndex = newIndex;
                else if (selectedSequenceIndex == newIndex) selectedSequenceIndex = index;
                serializedObject.ApplyModifiedProperties();
            }
        }

        #endregion
    }
}
#endif
