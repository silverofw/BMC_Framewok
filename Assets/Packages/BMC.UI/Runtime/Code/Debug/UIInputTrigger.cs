using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
// 引入 Profiler 命名空間
using UnityEngine.Profiling;

namespace BMC.UI
{
    /// <summary>
    /// 監控 ESC 與 F2 輸入，並提供 FPS 與記憶體顯示功能
    /// </summary>
    public class UIInputTrigger : MonoBehaviour
    {
        // --- 靜態開關 ---
        public static bool ShowFPS = false;

        private float _deltaTime = 0.0f;

        [RuntimeInitializeOnLoadMethod]
        static void OnRuntimeInitialized()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;

            if (currentSceneName != "Patch" && currentSceneName != "Logo")
            {
                Debug.LogWarning($"當前場景為 {currentSceneName}，非 Patch 場景，跳過初始化。");
                return;
            }

            Application.logMessageReceived += HandleLog;
            Application.quitting += Close;

            Debug.Log("Runtime initialized: First scene loaded: After Awake is called.");
            var go = new GameObject("[UIInputTrigger]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<UIInputTrigger>();
        }

        static void Close()
        {
            Application.logMessageReceived -= HandleLog;
            Application.quitting -= Close;
        }

        /// <summary>正在顯示錯誤面板。防止顯示過程中自己再丟錯而無限遞迴。</summary>
        static bool _showingError;

        // 【為什麼需要兩道防護】這個回呼掛在 Application.logMessageReceived 上，
        // 任何一則 Error/Exception 都會進來，包含「顯示面板本身失敗」丟出來的那則：
        //
        //   1. 補丁流程(下載資源)跑在 UI 之前，那時 globalCanvas 還是 null，直接 ShowPanel
        //      會丟 NullReferenceException —— 而它本身又是 Exception，於是再次進到這裡，
        //      變成錯誤風暴。實際踩過：資源初始化失敗時，同一份堆疊在 Player.log 重複好幾輪。
        //   2. 就算 UI 已就緒，ShowPanel 內部載入失敗時也會 Log.Error，同樣會捲回來。
        //
        // 所以：UI 沒就緒就只留 log 不彈面板；顯示期間用旗標擋掉重入。
        static void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception)
                return;
            if (_showingError)
                return;
            if (!UIMgr.Instance.IsGlobalCanvasReady)
                return;

            _showingError = true;
            ShowErrorPanel(type, logString, stackTrace).Forget();
        }

        static async UniTaskVoid ShowErrorPanel(LogType type, string logString, string stackTrace)
        {
            try
            {
                var panel = await UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug);
                panel?.Initial($"[{type}] {logString}\n{stackTrace}", "[ERROR]", null, null);
            }
            catch (System.Exception e)
            {
                // 【這裡只能用 Warning】用 LogError 會直接繞回 HandleLog。
                Debug.LogWarning($"[UIInputTrigger] 顯示錯誤面板失敗: {e}");
            }
            finally
            {
                _showingError = false;
            }
        }

        private void Update()
        {
            // 計算 FPS 平滑時間
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                UIMgr.Instance.closeJoypadPanel();
                return;
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                UIMgr.Instance.ShowPanel<DebugPanel>(UICanvasType.UI_Debug).Forget();
            }

            // F3 切換顯示開關
            if (keyboard.f3Key.wasPressedThisFrame)
            {
                ShowFPS = !ShowFPS;
            }
        }

        private void OnGUI()
        {
            if (!ShowFPS) return;

            int w = Screen.width, h = Screen.height;
            GUIStyle style = new GUIStyle();

            // 調整顯示區域高度，避免文字被切掉 (從 2% 改為 10%)
            Rect rect = new Rect(20, 20, w, h * 10 / 100);

            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = h * 2 / 100; // 字體大小維持

            // --- 1. 計算 FPS ---
            float msec = _deltaTime * 1000.0f;
            float fps = 1.0f / _deltaTime;

            // --- 2. 計算 Memory (MB) ---
            // GetTotalAllocatedMemoryLong: Unity 實際分配給物件與資源的記憶體
            // GetTotalReservedMemoryLong: Unity 向作業系統預留的總記憶體 (包含未使用的)
            long totalAlloc = Profiler.GetTotalAllocatedMemoryLong() / 1048576; // 1024 * 1024
            long totalReserved = Profiler.GetTotalReservedMemoryLong() / 1048576;

            // 設定顏色 (FPS < 30 紅色，否則綠色)
            if (fps < 30)
                style.normal.textColor = Color.red;
            else
                style.normal.textColor = Color.green;

            // 組合顯示文字
            // 格式: 
            // 16.6 ms (60 fps)
            // Mem: 150MB / 1024MB
            string text = string.Format("{0:0.0} ms ({1:0.} fps)\nMem: {2}MB / {3}MB",
                msec, fps, totalAlloc, totalReserved);

            GUI.Label(rect, text, style);
        }
    }
}