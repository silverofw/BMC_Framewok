using System;
using System.Threading;

namespace InfiniteMap.Unity
{
    /// <summary>
    /// 負責統一派發 Entity 的全域唯一 ID (long / int64)
    /// 同時提供反向解析功能，供遊戲邏輯判定物件屬性
    /// </summary>
    public static class EntityGuidFactory
    {
        // 靜態 ID 的保留上限 (一千萬)
        public const long MaxStaticId = 10000000L;

        // 基準時間 (2024-01-01)，用來縮小時間戳佔用的位元數
        private static readonly long EpochTicks = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        // 序列號佔用的位元數 (12位元)
        private const int SequenceBits = 12;
        private const long SequenceMask = -1L ^ (-1L << SequenceBits);

        // 內部計數器狀態
        private static long _staticCounter = 0;
        private static long _lastDynamicTicks = 0;
        private static long _sequence = 0;

        // =========================================================
        // ID 生成 API
        // =========================================================

        /// <summary>
        /// (僅限 Editor 或存檔初始化時使用) 獲取下一個靜態物件 ID
        /// </summary>
        public static long GetNextStaticGuid()
        {
            long id = Interlocked.Increment(ref _staticCounter);
            if (id > MaxStaticId)
            {
                UnityEngine.Debug.LogError("[GuidFactory] 靜態物件 ID 已超過一千萬的保留區段！");
            }
            return id;
        }

        /// <summary>
        /// 提供給編輯器，手動設置目前的靜態計數器進度
        /// </summary>
        public static void SetStaticCounter(long currentMaxId)
        {
            Interlocked.Exchange(ref _staticCounter, currentMaxId);
        }

        /// <summary>
        /// (遊戲運行時使用) 獲取全域唯一的動態物件 ID (玩家、怪物、動態掉落物)
        /// </summary>
        public static long GetNextDynamicGuid()
        {
            long currentTicks = DateTime.UtcNow.Ticks - EpochTicks;

            lock (typeof(EntityGuidFactory))
            {
                if (currentTicks == _lastDynamicTicks)
                {
                    _sequence = (_sequence + 1) & SequenceMask;
                    if (_sequence == 0)
                    {
                        currentTicks = WaitNextTick(_lastDynamicTicks);
                    }
                }
                else
                {
                    _sequence = 0L;
                }

                _lastDynamicTicks = currentTicks;

                return (currentTicks << SequenceBits) | _sequence;
            }
        }

        private static long WaitNextTick(long lastTicks)
        {
            long currentTicks = DateTime.UtcNow.Ticks - EpochTicks;
            while (currentTicks <= lastTicks)
            {
                currentTicks = DateTime.UtcNow.Ticks - EpochTicks;
            }
            return currentTicks;
        }

        // =========================================================
        // 反向解析與資訊判定 API (提供給遊戲邏輯使用)
        // =========================================================

        /// <summary>
        /// 判斷這個 ID 是否為地圖編輯器生成的「靜態/預設物件」
        /// </summary>
        public static bool IsStaticGuid(long guid)
        {
            return guid > 0 && guid <= MaxStaticId;
        }

        /// <summary>
        /// 判斷這個 ID 是否為遊戲運行時生成的「動態物件」
        /// </summary>
        public static bool IsDynamicGuid(long guid)
        {
            return guid > MaxStaticId;
        }

        /// <summary>
        /// 從動態 GUID 中反向解析出它的「建立時間」
        /// </summary>
        /// <param name="guid">要解析的實體 ID</param>
        /// <param name="convertToLocalTime">是否轉換為本地時區時間 (預設為 true)。若為 false 則回傳 UTC 時間</param>
        /// <returns>建立時間。如果傳入的是靜態 GUID，將會回傳 null。</returns>
        public static DateTime? GetCreationTime(long guid, bool convertToLocalTime = true)
        {
            // 靜態 GUID 沒有包含時間資訊
            if (IsStaticGuid(guid))
            {
                return null;
            }

            // 反向推算：將 GUID 向右位移 (抹除序列號)，然後加回基準時間 (Epoch)
            long timeTicks = (guid >> SequenceBits) + EpochTicks;

            DateTime creationTimeUtc = new DateTime(timeTicks, DateTimeKind.Utc);

            return convertToLocalTime ? creationTimeUtc.ToLocalTime() : creationTimeUtc;
        }
    }
}