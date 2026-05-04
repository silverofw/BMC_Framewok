using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Collections.Generic;
using InfiniteMap.Proto;

namespace InfiniteMap.Unity
{
    /// <summary>
    /// 掛載在 Unity 場景中的組件，用來綁定 Inspector 參數並控制純 C# 的 InfiniteWorldController
    /// </summary>
    public class InfiniteWorldMono : MonoBehaviour
    {
        [Header("世界設定")]
        [Tooltip("世界編號 (0 代表 Zone_0)")]
        public int worldId = 0;

        [Header("核心設置")]
        [Tooltip("每個 Chunk 的大小 (幾格)")]
        public int chunkSize = 16;

        [Tooltip("載入半徑 (1代表中心+周圍一圈共9個Chunk, 2代表共25個Chunk)")]
        public int loadRadius = 1;

        [Header("網格視覺化設定")]
        public float tileSize = 1.0f; // Unity中一格代表多大 (例如 1 Unit = 1 格)
        public bool showChunkGizmos = true;

        [Header("測試與除錯設定")]
        [Tooltip("用來測試生成的 GameObject 預製件 (例如一個 Cube)")]
        public GameObject testEntityPrefab;

        [Tooltip("選填：將場景中的物件拖入此欄位。若有綁定，將自動追蹤此物件的位置作為焦點")]
        public Transform debugFocusTarget;

        [SerializeField]
        [Tooltip("當前的焦點座標 (唯讀顯示，或在未綁定 debugFocusTarget 時可手動輸入)")]
        private Vector3 _currentFocusPosition = Vector3.zero;

        /// <summary>
        /// 當前焦點位置。
        /// </summary>
        public Vector3 CurrentFocusPosition
        {
            get => _currentFocusPosition;
            set => _currentFocusPosition = value;
        }

        // 純 C# 的底層控制器
        public InfiniteWorldController Controller { get; private set; }

        private string _saveBasePath;
        private Dictionary<long, GameObject> _activeTestEntities = new Dictionary<long, GameObject>();

        private void Awake()
        {
            Controller = new InfiniteWorldController();

            Controller.OnEntitySpawn += HandleEntitySpawn;
            Controller.OnEntitySerialize += HandleEntitySerialize;
            Controller.OnEntityDestroy += HandleEntityDestroy;

            _saveBasePath = Path.Combine(Application.persistentDataPath, "WorldSaves");
            Controller.Init(worldId, chunkSize, loadRadius, tileSize, _saveBasePath);
        }

        private void Update()
        {
            if (debugFocusTarget != null)
            {
                CurrentFocusPosition = debugFocusTarget.position;
            }

            if (Controller != null)
            {
                Controller.Tick(CurrentFocusPosition);
            }

            // 測試流程：結合 EntityGuidFactory 測試靜態與動態 ID
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                // 按住 Shift 鍵代表模擬「地圖編輯器」擺放靜態物件
                bool isStatic = Keyboard.current.shiftKey.isPressed;
                SpawnTestEntityAtFocus(isStatic);
            }
        }

        // =========================================================
        // 測試流程實作
        // =========================================================

        /// <summary>
        /// 測試功能：在當前焦點位置建立一個新實體
        /// </summary>
        public void SpawnTestEntityAtFocus(bool isStatic)
        {
            if (testEntityPrefab == null)
            {
                Debug.LogWarning("[Test] 請先在 Inspector 中綁定 Test Entity Prefab！");
                return;
            }

            // 1. 【關鍵更改】透過 GuidFactory 取得正確規範的 ID
            long newGuid = isStatic ? EntityGuidFactory.GetNextStaticGuid() : EntityGuidFactory.GetNextDynamicGuid();

            // 2. 將 Unity 座標轉為地圖框架的 Pos3
            Pos3 pos3 = new Pos3(
                Mathf.FloorToInt(CurrentFocusPosition.x / tileSize),
                Mathf.FloorToInt(CurrentFocusPosition.z / tileSize),
                Mathf.FloorToInt(CurrentFocusPosition.y / tileSize)
            );

            // 3. 通知地圖底層註冊
            Controller.AddRuntimeEntity(newGuid, pos3);

            // 4. 在畫面上實際把它建立出來
            CreateTestGameObject(newGuid, pos3);

            string typeName = isStatic ? "靜態(預設)" : "動態(玩家)";
            Debug.Log($"[Test] 手動建立 {typeName} 實體。GUID: {newGuid}，座標 {pos3}");
        }

        private void CreateTestGameObject(long guid, Pos3 pos)
        {
            if (testEntityPrefab == null || _activeTestEntities.ContainsKey(guid)) return;

            Vector3 unityPos = new Vector3(
                pos.x * tileSize + (tileSize / 2f),
                pos.h * tileSize,
                pos.y * tileSize + (tileSize / 2f)
            );

            GameObject go = Instantiate(testEntityPrefab, unityPos, Quaternion.identity);

            // 讓 GameObject 的名字也反映出它是哪種實體，方便在 Hierarchy 觀察
            string typePrefix = EntityGuidFactory.IsStaticGuid(guid) ? "Static" : "Dynamic";
            go.name = $"{typePrefix}_{guid}";

            _activeTestEntities[guid] = go;
        }

        // --- 框架事件接聽 ---

        /// <summary>
        /// 當玩家靠近，區塊載入時觸發：把存檔中的實體生出來
        /// </summary>
        private void HandleEntitySpawn(EntityProto proto, long lastSaveTimeUnix)
        {
            Pos3 pos = new Pos3(proto.Pos.X, proto.Pos.Y, proto.Pos.H);
            CreateTestGameObject(proto.Guid, pos);

            string typeName = "未知";
            if (EntityGuidFactory.IsStaticGuid(proto.Guid)) typeName = "靜態(預設)";
            else if (EntityGuidFactory.IsDynamicGuid(proto.Guid)) typeName = "動態(玩家)";

            // 計算距離上次卸載經過了多久
            string offlineLog = "";
            if (lastSaveTimeUnix > 0)
            {
                // 將 Unix 時間戳轉回 DateTimeOffset
                System.DateTimeOffset lastSaveTime = System.DateTimeOffset.FromUnixTimeSeconds(lastSaveTimeUnix);
                System.TimeSpan offlineDuration = System.DateTimeOffset.UtcNow - lastSaveTime;

                // 【修改此處】改為使用 TotalSeconds，並轉型為整數 (或使用 :F0) 來顯示總秒數
                int totalOfflineSeconds = (int)offlineDuration.TotalSeconds;

                // 實務上，您就是把這個 totalOfflineSeconds 傳給 ECS 的組件去扣除/增加數值
                offlineLog = $" (離線 {totalOfflineSeconds} 秒)";
            }

            Debug.Log($"[Test] 從存檔還原 {typeName} 實體。GUID: {proto.Guid}{offlineLog}");
        }

        private EntityProto HandleEntitySerialize(long guid)
        {
            if (_activeTestEntities.TryGetValue(guid, out GameObject go))
            {
                Pos3 currentPos = new Pos3(
                    Mathf.FloorToInt(go.transform.position.x / tileSize),
                    Mathf.FloorToInt(go.transform.position.z / tileSize),
                    Mathf.FloorToInt(go.transform.position.y / tileSize)
                );

                EntityProto proto = new EntityProto
                {
                    Guid = guid,
                    ConfigId = 999,
                    Pos = new Pos3Proto { X = currentPos.x, Y = currentPos.y, H = currentPos.h }
                };

                proto.Props.Add(1, 100);
                return proto;
            }
            return null;
        }

        private void HandleEntityDestroy(long guid)
        {
            if (_activeTestEntities.TryGetValue(guid, out GameObject go))
            {
                // 【修正】判斷當前是否還在遊戲模式
                // 應對 OnApplicationQuit 時因為非同步延遲，導致切換回編輯模式的錯誤
                if (Application.isPlaying)
                {
                    Destroy(go);
                }
                else
                {
                    DestroyImmediate(go);
                }

                _activeTestEntities.Remove(guid);
                Debug.Log($"[Test] 區塊卸載，銷毀實體 GUID:{guid}");
            }
        }

        // =========================================================
        // 開放給遊戲流程呼叫的便利接口
        // =========================================================

        public async void SaveGame()
        {
            if (Controller != null) await Controller.ForceSaveAllAsync();
        }

        public async void SwitchToZone(int newZoneId, Vector3 newSpawnPosition)
        {
            if (Controller != null)
            {
                worldId = newZoneId;
                CurrentFocusPosition = newSpawnPosition;
                await Controller.SwitchZoneAsync(newZoneId);
            }
        }

        private void OnApplicationQuit()
        {
            if (Controller != null) Controller.SwitchZoneAsync(worldId).Wait(1000);
        }

        private void OnDrawGizmos()
        {
            if (!showChunkGizmos) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(CurrentFocusPosition, tileSize * 0.5f);
            Gizmos.DrawLine(CurrentFocusPosition - Vector3.left * tileSize, CurrentFocusPosition + Vector3.left * tileSize);
            Gizmos.DrawLine(CurrentFocusPosition - Vector3.forward * tileSize, CurrentFocusPosition + Vector3.forward * tileSize);
        }
    }
}