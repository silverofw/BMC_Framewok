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
        // =========================================================
        private const long DynamicFlag = 1L << 62;

        // =========================================================
        // 靜態 GUID 配置 (Bit 62 = 0)
        // =========================================================
        // 原本使用計數器，為了解決「未完全加載地圖時不知道最大計數」的盲區，
        // 這裡將 42 個位元改為【時間驅動 (Time-based)】：
        // 31 bits: 秒數 (足以從 2024 年使用至 2092 年不溢位)
        // 11 bits: 序列號 (允許編輯器【每秒】產生 2048 個靜態物件)
        private const int StaticTimeBits = 31;
        private const int StaticSeqBits = 11;
        private const long StaticSeqMask = (1L << StaticSeqBits) - 1L;
        private const int StaticCounterBits = StaticTimeBits + StaticSeqBits; // 總共 42 bits

        // 剩餘高位元留給地圖編號 (容量可達 1,048,575 張地圖)
        public static int CurrentZoneId { get; set; } = 0;

        // 內部靜態時間與序列狀態
        private static long _lastStaticSec = 0;
        private static long _staticSequence = 0;

        // =========================================================
        // 動態 GUID 配置 (Bit 62 = 1)
        // =========================================================
        // 基準時間 (2024-01-01)
        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 序列號佔用的位元數 (12位元，每毫秒可產生 4096 個動態物件)
        private const int SequenceBits = 12;
        private const long SequenceMask = -1L ^ (-1L << SequenceBits);

        // 內部動態計數器
        private static long _lastDynamicMs = 0;
        private static long _dynamicSequence = 0;

        // =========================================================
        // 靜態 ID 生成 API (完全防碰撞升級)
        // =========================================================

        /// <summary>
        /// (Editor 手動放置或存檔初始化使用) 獲取具備地圖前綴的下一個靜態物件 ID。
        /// 【升級】：改用時間戳記驅動，不需要預先載入全地圖也能保證 ID 絕對不重複！
        /// </summary>
        public static long GetNextStaticGuid()
        {
            long currentSec = (long)(DateTime.UtcNow - Epoch).TotalSeconds;

            lock (typeof(EntityGuidFactory))
            {
                if (currentSec == _lastStaticSec)
                {
                    _staticSequence = (_staticSequence + 1) & StaticSeqMask;
                    if (_staticSequence == 0)
                    {
                        // 如果編輯器腳本在一秒內狂塞超過 2048 個物件，強制等待至下一秒
                        currentSec = WaitNextSecond(_lastStaticSec);
                    }
                }
                else
                {
                    _staticSequence = 0L;
                }

                _lastStaticSec = currentSec;

                // 確保秒數不會超過 31 bits 的安全範圍
                long timePart = currentSec & ((1L << StaticTimeBits) - 1L);
                
                // 組合局部 ID (31 bits 時間 + 11 bits 序列)
                long localId = (timePart << StaticSeqBits) | _staticSequence;

                // 組合最終 GUID
                long finalGuid = ((long)CurrentZoneId << StaticCounterBits) | localId;

                // 【新增防線】：驗證生成的靜態 GUID 是否合法
                if (finalGuid < 0 || (finalGuid & DynamicFlag) != 0)
                {
                    UnityEngine.Debug.LogError($"[EntityGuidFactory] 嚴重錯誤：產生的靜態 GUID 異常 ({finalGuid})！\n" +
                                            $"ZoneId: {CurrentZoneId}, LocalId: {localId}。可能發生位元溢位！");
                }

                return finalGuid;
            }
        }

        private static long WaitNextSecond(long lastSec)
        {
            long currentSec = (long)(DateTime.UtcNow - Epoch).TotalSeconds;
            while (currentSec <= lastSec)
            {
                Thread.Sleep(1);
                currentSec = (long)(DateTime.UtcNow - Epoch).TotalSeconds;
            }
            return currentSec;
        }

        /// <summary>
        /// [已廢棄] 由於靜態 ID 已升級為時間驅動，不再需要手動設置計數器。
        /// 保留此方法以相容舊有程式碼呼叫，但內部不再執行任何邏輯。
        /// </summary>
        public static void SetStaticCounter(long currentMaxId)
        {
            // Do nothing
        }

        /// <summary>
        /// [已廢棄] 由於靜態 ID 已升級為時間驅動，讀檔時不再需要掃描並更新最大值。
        /// 保留此方法以相容舊有程式碼呼叫，但內部不再執行任何邏輯。
        /// </summary>
        public static void UpdateStaticCounterMax(long loadedId)
        {
            // Do nothing
        }

        // =========================================================
        // 動態 ID 與解析 API
        // =========================================================

        /// <summary>
        /// 獲取全域唯一的動態物件 ID (遊戲運行時生成，包含自動生成地圖)
        /// </summary>
        public static long GetNextDynamicGuid()
        {
            long currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

            lock (typeof(EntityGuidFactory))
            {
                if (currentMs == _lastDynamicMs)
                {
                    _dynamicSequence = (_dynamicSequence + 1) & SequenceMask;
                    if (_dynamicSequence == 0)
                    {
                        currentMs = WaitNextMillis(_lastDynamicMs);
                    }
                }
                else
                {
                    _dynamicSequence = 0L;
                }

                _lastDynamicMs = currentMs;

                long finalGuid = DynamicFlag | (currentMs << SequenceBits) | _dynamicSequence;

                // 【新增防線】：動態 GUID 的最高位元(符號位元)不能為 1，必須是大於 0 的正數
                if (finalGuid < 0)
                {
                    UnityEngine.Debug.LogError($"[EntityGuidFactory] 嚴重錯誤：產生的動態 GUID 異常 ({finalGuid})！\n" +
                                            $"時間戳記(ms): {currentMs}。系統時間可能異常或發生位元溢位！");
                }

                return finalGuid;
            }
        }

        private static long WaitNextMillis(long lastMs)
        {
            long currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            while (currentMs <= lastMs)
            {
                // 使用極短的自旋等待
                currentMs = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            }
            return currentMs;
        }

        // =========================================================
        // 反向解析與資訊判定 API
        // =========================================================

        public static bool IsStaticGuid(long guid)
        {
            return guid > 0 && (guid & DynamicFlag) == 0;
        }

        public static bool IsDynamicGuid(long guid)
        {
            return (guid & DynamicFlag) != 0;
        }

        public static DateTime? GetCreationTime(long guid, bool convertToLocalTime = true)
        {
            if (IsStaticGuid(guid)) return null; // 為確保與舊存檔的相容性，不反解靜態時間

            long timeMs = (guid & ~DynamicFlag) >> SequenceBits;
            DateTime creationTimeUtc = Epoch.AddMilliseconds(timeMs);

            return convertToLocalTime ? creationTimeUtc.ToLocalTime() : creationTimeUtc;
        }
    }
}