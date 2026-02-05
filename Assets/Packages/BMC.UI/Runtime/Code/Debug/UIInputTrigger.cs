using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BMC.UI
{
    /// <summary>
    /// 監控 ESC 與 F2 輸入
    /// 
    /// </summary>
    public class UIInputTrigger : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod]
        static void OnRuntimeInitialized()
        {
            // 獲取當前場景名稱
            string currentSceneName = SceneManager.GetActiveScene().name;

            // 判定場景名稱：如果「不是」Patch，就印出警告並直接返回，不執行後續邏輯
            if (currentSceneName != "Patch")
            {
                Debug.LogWarning($"當前場景為 {currentSceneName}，非 Patch 場景，跳過初始化。");
                return;
            }
            // 訂閱Unity的錯誤日誌事件
            Application.logMessageReceived += HandleLog;
            Application.quitting += Close;

            Debug.Log("Runtime initialized: First scene loaded: After Awake is called.");
            var go = new GameObject("[UIInputTrigger]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<UIInputTrigger>();
        }

        static void Close()
        {
            // 取消訂閱Unity的錯誤日誌事件
            Application.logMessageReceived -= HandleLog;
            Application.quitting -= Close;
        }

        static void HandleLog(string logString, string stackTrace, LogType type)
        {
            // 如果日誌類型是錯誤或例外，則打印日誌信息
            if (type == LogType.Error || type == LogType.Exception)
            {
                //Debug.LogError($"[{type}] {logString}\n{stackTrace}");
                UIMgr.Instance.ShowPanel<MsgPanel>(UICanvasType.UI_Debug).ContinueWith((p) =>
                {
                    p.Initial($"[{type}] {logString}\n{stackTrace}", "[ERROR]", null, null);
                }).Forget();
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return; // 如果沒有偵測到鍵盤則跳出

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                //if (Application.platform == RuntimePlatform.Android)
                {
                    UIMgr.Instance.closeJoypadPanel();
                }
                return;
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                UIMgr.Instance.ShowPanel<DebugPanel>(UICanvasType.UI_Debug).Forget();
            }
        }
    }
}
