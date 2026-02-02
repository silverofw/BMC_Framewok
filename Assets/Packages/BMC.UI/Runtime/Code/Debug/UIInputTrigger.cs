using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
            if (Keyboard.current.escapeKey.isPressed)
            {
                if (Application.platform == RuntimePlatform.Android)
                {
                    UIMgr.Instance.closeJoypadPanel();
                }
                return;
            }

            if (Keyboard.current.f2Key.isPressed)
            {
                UIMgr.Instance.ShowPanel<DebugPanel>(UICanvasType.UI_Debug).Forget();
            }
        }
    }
}
