using System;
using System.Threading;

namespace InfiniteMap.Unity
{
    /// <summary>
    /// 負責統一派發 Entity 的全域唯一 ID (long / int64)
    /// 完全移除十進位數量限制，改用業界標準的二進位分區 (Bit-Shifting) 產生複合 ID。
    /// </summary>
    public static class EntityGuidFactory
    {
        // =========================================================
        // 全域旗標：使用第 62 位元來絕對區隔「動態」與「靜態」
        // (這樣兩者的 ID 區間永遠不會重疊，無需靠估算數值大小)
        // =========================================================
        private const long DynamicFlag = 1L << 62;

        // =========================================================
        // 靜態 GUID 配置 (Bit 62 = 0)
        // =========================================================
        // 撥出 42 個位元給局部計數器 (容量高達 4.3兆 / 4,398,046,511,103，等同無限)
        private const int StaticCounterBits = 42;
        private const long StaticCounterMask = (1L << StaticCounterBits) - 1L;

        // 剩餘高位元留給地圖編號 (容量可達 1,048,575 張地圖)
        public static int CurrentZoneId { get; set; } = 0;

        // 內部靜態局部計數器
        private static long _staticCounter = 0;


        // =========================================================
        // 動態 GUID 配置 (Bit 62 = 1)
        // =========================================================
        // 基準時間 (2024-01-01)，【安全修正】：使用 TotalMilliseconds 而非 Ticks，
        // 避免幾年後向左位移 12 位元時發生 long 數值溢位 (Overflow)
        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 序列號佔用的位元數 (12位元，每毫秒可產生 4096 個動態物件)
        private const int SequenceBits = 12;
        private const long SequenceMask = -1L ^ (-1L << SequenceBits);

        // 內部動態計數器
        private static long _lastDynamicMs = 0;
        private static long _sequence = 0;

        // =========================================================
        // ID 生成 API
        // =========================================================

        /// <summary>
        /// (僅限 Editor 或存檔初始化時使用) 獲取具備地圖前綴的下一個靜態物件 ID
        /// </summary>
        public static long GetNextStaticGuid()
        {
            long localId = Interlocked.Increment(ref _staticCounter);

            // 使用二進位位移：將 ZoneId 移至高位，並在低位放入局部計數器
            return ((long)CurrentZoneId << StaticCounterBits) | (localId & StaticCounterMask);
        }

        /// <summary>
        /// 提供給編輯器，手動設置目前的靜態計數器進度
        /// (利用遮罩，自動過濾掉地圖前綴，只取單純的流水號)
        /// </summary>
        public static void SetStaticCounter(long currentMaxId)
        {
            long localId = currentMaxId & StaticCounterMask;
            Interlocked.Exchange(ref _staticCounter, localId);
        }

        /// <summary>
        /// 安全地將靜態計數器推升到指定的 ID (只進不退)
        /// 用於讀檔時動態更新全域最大 GUID
        /// </summary>
        public static void UpdateStaticCounterMax(long loadedId)
        {
            if (!IsStaticGuid(loadedId)) return;

            // 防呆機制：如果讀取到的 ID 不屬於當前地圖(例如從別張圖搬過來的)，不影響本地計數器
            long zoneId = loadedId >> StaticCounterBits;
            if (zoneId != CurrentZoneId) return;

            long localId = loadedId & StaticCounterMask;
            long current;
            do
            {
                current = Interlocked.Read(ref _staticCounter);
                if (localId <= current) break;
            }
            while (Interlocked.CompareExchange(ref _staticCounter, localId, current) != current);
        }

        // =========================================================
        // 動態 ID 與解析 API
        // =========================================================

        /// <summary>
        /// (遊戲運行時使用) 獲取全域唯一的動態物件 ID (玩家、怪物、動態掉落物)
        /// </summary>
        public static long GetNextDynamicGuid()
        {
            long currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

            lock (typeof(EntityGuidFactory))
            {
                if (currentMs == _lastDynamicMs)
                {
                    _sequence = (_sequence + 1) & SequenceMask;
                    if (_sequence == 0)
                    {
                        currentMs = WaitNextMillis(_lastDynamicMs);
                    }
                }
                else
                {
                    _sequence = 0L;
                }

                _lastDynamicMs = currentMs;

                // 加上 DynamicFlag 旗標，確保絕對不會與 StaticGuid 重疊
                return DynamicFlag | (currentMs << SequenceBits) | _sequence;
            }
        }

        private static long WaitNextMillis(long lastMs)
        {
            long currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            while (currentMs <= lastMs)
            {
                currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            }
            return currentMs;
        }

        // =========================================================
        // 反向解析與資訊判定 API (提供給遊戲邏輯使用)
        // =========================================================

        /// <summary>
        /// 判斷這個 ID 是否為地圖編輯器生成的「靜態/預設物件」
        /// (只要 ID大於0，且沒有包含動態旗標，就是靜態物件)
        /// </summary>
        public static bool IsStaticGuid(long guid)
        {
            return guid > 0 && (guid & DynamicFlag) == 0;
        }

        /// <summary>
        /// 判斷這個 ID 是否為遊戲運行時生成的「動態物件」
        /// </summary>
        public static bool IsDynamicGuid(long guid)
        {
            return (guid & DynamicFlag) != 0;
        }

        /// <summary>
        /// 從動態 GUID 中反向解析出它的「建立時間」
        /// </summary>
        public static DateTime? GetCreationTime(long guid, bool convertToLocalTime = true)
        {
            if (IsStaticGuid(guid))
            {
                return null;
            }

            // 反向推算：移除 DynamicFlag，再向右位移抹除 Sequence，即得毫秒數
            long timeMs = (guid & ~DynamicFlag) >> SequenceBits;
            DateTime creationTimeUtc = Epoch.AddMilliseconds(timeMs);

            return convertToLocalTime ? creationTimeUtc.ToLocalTime() : creationTimeUtc;
        }
    }
}