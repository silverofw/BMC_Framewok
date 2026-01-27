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
                string backupDir = Path.Combine(dir, "Backups~");

                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(backupDir, $"{fileName}_{timestamp}{extension}.bak");

                File.Copy(originalPath, backupPath, true);
                CleanUpOldBackups(backupDir, fileName);
            }
            catch { }
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
                    for (int i = 5; i < files.Count; i++) try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }

        public static List<string> GetAvailableBackups(string originalPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(originalPath);
                string fileName = Path.GetFileNameWithoutExtension(originalPath);
                string backupDir = Path.Combine(dir, "Backups~");

                if (!Directory.Exists(backupDir)) return new List<string>();

                var directoryInfo = new DirectoryInfo(backupDir);
                return directoryInfo.GetFiles($"{fileName}_*.bak")
                                    .OrderByDescending(f => f.CreationTime)
                                    .Select(f => f.FullName)
                                    .ToList();
            }
            catch { return new List<string>(); }
        }

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

        public static void ClearAllBackups(string originalPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(originalPath);
                string backupDir = Path.Combine(dir, "Backups~");
                if (Directory.Exists(backupDir))
                {
                    string fileName = Path.GetFileNameWithoutExtension(originalPath);
                    foreach (var file in new DirectoryInfo(backupDir).GetFiles($"{fileName}_*.bak"))
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        public static StoryPackage CreateDefaultPackage(string chapterId)
        {
            StoryPackage newPackage = new StoryPackage();
            newPackage.ChapterId = chapterId;
            newPackage.Nodes.Add(new StoryNode { Id = "Start" });
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
            var newNode = new StoryNode { Id = newId };
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
            int removedRefs = 0;
            foreach (var otherNode in package.Nodes)
            {
                removedRefs += RemoveReferencesTo(otherNode, nodeIdToDelete);
            }
            DeleteNode(package, nodeIdToDelete);
            return removedRefs;
        }

        public static int DeleteNodesAndCleanReferences(StoryPackage package, HashSet<string> nodesToDelete)
        {
            int removedRefs = 0;
            foreach (var node in package.Nodes)
            {
                if (nodesToDelete.Contains(node.Id)) continue;
                // 從保留的節點中移除對將刪除節點的引用
                foreach (var delId in nodesToDelete)
                {
                    removedRefs += RemoveReferencesTo(node, delId);
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
                count += RenameTargetIdInNode(n, oldId, newId);
            }
            return count;
        }

        // ===================================================================================
        // Event-Aware Reference Logic (Cleaned)
        // ===================================================================================

        /// <summary>
        /// 獲取節點中所有對外的跳轉引用 (包含 AutoJump, ShowChoices, Games 等)
        /// </summary>
        public static IEnumerable<string> GetTargetNodeIds(StoryNode node)
        {
            if (!string.IsNullOrEmpty(node.AutoJumpNodeId)) yield return node.AutoJumpNodeId;

            foreach (var evt in node.OnEnterEvents.Concat(node.OnExitEvents))
            {
                foreach (var id in GetTargetIdsFromEvent(evt)) yield return id;
            }
        }

        private static IEnumerable<string> GetTargetIdsFromEvent(StoryEvent evt)
        {
            switch (evt.ActionCase)
            {
                case StoryEvent.ActionOneofCase.ShowChoices:
                    foreach (var c in evt.ShowChoices.Choices)
                        if (!string.IsNullOrEmpty(c.TargetNodeId)) yield return c.TargetNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameDice:
                    if (!string.IsNullOrEmpty(evt.GameDice.SuccessNodeId)) yield return evt.GameDice.SuccessNodeId;
                    if (!string.IsNullOrEmpty(evt.GameDice.FailNodeId)) yield return evt.GameDice.FailNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameRussianRoulette:
                    if (!string.IsNullOrEmpty(evt.GameRussianRoulette.WinNodeId)) yield return evt.GameRussianRoulette.WinNodeId;
                    if (!string.IsNullOrEmpty(evt.GameRussianRoulette.LoseNodeId)) yield return evt.GameRussianRoulette.LoseNodeId;
                    break;
                case StoryEvent.ActionOneofCase.GameQte:
                    if (!string.IsNullOrEmpty(evt.GameQte.SuccessNodeId)) yield return evt.GameQte.SuccessNodeId;
                    if (!string.IsNullOrEmpty(evt.GameQte.FailNodeId)) yield return evt.GameQte.FailNodeId;
                    break;
            }
        }

        /// <summary>
        /// 將節點內所有指向 oldId 的引用改為 newId
        /// </summary>
        public static int RenameTargetIdInNode(StoryNode node, string oldId, string newId)
        {
            int count = 0;
            if (node.AutoJumpNodeId == oldId) { node.AutoJumpNodeId = newId; count++; }

            foreach (var evt in node.OnEnterEvents.Concat(node.OnExitEvents))
            {
                switch (evt.ActionCase)
                {
                    case StoryEvent.ActionOneofCase.ShowChoices:
                        foreach (var c in evt.ShowChoices.Choices)
                            if (c.TargetNodeId == oldId) { c.TargetNodeId = newId; count++; }
                        break;
                    case StoryEvent.ActionOneofCase.GameDice:
                        if (evt.GameDice.SuccessNodeId == oldId) { evt.GameDice.SuccessNodeId = newId; count++; }
                        if (evt.GameDice.FailNodeId == oldId) { evt.GameDice.FailNodeId = newId; count++; }
                        break;
                    case StoryEvent.ActionOneofCase.GameRussianRoulette:
                        if (evt.GameRussianRoulette.WinNodeId == oldId) { evt.GameRussianRoulette.WinNodeId = newId; count++; }
                        if (evt.GameRussianRoulette.LoseNodeId == oldId) { evt.GameRussianRoulette.LoseNodeId = newId; count++; }
                        break;
                    case StoryEvent.ActionOneofCase.GameQte:
                        if (evt.GameQte.SuccessNodeId == oldId) { evt.GameQte.SuccessNodeId = newId; count++; }
                        if (evt.GameQte.FailNodeId == oldId) { evt.GameQte.FailNodeId = newId; count++; }
                        break;
                }
            }
            return count;
        }

        /// <summary>
        /// 移除節點內指向 targetId 的所有引用 (清空 ID 或刪除選項)
        /// </summary>
        public static int RemoveReferencesTo(StoryNode node, string targetId)
        {
            int count = 0;
            if (node.AutoJumpNodeId == targetId) { node.AutoJumpNodeId = ""; count++; }

            foreach (var evt in node.OnEnterEvents.Concat(node.OnExitEvents))
            {
                if (evt.ActionCase == StoryEvent.ActionOneofCase.ShowChoices)
                {
                    // 對於選項，若目標刪除，則移除該選項
                    var toRemove = evt.ShowChoices.Choices.Where(c => c.TargetNodeId == targetId).ToList();
                    foreach (var c in toRemove) { evt.ShowChoices.Choices.Remove(c); count++; }
                }
                else
                {
                    // 對於遊戲結果，僅清空 ID
                    switch (evt.ActionCase)
                    {
                        case StoryEvent.ActionOneofCase.GameDice:
                            if (evt.GameDice.SuccessNodeId == targetId) evt.GameDice.SuccessNodeId = "";
                            if (evt.GameDice.FailNodeId == targetId) evt.GameDice.FailNodeId = "";
                            break;
                        case StoryEvent.ActionOneofCase.GameRussianRoulette:
                            if (evt.GameRussianRoulette.WinNodeId == targetId) evt.GameRussianRoulette.WinNodeId = "";
                            if (evt.GameRussianRoulette.LoseNodeId == targetId) evt.GameRussianRoulette.LoseNodeId = "";
                            break;
                        case StoryEvent.ActionOneofCase.GameQte:
                            if (evt.GameQte.SuccessNodeId == targetId) evt.GameQte.SuccessNodeId = "";
                            if (evt.GameQte.FailNodeId == targetId) evt.GameQte.FailNodeId = "";
                            break;
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
            return package.Nodes.Any(n => GetTargetNodeIds(n).Contains(targetId));
        }

        public static List<string> GetOrphanedNodes(StoryPackage package)
        {
            var referencedIds = new HashSet<string>();
            foreach (var node in package.Nodes)
            {
                foreach (var id in GetTargetNodeIds(node))
                {
                    referencedIds.Add(id);
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
                foreach (var id in GetTargetNodeIds(rootNode)) queue.Enqueue(id);
            }

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                if (nodesToDelete.Contains(currentId)) continue;

                var currentNode = package.Nodes.FirstOrDefault(n => n.Id == currentId);
                if (currentNode == null) continue;

                // 檢查是否有「外部」引用 (非待刪除集合內的節點指向此節點)
                bool isReferencedExternally = package.Nodes.Any(n =>
                    !nodesToDelete.Contains(n.Id) &&
                    GetTargetNodeIds(n).Contains(currentId)
                );

                if (!isReferencedExternally)
                {
                    nodesToDelete.Add(currentId);
                    foreach (var id in GetTargetNodeIds(currentNode)) queue.Enqueue(id);
                }
            }
            return nodesToDelete;
        }

        public static string GenerateUniqueNextID(StoryPackage package, StoryNode sourceNode)
        {
            string baseId = $"{sourceNode.Id}_Next";
            string finalId = baseId;
            int suffix = 1;

            var existingIds = new HashSet<string>(package.Nodes.Select(n => n.Id));

            // Check direct match
            while (existingIds.Contains(finalId))
            {
                finalId = $"{baseId}_{suffix}";
                suffix++;
            }
            return finalId;
        }
    }
}