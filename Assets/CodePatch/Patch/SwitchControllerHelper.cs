using System;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

public static class SwitchControllerHelper
{
    // ==========================================
    // 全域連線設定 (你可以直接在這裡修改，或在 GameManager 的 Start 裡給值)
    // ==========================================
    public static string pi_IP = "192.168.0.18";
    public static string port = "5000";

    // ==========================================
    // 供其他腳本快速呼叫的靜態方法
    // ==========================================
    public static void PressButton_A() => SendSwitchCommand("A");
    public static void PressButton_B() => SendSwitchCommand("B");
    public static void PressButton_X() => SendSwitchCommand("X");
    public static void PressButton_Y() => SendSwitchCommand("Y");
    public static void PressButton_HOME() => SendSwitchCommand("HOME");
    public static void PressButton_UP() => SendSwitchCommand("DPAD_UP");
    public static void PressButton_DOWN() => SendSwitchCommand("DPAD_DOWN");
    public static void PressButton_LEFT() => SendSwitchCommand("DPAD_LEFT");
    public static void PressButton_RIGHT() => SendSwitchCommand("DPAD_RIGHT");

    // ==========================================
    // 核心發送方法 (靜態)
    // ==========================================
    public static void SendSwitchCommand(string buttonName)
    {
        // 使用 UniTask 的 Fire-and-forget 模式，不卡頓主執行緒
        PostCommandAsync(buttonName).Forget();
    }

    private static async UniTaskVoid PostCommandAsync(string button)
    {
        // ⚠️ 注意：如果出現 "Insecure connection not allowed" 錯誤
        // 請至 Unity 頂部選單 Edit > Project Settings > Player > Other Settings
        // 找到 "Allow downloads over HTTP" 並改為 "Always allowed"
        string url = $"http://{pi_IP}:{port}/press/{button}";

        // 建立一個空的表單，確保 Unity 100% 以標準 POST 格式送出請求
        WWWForm form = new WWWForm();

        // 發送 POST 請求 (使用 WWWForm 寫法，完美避開警告與 405 錯誤)
        using (UnityWebRequest www = UnityWebRequest.Post(url, form))
        {
            // 設定 Timeout 避免網路不穩時 Unity 卡死 (單位為秒)
            www.timeout = 5;

            try
            {
                // 因為是靜態類別沒有生命週期，我們直接 await 等待請求完成
                await www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"[樹莓派連線失敗] 無法發送 {button} 鍵指令: {www.error}");
                }
                else
                {
                    Debug.Log($"🎮 [指令成功] 已經向樹莓派發送按下: {button}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"⚠️ 發送指令已取消: {button}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"發送指令時發生未知錯誤: {ex.Message}");
            }
        }
    }
}