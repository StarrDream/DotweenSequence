using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DOTweenAnimationSystem
{
    [Serializable]
    public class AnimationSaveData
    {
        public List<AnimationSequence> sequences;
        public DateTime saveTime;
        public string version;
    }

    /// <summary>
    /// 数据持久化服务
    /// </summary>
    public class DOTweenAnimationDataService
    {
        private readonly Func<List<AnimationSequence>> _getSequences;
        private readonly string _saveFileName;
        private readonly bool _useCustomPath;
        private readonly string _customSavePath;

        public DOTweenAnimationDataService(
            Func<List<AnimationSequence>> sequencesProvider,
            string saveFileName,
            bool useCustomPath,
            string customSavePath)
        {
            _getSequences = sequencesProvider;
            _saveFileName = string.IsNullOrEmpty(saveFileName) ? "AnimationData" : saveFileName;
            _useCustomPath = useCustomPath;
            _customSavePath = customSavePath ?? "";
        }

        private string DefaultSavePath => Path.Combine(Application.persistentDataPath, "AnimationData");
        private string SavePath => _useCustomPath && !string.IsNullOrEmpty(_customSavePath) ? _customSavePath : DefaultSavePath;
        private string SaveFilePath => Path.Combine(SavePath, $"{_saveFileName}.json");

        #region Public API

        public bool SaveToFile(string overrideFileName = null)
        {
            try
            {
                if (!Directory.Exists(SavePath))
                    Directory.CreateDirectory(SavePath);

                string path = string.IsNullOrEmpty(overrideFileName)
                    ? SaveFilePath
                    : Path.Combine(SavePath, $"{overrideFileName}.json");

                var saveData = BuildSaveData(_getSequences());
                string json = JsonConvert.SerializeObject(saveData, Formatting.Indented, GetSerializerSettings());
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DOTweenAnimationDataService] 保存失败: {e.Message}");
                return false;
            }
        }

        public bool LoadFromFile(out List<AnimationSequence> sequences, string overrideFileName = null)
        {
            sequences = new List<AnimationSequence>();
            try
            {
                string path = string.IsNullOrEmpty(overrideFileName)
                    ? SaveFilePath
                    : Path.Combine(SavePath, $"{overrideFileName}.json");

                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[DOTweenAnimationDataService] 文件不存在: {path}");
                    return false;
                }

                string json = File.ReadAllText(path);
                return ImportFromJson(json, out sequences);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DOTweenAnimationDataService] 加载失败: {e.Message}");
                return false;
            }
        }

        public string ExportToJson()
        {
            try
            {
                var saveData = BuildSaveData(_getSequences());
                return JsonConvert.SerializeObject(saveData, Formatting.Indented, GetSerializerSettings());
            }
            catch (Exception e)
            {
                Debug.LogError($"[DOTweenAnimationDataService] 导出 JSON 失败: {e.Message}");
                return null;
            }
        }

        public bool ImportFromJson(string json, out List<AnimationSequence> sequences)
        {
            sequences = new List<AnimationSequence>();
            try
            {
                var saveData = JsonConvert.DeserializeObject<AnimationSaveData>(json, GetSerializerSettings());
                if (saveData?.sequences != null)
                {
                    sequences = saveData.sequences;
                    RestoreAllTargetsByPath(sequences);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DOTweenAnimationDataService] 解析 JSON 失败: {e.Message}");
                return false;
            }
        }

        public bool SaveFileExists(string overrideFileName = null)
        {
            string path = string.IsNullOrEmpty(overrideFileName)
                ? SaveFilePath
                : Path.Combine(SavePath, $"{overrideFileName}.json");
            return File.Exists(path);
        }

        public List<string> GetAllSaveFiles()
        {
            List<string> results = new List<string>();
            if (!Directory.Exists(SavePath)) return results;
            var files = Directory.GetFiles(SavePath, "*.json");
            foreach (var f in files)
                results.Add(Path.GetFileNameWithoutExtension(f));
            return results;
        }

        public string GetSaveDirectory() => SavePath;
        public string GetSaveFilePath(string overrideFileName = null)
            => string.IsNullOrEmpty(overrideFileName) ? SaveFilePath : Path.Combine(SavePath, $"{overrideFileName}.json");

        #endregion

        #region Build / Restore

        private AnimationSaveData BuildSaveData(List<AnimationSequence> sequences)
        {
            if (sequences == null) sequences = new List<AnimationSequence>();
            FillAllTargetPaths(sequences);
            return new AnimationSaveData
            {
                sequences = sequences,
                saveTime = DateTime.Now,
                version = "1.0"
            };
        }

        private JsonSerializerSettings GetSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
        }

        private void FillAllTargetPaths(List<AnimationSequence> sequences)
        {
            if (sequences == null) return;
            foreach (var seq in sequences)
            {
                if (seq?.animations == null) continue;
                foreach (var anim in seq.animations)
                {
                    if (anim == null) continue;
                    anim.targetObjectPath = anim.targetObject
                        ? GetHierarchyPath(anim.targetObject.transform)
                        : "";
                }
            }
        }

        private void RestoreAllTargetsByPath(List<AnimationSequence> sequences)
        {
            if (sequences == null) return;
            foreach (var seq in sequences)
            {
                if (seq?.animations == null) continue;
                foreach (var anim in seq.animations)
                {
                    if (string.IsNullOrEmpty(anim.targetObjectPath)) continue;
                    var t = FindByHierarchyPath(anim.targetObjectPath);
                    if (t) anim.targetObject = t.gameObject;
                }
            }
        }

        #endregion

        #region Hierarchy Path Utilities

        private string GetHierarchyPath(Transform t)
        {
            if (!t) return "";
            var list = new List<string>();
            while (t)
            {
                list.Add(t.name);
                t = t.parent;
            }
            list.Reverse();
            return string.Join("/", list);
        }

        private Transform FindByHierarchyPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var segs = path.Split('/');
            if (segs.Length == 0) return null;

            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            Transform current = null;
            foreach (var r in roots)
            {
                if (r.name == segs[0])
                {
                    current = r.transform;
                    break;
                }
            }

            if (!current) return null;

            for (int i = 1; i < segs.Length; i++)
            {
                current = current.Find(segs[i]);
                if (!current) return null;
            }
            return current;
        }

        #endregion
    }
}
