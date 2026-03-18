using System.Collections.Generic;
using BMC.Core;
using UnityEngine;

public class AudioMgr : BMC.Core.Singleton<AudioMgr>
{
    private Transform root;
    private Transform poolRoot;

    // 真正的音效來源物件池
    private List<AudioSource> audioSourcePool = new List<AudioSource>();

    // 可選：擴充背景音樂專用頻道
    private AudioSource bgmSource;

    protected override void Init()
    {
        base.Init();

        // 建立管理器根節點，並確保其在切換場景時不會被銷毀
        root = new GameObject("[AudioMgr]").transform;
        GameObject.DontDestroyOnLoad(root.gameObject);

        // 建立物件池節點
        poolRoot = new GameObject("[Pool]").transform;
        poolRoot.SetParent(root, false);
    }

    /// <summary>
    /// 播放音效 (SFX)
    /// </summary>
    public void Play(string clipName)
    {
        // 1. 防呆：檢查是否成功載入音效
        AudioClip clip = ResMgr.Instance.LoadAsset<AudioClip>(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"[AudioMgr] 找不到音效資源: {clipName}");
            return;
        }

        // 2. 從物件池獲取可用的 AudioSource
        AudioSource source = GetAvailableAudioSource();

        // 3. 設定並播放
        source.gameObject.name = $"[{clipName}]"; // 方便在 Hierarchy 中辨識
        source.clip = clip;
        source.Play();
    }

    /// <summary>
    /// 從物件池中尋找閒置的 AudioSource，若無則自動擴充
    /// </summary>
    private AudioSource GetAvailableAudioSource()
    {
        // 尋找沒有在播放的 AudioSource
        for (int i = 0; i < audioSourcePool.Count; i++)
        {
            if (!audioSourcePool[i].isPlaying)
            {
                return audioSourcePool[i];
            }
        }

        // 如果池子裡的都在播放，則動態擴充一個新的
        GameObject go = new GameObject("[AudioSource_Temp]");
        go.transform.SetParent(poolRoot, false);
        AudioSource newSource = go.AddComponent<AudioSource>();

        audioSourcePool.Add(newSource);
        return newSource;
    }

    /// <summary>
    /// (擴充建議) 播放背景音樂 (BGM)
    /// 通常 BGM 只需要一個 AudioSource 且需要循環播放
    /// </summary>
    public void PlayBGM(string clipName)
    {
        AudioClip clip = ResMgr.Instance.LoadAsset<AudioClip>(clipName);
        if (clip == null) return;

        if (bgmSource == null)
        {
            GameObject go = new GameObject("[BGM_Source]");
            go.transform.SetParent(root, false);
            bgmSource = go.AddComponent<AudioSource>();
            bgmSource.loop = true; // BGM 通常需要循環
        }

        bgmSource.clip = clip;
        bgmSource.Play();
    }
}