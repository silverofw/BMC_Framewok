using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using BMC.Core;

namespace BMC.Patch.Core
{
    public class AudioMgr : Singleton<AudioMgr>
    {
        private Transform root;
        private Transform poolRoot;

        // 真正的音效來源物件池
        private List<AudioSource> audioSourcePool = new List<AudioSource>();

        // 音效播放器模板
        private AudioSource audioSourceTemplate;

        // 可選：擴充背景音樂專用頻道
        private AudioSource bgmSource;

        // BGM 漸變專用的取消權杖，用於中斷上一次的漸變動畫
        private CancellationTokenSource bgmFadeCts;

        // 新增：明確標記當前是否正在進行 BGM 漸變，解決 bgmFadeCts 未清空導致音量設定無效的問題
        private bool isBgmFading = false;

        // --- 設定參數 ---
        private const int InitialPoolSize = 5; // 預熱的物件池大小

        // --- 音量與靜音狀態控制 (開放外部讀取，內部寫入) ---
        public float SfxVolume { get; private set; } = 1.0f;
        public float BgmVolume { get; private set; } = 1.0f;
        public bool IsSfxMuted { get; private set; } = false;
        public bool IsBgmMuted { get; private set; } = false;
        public bool IsGlobalMuted { get; private set; } = false;

        protected override void Init()
        {
            base.Init();

            // 建立管理器根節點，並確保其在切換場景時不會被銷毀
            root = new GameObject("[AudioMgr]").transform;
            GameObject.DontDestroyOnLoad(root.gameObject);

            // 建立物件池節點
            poolRoot = new GameObject("[Pool]").transform;
            poolRoot.SetParent(root, false);

            // 初始化模板與預熱物件池
            CreateTemplate();
            PrewarmPool();
        }

        /// <summary>
        /// 建立音效來源模板
        /// </summary>
        private void CreateTemplate()
        {
            GameObject templateGo = new GameObject("[AudioSource_Template]");
            templateGo.transform.SetParent(poolRoot, false);
            audioSourceTemplate = templateGo.AddComponent<AudioSource>();

            // 模板預設為隱藏 (停用狀態視為已回收)
            templateGo.SetActive(false);
        }

        /// <summary>
        /// 預先實例化一定數量的 AudioSource，避免遊戲中途 Instantiate 造成卡頓
        /// </summary>
        private void PrewarmPool()
        {
            for (int i = 0; i < InitialPoolSize; i++)
            {
                CreateNewAudioSource();
            }
        }

        /// <summary>
        /// 播放音效 (SFX)
        /// </summary>
        /// <param name="clipName">音效名稱</param>
        /// <param name="pitch">基礎音高 (預設 1)</param>
        /// <param name="randomPitchRange">隨機音高偏移範圍 (例: 0.1 代表音高會在 0.9 ~ 1.1 之間隨機跳動，增加真實感)</param>
        public void Play(string clipName, float pitch = 1.0f, float randomPitchRange = 0f)
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
            source.volume = SfxVolume;
            source.mute = IsGlobalMuted || IsSfxMuted;

            // 套用音高與隨機偏移
            source.pitch = pitch + Random.Range(-randomPitchRange, randomPitchRange);

            source.Play();

            // 4. 依照 clip 的長度，使用 UniTask 播完後回收 (為了配合 pitch 變化，播放時間需除以 pitch)
            float duration = clip.length / Mathf.Max(0.01f, source.pitch);
            RecycleAudioSourceAsync(source, duration).Forget();
        }

        /// <summary>
        /// 停止所有目前正在播放的音效 (適用於切換場景或重新開始時)
        /// </summary>
        public void StopAllSfx()
        {
            foreach (var source in audioSourcePool)
            {
                if (source.gameObject.activeSelf)
                {
                    source.Stop();
                    source.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 建立新的 AudioSource 並加入池中
        /// </summary>
        private AudioSource CreateNewAudioSource()
        {
            GameObject go = GameObject.Instantiate(audioSourceTemplate.gameObject, poolRoot);
            go.SetActive(false);
            AudioSource newSource = go.GetComponent<AudioSource>();
            audioSourcePool.Add(newSource);
            return newSource;
        }

        /// <summary>
        /// 從物件池中尋找閒置的 AudioSource，若無則透過模板實例化擴充
        /// </summary>
        private AudioSource GetAvailableAudioSource()
        {
            // 尋找被回收 (未啟用) 的 AudioSource
            for (int i = 0; i < audioSourcePool.Count; i++)
            {
                if (!audioSourcePool[i].gameObject.activeSelf)
                {
                    audioSourcePool[i].gameObject.SetActive(true);
                    return audioSourcePool[i];
                }
            }

            // 如果池子裡的都在使用中，則擴充一個新的
            AudioSource newSource = CreateNewAudioSource();
            newSource.gameObject.SetActive(true);
            return newSource;
        }

        /// <summary>
        /// 播放背景音樂 (BGM) 包含淡入淡出效果
        /// </summary>
        /// <param name="clipName">音效名稱</param>
        /// <param name="fadeDuration">淡入淡出過渡時間 (秒)</param>
        public void PlayBGM(string clipName, float fadeDuration = 1.0f)
        {
            AudioClip clip = ResMgr.Instance.LoadAsset<AudioClip>(clipName);
            if (clip == null) return;

            if (bgmSource == null)
            {
                GameObject go = new GameObject("[BGM_Source]");
                go.transform.SetParent(root, false);
                bgmSource = go.AddComponent<AudioSource>();
                bgmSource.loop = true;
            }

            // 如果要播放的 BGM 與當前相同且正在播放，就不重複處理
            if (bgmSource.clip == clip && bgmSource.isPlaying) return;

            // 取消上一次還沒完成的漸變協程
            bgmFadeCts?.Cancel();
            bgmFadeCts?.Dispose();
            bgmFadeCts = new CancellationTokenSource();

            // 綁定 GameObject 銷毀時的 Token，確保安全
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                bgmFadeCts.Token, root.gameObject.GetCancellationTokenOnDestroy());

            if (fadeDuration > 0)
            {
                isBgmFading = true; // 標記開始漸變
                FadeBGMAsync(clip, fadeDuration, linkedCts.Token).Forget();
            }
            else
            {
                // 無漸變，直接切換
                isBgmFading = false;
                bgmSource.clip = clip;
                bgmSource.volume = BgmVolume;
                bgmSource.mute = IsGlobalMuted || IsBgmMuted;
                bgmSource.Play();
            }
        }

        /// <summary>
        /// 停止背景音樂 (BGM)
        /// </summary>
        /// <param name="fadeDuration">淡出過渡時間 (秒)，若設定為 0 則立即停止</param>
        public void StopBGM(float fadeDuration = 1.0f)
        {
            if (bgmSource == null || !bgmSource.isPlaying) return;

            // 取消上一次還沒完成的漸變協程（例如原本還在淡入中，突然被要求停止）
            bgmFadeCts?.Cancel();
            bgmFadeCts?.Dispose();
            bgmFadeCts = new CancellationTokenSource();

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                bgmFadeCts.Token, root.gameObject.GetCancellationTokenOnDestroy());

            if (fadeDuration > 0)
            {
                isBgmFading = true; // 標記開始漸變
                StopBGMFadeAsync(fadeDuration, linkedCts.Token).Forget();
            }
            else
            {
                // 無漸變，直接停止
                isBgmFading = false;
                bgmSource.Stop();
                bgmSource.clip = null;
                bgmSource.volume = BgmVolume;
            }
        }

        /// <summary>
        /// 處理 BGM 的淡入淡出切換
        /// </summary>
        private async UniTaskVoid FadeBGMAsync(AudioClip nextClip, float duration, CancellationToken token)
        {
            // 1. 如果有音樂正在播放，先淡出
            if (bgmSource.isPlaying && bgmSource.clip != null)
            {
                float startVol = bgmSource.volume;
                float time = 0;
                while (time < duration && !token.IsCancellationRequested)
                {
                    time += Time.deltaTime;
                    // 將當前音量逐漸降到 0
                    bgmSource.volume = Mathf.Lerp(startVol, 0f, time / duration);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }

            // 被取消的話就直接結束
            if (token.IsCancellationRequested) return;

            // 2. 替換音樂片段並設定靜音狀態
            bgmSource.clip = nextClip;
            bgmSource.mute = IsGlobalMuted || IsBgmMuted;
            bgmSource.Play();

            // 3. 執行淡入
            float inTime = 0;
            while (inTime < duration && !token.IsCancellationRequested)
            {
                inTime += Time.deltaTime;
                // 優化：這裡不再使用固定的 targetVol，而是直接讀取最新的 BgmVolume
                // 這樣一來如果在漸變中途玩家調整了音量設定，漸變過程也能動態追蹤並平滑反映
                bgmSource.volume = Mathf.Lerp(0f, BgmVolume, inTime / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            // 確保最後音量精確，並結束漸變狀態
            if (!token.IsCancellationRequested)
            {
                bgmSource.volume = BgmVolume;
                isBgmFading = false;
            }
        }

        /// <summary>
        /// 處理 BGM 的淡出並停止
        /// </summary>
        private async UniTaskVoid StopBGMFadeAsync(float duration, CancellationToken token)
        {
            float startVol = bgmSource.volume;
            float time = 0;

            while (time < duration && !token.IsCancellationRequested)
            {
                time += Time.deltaTime;
                // 將當前音量逐漸降到 0
                bgmSource.volume = Mathf.Lerp(startVol, 0f, time / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            if (!token.IsCancellationRequested)
            {
                bgmSource.Stop();
                bgmSource.clip = null;
                bgmSource.volume = BgmVolume; // 停止後恢復原本音量設定
                isBgmFading = false; // 結束漸變狀態
            }
        }

        // ==========================================
        // 音量與靜音控制 API
        // ==========================================

        public void SetSfxVolume(float volume)
        {
            SfxVolume = Mathf.Clamp01(volume);
            for (int i = 0; i < audioSourcePool.Count; i++)
            {
                if (audioSourcePool[i] != null) audioSourcePool[i].volume = SfxVolume;
            }
        }

        public void SetSfxMute(bool isMuted)
        {
            IsSfxMuted = isMuted;
            for (int i = 0; i < audioSourcePool.Count; i++)
            {
                if (audioSourcePool[i] != null) audioSourcePool[i].mute = IsGlobalMuted || IsSfxMuted;
            }
        }

        public void SetBgmVolume(float volume)
        {
            BgmVolume = Mathf.Clamp01(volume);

            // 只有在非漸變狀態下，才立即套用音量。
            // (如果在漸變中，FadeBGMAsync 內部的 Lerp 已經會動態讀取最新的 BgmVolume)
            if (bgmSource != null && !isBgmFading)
            {
                bgmSource.volume = BgmVolume;
            }
        }

        public void SetBgmMute(bool isMuted)
        {
            IsBgmMuted = isMuted;
            if (bgmSource != null) bgmSource.mute = IsGlobalMuted || IsBgmMuted;
        }

        /// <summary>
        /// 設定全局靜音 (同時影響 BGM 與 SFX，但不覆蓋各自獨立的靜音狀態)
        /// </summary>
        public void SetGlobalMute(bool isMuted)
        {
            IsGlobalMuted = isMuted;

            // 更新所有 SFX
            for (int i = 0; i < audioSourcePool.Count; i++)
            {
                if (audioSourcePool[i] != null) audioSourcePool[i].mute = IsGlobalMuted || IsSfxMuted;
            }

            // 更新 BGM
            if (bgmSource != null) bgmSource.mute = IsGlobalMuted || IsBgmMuted;
        }

        /// <summary>
        /// 使用 UniTask 處理延遲回收
        /// </summary>
        private async UniTaskVoid RecycleAudioSourceAsync(AudioSource source, float delay)
        {
            if (source == null) return;

            var token = source.GetCancellationTokenOnDestroy();
            bool isCancelled = await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: token).SuppressCancellationThrow();

            if (!isCancelled && source != null)
            {
                source.gameObject.SetActive(false);
            }
        }
    }
}