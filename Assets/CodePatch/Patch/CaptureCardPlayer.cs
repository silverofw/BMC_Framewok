using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks; // 引入 UniTask 命名空間

// 強制要求掛載此腳本的物件必須要有 AudioSource 元件
[RequireComponent(typeof(AudioSource))]
public class CaptureCardPlayer : BMC.UI.UIPanel
{
    [Header("--- 系統開關 ---")]
    [Tooltip("勾選：由 Unity 處理聲音 (會有微小延遲)\n取消勾選：不處理聲音，交給 OBS 擷取 (零延遲)")]
    public bool processAudioInUnity = false; // 預設為 false，推薦交給 OBS

    [Header("--- 影像設定 ---")]
    [Tooltip("用來顯示擷取卡畫面的 UI 元件")]
    public RawImage displayImage;
    [Tooltip("指定影像設備名稱 (留空則自動抓第一個)")]
    public string targetVideoDeviceName = "";

    [Header("--- 音訊設定 ---")]
    [Tooltip("指定音訊設備名稱關鍵字 (例如: USB3 PLUS)")]
    public string targetAudioDeviceName = "USB3 PLUS";

    // 私有變數：用來儲存影像與音訊的參考
    private WebCamTexture webcamTexture;
    private AudioSource audioSource;
    private string selectedAudioDevice;

    void Start()
    {
        // 初始化影像與音訊
        InitializeVideo();
        // 根據布林值開關，決定是否要啟動 Unity 的音訊讀取
        if (processAudioInUnity)
        {
            InitializeAudio();
        }
        else
        {
            Debug.Log("[系統] 音訊處理已關閉，請在 OBS 中直接擷取擷取卡音訊以達到零延遲。");
        }
    }

    // ==========================================
    // 1. 影像處理邏輯
    // ==========================================
    private void InitializeVideo()
    {
        WebCamDevice[] videoDevices = WebCamTexture.devices;

        if (videoDevices.Length == 0)
        {
            Debug.LogError("[影像] 找不到任何影像擷取設備！");
            return;
        }

        string selectedVideoDevice = videoDevices[0].name;

        Debug.Log("--- 偵測到的影像設備 ---");
        for (int i = 0; i < videoDevices.Length; i++)
        {
            Debug.Log($"影像設備 [{i}]: {videoDevices[i].name}");

            if (!string.IsNullOrEmpty(targetVideoDeviceName) && videoDevices[i].name.Contains(targetVideoDeviceName))
            {
                selectedVideoDevice = videoDevices[i].name;
            }
        }
        Debug.Log("------------------------");

        // 初始化 WebCamTexture (建議強制指定 1920x1080, 60FPS 確保畫質)
        webcamTexture = new WebCamTexture(selectedVideoDevice, 1920, 1080, 60);

        if (displayImage != null)
        {
            displayImage.texture = webcamTexture;
        }

        webcamTexture.Play();
        Debug.Log($"[影像] 正在播放: {selectedVideoDevice}");
    }

    // ==========================================
    // 2. 音訊處理邏輯
    // ==========================================
    private void InitializeAudio()
    {
        audioSource = GetComponent<AudioSource>();

        string[] audioDevices = Microphone.devices;

        if (audioDevices.Length == 0)
        {
            Debug.LogWarning("[音訊] 找不到任何麥克風輸入設備，將只有畫面沒有聲音。");
            return;
        }

        selectedAudioDevice = audioDevices[0];

        Debug.Log("--- 偵測到的音訊設備 ---");
        for (int i = 0; i < audioDevices.Length; i++)
        {
            Debug.Log($"音訊設備 [{i}]: {audioDevices[i]}");

            if (!string.IsNullOrEmpty(targetAudioDeviceName) && audioDevices[i].Contains(targetAudioDeviceName))
            {
                selectedAudioDevice = audioDevices[i];
            }
        }
        Debug.Log("------------------------");

        // 使用 UniTask 啟動非同步麥克風讀取，並加上 Forget() 避免編譯器警告
        StartMicrophoneAsync().Forget();
    }

    // 將原本的 IEnumerator 改為 async UniTaskVoid
    private async UniTaskVoid StartMicrophoneAsync()
    {
        // 開始錄製麥克風 (擷取卡聲音)
        audioSource.clip = Microphone.Start(selectedAudioDevice, true, 1, 48000);
        audioSource.loop = true;

        // 使用 UniTask.WaitUntil 等待緩衝，取代原本的 while (yield return null)
        // 這樣寫不僅效能更好，語意也更明確
        await UniTask.WaitUntil(() => Microphone.GetPosition(selectedAudioDevice) > 0);

        audioSource.Play();
        Debug.Log($"[音訊] 正在播放: {selectedAudioDevice}");
    }

    // ==========================================
    // 3. 資源釋放
    // ==========================================
    protected override void OnDestroy()
    {
        base.OnDestroy();
        // 停止影像
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
            Debug.Log("[系統] 已釋放影像設備");
        }

        // 停止音訊
        if (selectedAudioDevice != null && Microphone.IsRecording(selectedAudioDevice))
        {
            Microphone.End(selectedAudioDevice);
            Debug.Log("[系統] 已釋放音訊設備");
        }
    }
}