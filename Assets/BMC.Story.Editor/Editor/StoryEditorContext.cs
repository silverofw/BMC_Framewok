using UnityEditor;
using System.IO;
using UnityEngine;
using Google.Protobuf;
using System.Linq;
using System.Collections.Generic;

namespace BMC.Story.Editor
{
    public static class StoryEditorContext
    {
        private const string PREF_KEY_ID = "BMC_Story_CurrentChapterID";
        public const string BASE_FOLDER = "Assets/yoo/DefaultPackage/Proto";
        private const string FILE_FORMAT = "StoryPackage_{0}.bytes";

        public static int CurrentChapterId
        {
            get => EditorPrefs.GetInt(PREF_KEY_ID, 1);
            set => EditorPrefs.SetInt(PREF_KEY_ID, value);
        }

        public static string CurrentFilePath => GetPathById(CurrentChapterId);

        public static string GetPathById(int id)
        {
            string fileName = string.Format(FILE_FORMAT, id);
            return Path.Combine(BASE_FOLDER, fileName);
        }

        // ===================================================================================
        // IO Operations
        // ===================================================================================

        public static StoryPackage LoadPackage(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return StoryPackage.Parser.ParseFrom(bytes);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[StoryEditorContext] Load Error: {e.Message}");
                return null;
            }
        }

        public static StoryNode LoadNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            var package = LoadPackage(CurrentFilePath);
            return package?.Nodes.FirstOrDefault(n => n.Id == nodeId);
        }

        public static void SavePackage(StoryPackage package, string path)
        {
            if (package == null) return;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                // 如果檔案存在，執行自動備份
                if (File.Exists(path)) PerformBackup(path);

                using (var output = File.Create(path)) package.WriteTo(output);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[StoryEditorContext] Save Error: {e.Message}");
            }
        }

        private static void PerformBackup(string originalPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(originalPath);
                string fileName = Path.GetFileNameWithoutExtension(originalPath);
                string extension = Path.GetExtension(originalPath);

                // 修改：使用 "Backups~" 避免 Unity 生成 Meta 檔
                string backupDir = Path.Combine(dir, "Backups~");

                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // 檔名格式: Name_Timestamp.ext.bak
                string backupPath = Path.Combine(backupDir, $"{fileName}_{timestamp}{extension}.bak");

                File.Copy(originalPath, backupPath, true);
                Debug.Log($"[StoryEditorContext] Backup created: {backupPath}");

                CleanUpOldBackups(backupDir, fileName);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StoryEditorContext] Backup failed: {e.Message}");
            }
        }

        private static void CleanUpOldBackups(string backupDir, string baseFileName)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(backupDir);
                var files = directoryInfo.GetFiles($"{baseFileName}_*.bak")
                                         .OrderByDescending(f => f.CreationTime)
                                         .ToList();

                if (files.Count > 5)
                {
                    for (int i = 5; i < files.Count; i++)
                    {
                        try { files[i].Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        // --- 取得可用備份列表 ---
        public static List<string> GetAvailableBackups(string originalPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(originalPath);
                string fileName = Path.GetFileNameWithoutExtension(originalPath);

                // 修改：讀取 "Backups~"
                string backupDir = Path.Combine(dir, "Backups~");

                if (!Directory.Exists(backupDir)) return new List<string>();

                var directoryInfo = new DirectoryInfo(backupDir);
                return directoryInfo.GetFiles($"{fileName}_*.bak")
                                    .OrderByDescending(f => f.CreationTime)
                                    .Select(f => f.FullName)
                                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        // --- 還原指定備份 ---
        public static bool RestoreBackup(string backupPath, string targetPath)
        {
            if (!File.Exists(backupPath)) return false;
            try
            {
                File.Copy(backupPath, targetPath, true);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[StoryEditorContext] Restore failed: {e.Message}");
                return false;
            }
        }

        // --- 清除所有備份 ---
        public static void ClearAllBackups(string originalPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(originalPath);
                string fileName = Path.GetFileNameWithoutExtension(originalPath);

                // 修改：清除 "Backups~" 內的檔案
                string backupDir = Path.Combine(dir, "Backups~");

                if (!Directory.Exists(backupDir)) return;

                var directoryInfo = new DirectoryInfo(backupDir);
                var files = directoryInfo.GetFiles($"{fileName}_*.bak");

                foreach (var file in files)
                {
                    try { file.Delete(); } catch { }
                }

                Debug.Log($"[StoryEditorContext] Cleared {files.Length} backup files for '{fileName}'.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StoryEditorContext] Failed to clear backups: {e.Message}");
            }
        }

        public static StoryPackage CreateDefaultPackage(string chapterId)
        {
            StoryPackage newPackage = new StoryPackage();
            newPackage.ChapterId = chapterId;
            newPackage.Nodes.Add(new StoryNode { Id = "Start", VideoPath = "" });
            return newPackage;
        }

        // ===================================================================================
        // Node Operations
        // ===================================================================================

        public static StoryNode CreateNewNode(StoryPackage package)
        {
            string baseId = "Node";
            int count = package.Nodes.Count + 1;
            string newId = $"{baseId}_{count}";
            while (package.Nodes.Any(n => n.Id == newId))
            {
                count++;
                newId = $"{baseId}_{count}";
            }
            return CreateSpecificNode(package, newId);
        }

        public static StoryNode CreateSpecificNode(StoryPackage package, string newId)
        {
            if (package.Nodes.Any(n => n.Id == newId)) return null;
            var newNode = new StoryNode { Id = newId, VideoPath = "" };
            package.Nodes.Add(newNode);
            return newNode;
        }

        public static void DeleteNode(StoryPackage package, string nodeId)
        {
            var nodeInList = package.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (nodeInList != null) package.Nodes.Remove(nodeInList);
        }

        public static void DeleteNodes(StoryPackage package, List<string> nodeIds)
        {
            foreach (var id in nodeIds) DeleteNode(package, id);
        }

        public static int DeleteNodeAndCleanReferences(StoryPackage package, string nodeIdToDelete)
        {
            int removedChoicesCount = 0;
            foreach (var otherNode in package.Nodes)
            {
                var choicesToRemove = otherNode.Choices.Where(c => c.TargetNodeId == nodeIdToDelete).ToList();
                foreach (var c in choicesToRemove)
                {
                    otherNode.Choices.Remove(c);
                    removedChoicesCount++;
                }
            }
            DeleteNode(package, nodeIdToDelete);
            return removedChoicesCount;
        }

        public static int DeleteNodesAndCleanReferences(StoryPackage package, HashSet<string> nodesToDelete)
        {
            int removedRefs = 0;
            foreach (var node in package.Nodes)
            {
                if (nodesToDelete.Contains(node.Id)) continue;

                var toRemove = node.Choices.Where(c => nodesToDelete.Contains(c.TargetNodeId)).ToList();
                foreach (var c in toRemove)
                {
                    node.Choices.Remove(c);
                    removedRefs++;
                }
            }

            var listToRemove = package.Nodes.Where(n => nodesToDelete.Contains(n.Id)).ToList();
            foreach (var n in listToRemove) package.Nodes.Remove(n);

            return removedRefs;
        }

        public static int RenameNode(StoryPackage package, string oldId, string newId)
        {
            var node = package.Nodes.FirstOrDefault(n => n.Id == oldId);
            if (node == null) return 0;
            node.Id = newId;
            int count = 0;
            foreach (var n in package.Nodes)
            {
                foreach (var c in n.Choices)
                {
                    if (c.TargetNodeId == oldId)
                    {
                        c.TargetNodeId = newId;
                        count++;
                    }
                }
            }
            return count;
        }

        // ===================================================================================
        // Dependency & Query Logic
        // ===================================================================================

        public static bool IsNodeReferenced(StoryPackage package, string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return false;
            return package.Nodes.Any(n => n.Choices.Any(c => c.TargetNodeId == targetId));
        }

        public static List<string> GetOrphanedNodes(StoryPackage package)
        {
            var referencedIds = new HashSet<string>();
            foreach (var node in package.Nodes)
            {
                foreach (var choice in node.Choices)
                {
                    if (!string.IsNullOrEmpty(choice.TargetNodeId)) referencedIds.Add(choice.TargetNodeId);
                }
            }
            var orphans = new List<string>();
            foreach (var node in package.Nodes)
            {
                if (node.Id == "Start") continue;
                if (!referencedIds.Contains(node.Id)) orphans.Add(node.Id);
            }
            return orphans;
        }

        public static HashSet<string> GetDependentNodes(StoryPackage package, string rootNodeId)
        {
            var nodesToDelete = new HashSet<string>();
            var queue = new Queue<string>();

            nodesToDelete.Add(rootNodeId);

            var rootNode = package.Nodes.FirstOrDefault(n => n.Id == rootNodeId);
            if (rootNode != null)
            {
                foreach (var choice in rootNode.Choices)
                {
                    if (!string.IsNullOrEmpty(choice.TargetNodeId)) queue.Enqueue(choice.TargetNodeId);
                }
            }

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();

                if (nodesToDelete.Contains(currentId)) continue;

                var currentNode = package.Nodes.FirstOrDefault(n => n.Id == currentId);
                if (currentNode == null) continue;

                bool isReferencedExternally = package.Nodes.Any(n =>
                    !nodesToDelete.Contains(n.Id) &&
                    n.Choices.Any(c => c.TargetNodeId == currentId)
                );

                if (isReferencedExternally)
                {
                    continue;
                }
                else
                {
                    nodesToDelete.Add(currentId);
                    foreach (var choice in currentNode.Choices)
                    {
                        if (!string.IsNullOrEmpty(choice.TargetNodeId)) queue.Enqueue(choice.TargetNodeId);
                    }
                }
            }
            return nodesToDelete;
        }

        public static string GenerateUniqueNextID(StoryPackage package, StoryNode sourceNode)
        {
            string baseId = $"{sourceNode.Id}_Next";
            string finalId = baseId;
            int suffix = 1;
            while (package.Nodes.Any(n => n.Id == finalId) || sourceNode.Choices.Any(c => c.TargetNodeId == finalId))
            {
                finalId = $"{baseId}_{suffix}";
                suffix++;
            }
            return finalId;
        }
    }
}