using Cysharp.Threading.Tasks;
using InfiniteMap.Proto;

namespace InfiniteMap
{
    /// <summary>
    /// 世界子系統標準介面 (純資料導向)
    /// 無論是全域系統 (如火車) 還是區域系統 (如 AtomECS)，都可以實作此介面來統一接受地圖控制器調度。
    /// </summary>
    public interface IGlobalSystem
    {
        /// <summary>
        /// 系統初始化
        /// </summary>
        void Init(int chunkSize, string saveBasePath, int worldId);

        /// <summary>
        /// 邏輯推演 (各系統自己決定要怎麼算資料)
        /// </summary>
        void Tick(int scale);

        /// <summary>
        /// 系統獨立的非同步存檔 (針對全域資料，若只依賴地塊存檔則可實作空白 UniTask)
        /// </summary>
        UniTask SaveAsync();

        /// <summary>
        /// 系統獨立的非同步讀檔 (針對全域資料，若只依賴地塊存檔則可實作空白 UniTask)
        /// </summary>
        UniTask LoadAsync();

        /// <summary>
        /// 判定這個 GUID 是否屬於本系統管轄？
        /// (Controller 會依次詢問，若返回 true，則由該系統處理該實體的存檔資料)
        /// </summary>
        bool OwnsEntity(long guid);

        /// <summary>
        /// 提供該實體的最新序列化資料 (EntityProto)
        /// 供 InfiniteWorldController 統籌存取時呼叫
        /// </summary>
        EntityProto FetchEntityData(long guid);

        /// <summary>
        /// 當地塊載入時觸發 (實體甦醒)
        /// 區域系統(如ECS)可在此將 Proto 轉為 Atom；全域系統可視需求忽略。
        /// </summary>
        void OnEntitySpawn(EntityProto proto, long lastSaveTimeUnix);

        /// <summary>
        /// 當地塊卸載時觸發 (實體休眠)
        /// 區域系統(如ECS)可在此回收記憶體；全域系統可視需求忽略。
        /// </summary>
        void OnEntityDestroy(long guid);
    }
}