# Changelog

本套件的重要變更皆記錄於此。
格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版本號採用[語意化版本](https://semver.org/lang/zh-TW/)。

## [1.0.16] - 2026-08-23

### Removed
- 拆掉 chunk 載入流程的 `[DIAG]` 診斷鷹架(`InfiniteWorldController.LoadChunkFromDiskAsync`
  六處、`World.DoUpdateFocusAsync` 兩處)。這批 log 是先前排查 chunk 漏載/黑塊問題時加的，
  功能穩定後就只剩噪音，其中兩處特別吵：
  - `data 為空/解析失敗，改用 OnGenerateEmptyChunk` 用的是 `LogWarning`，但走
    `OnGenerateEmptyChunk` 是**正常路徑** —— Gate 場景的無邊際海洋就是這樣即時生成、
    不存檔的，一進場就固定噴滿載入半徑內的每一個 chunk。
  - `World.DoUpdateFocusAsync` 那兩行每次焦點更新都印，其中一行還把整個 `neededChunks`
    集合 join 成字串，角色每跨一格 chunk 就吐一次。
  順手把只為了餵這些 log 而拆出來的 `localExists`、`locationValid` 兩個暫存變數收回
  `if` 條件裡。真正的錯誤處理(`[IO]` 讀檔/解析失敗、`[Generator]` 產生器例外、
  未註冊 `OnEntitySpawn` 的警告)全部保留。

## [1.0.15] - 2026-08-14

### Fixed
- `EntityGuidFactory.GetNextStaticGuid`：靜態 guid 每秒序列號配額從 2048 提高到
  16384(`StaticSeqBits` 11 → 14)。根因：`MapGeneratorMgr.GenerateChunk` 用柏林噪聲
  程序化生成開放世界地形時，一個 16x16 chunk 光地板本體+懸空填補層就可能要價
  900~1500 個靜態 guid，玩家在同一秒內觸發兩個以上全新 chunk 生成時(移動速度快、一次
  跨越多個 chunk 邊界時很容易發生)，累計容易超過舊配額。超過配額會呼叫
  `WaitNextSecond`，內部是「持有 lock 的情況下 `Thread.Sleep(1)` 忙等到下一秒」，對於
  在主執行緒同步呼叫的生成流程來說等同整個遊戲凍結最長接近 1 秒，且因為是卡在 Sleep
  而非真的在跑程式碼，Profiler(含 Deep Profile)幾乎看不出任何有意義的呼叫堆疊可以
  展開。剩餘可用的地圖編號容量從 1,048,575 降到 131,071，仍遠大於這個遊戲實際使用的
  ZoneId 範圍(目前都在幾千以內)。

## [1.0.14] - 2026-08-14

### Fixed
- `LoadChunkFromDiskAsync`：補上真正的 `UniTask.SwitchToThreadPool()`。1.0.12 那次只是把
  `ChunkProto.Parser.ParseFrom(data)` 這行程式碼移到既有的 `SwitchToMainThread()` 之前，
  但方法從呼叫端進來就一直在主執行緒跑、中間沒有任何明確的切換點，`ParseFrom` 實際上從
  未真正離開主執行緒——實測一個存了 800+ entity 的 chunk，`ParseFrom` 反序列化要價
  40ms+，整段卡在主執行緒上。現在明確呼叫 `SwitchToThreadPool()`，跟存檔側
  `SaveChunkStateAsync` 的對稱寫法一致。

## [1.0.13] - 2026-08-13

### Changed
- `LoadChunkFromDiskAsync`：寫入 `_entityStateCache` 時不再 `Clone()` 剛解析出來的
  `EntityProto`——這個物件接下來會透過 `OnEntitySpawn` 直接交給上層當作活資料的儲存體
  本身(見 Game001 端 `StatusComponent` 改直接持有 `EntityProto` 參照的對應改動)，讓快取
  從一開始就跟活著的資料共用同一個物件，之後任何屬性變動都會直接反映在快取裡，不需要
  額外的同步/重建步驟。
- `SaveChunkStateAsync`：存檔成功分支只保留一次 `Clone()`(放進即將離開主執行緒序列化的
  `ChunkProto.Entities`，這次不能省——序列化期間主執行緒可能仍在修改同一個活物件，需要
  一份跨執行緒安全的快照)，`_entityStateCache` 的更新不再額外 `Clone()`一次(理由同上，
  兩者本來就是同一個物件)。

## [1.0.12] - 2026-08-12

### Changed
- `SaveChunkStateAsync`：`_entityStateCache` fallback 分支不再 `Clone()` 快取物件——所有
  寫入點都是整筆替換(`dict[guid] = x.Clone()`)，從不就地修改，直接沿用參照是安全的，省下
  地板類 entity(一個 chunk 常見 500~800 個)每次存檔的大量 `Clone()`/GC alloc。
- `SaveChunkStateAsync`：entity 序列化迴圈跑完之後才會用到的 `ChunkProto.ToByteArray()`
  跟 `File.WriteAllBytesAsync` 改到 `UniTask.SwitchToThreadPool()` 之後執行，不再卡在主
  執行緒——`ToByteArray()` 對一個 500~800 entity 的 chunk 實測要價 70ms+，是玩家跨 chunk
  邊界移動時卡頓的主因(entity 序列化本身仍留在主執行緒，因為需要呼叫上層的
  `OnEntitySerialize` 存取活的 Unity/ECS 物件)。
- `LoadChunkFromDiskAsync`：`ChunkProto.Parser.ParseFrom(data)`(純 CPU、不碰 Unity API)
  改到切回主執行緒之前執行，跟上面的存檔端改動對稱；`OnGenerateEmptyChunk`(程序化地圖
  產生器需要 `UnityEngine.Random`/`Mathf`)跟 `OnEntitySpawn` 維持在主執行緒不動。
- `World.DoUpdateFocusAsync`：卸載迴圈比照既有的載入分幀機制(`ChunkLoadIntervalMs`)在
  連續卸載多個 chunk 時插入間隔，避免玩家一次跨越多個 chunk 邊界(例如斜向移動)時，多個
  chunk 的存檔尖峰疊在同一批處理裡。從 `activeChunks` 移除本身維持同步、不受影響。

## [1.0.11] - 2026-08-05

### Changed
- `SaveChunkStateAsync` 的 `_entityStateCache` fallback 分支移除了逐筆 `Debug.Log`。這個
  分支原本是為了「視覺物件還沒載入完就被存檔」這種偶發情況設計的除錯訊息，但只要上層有
  entity 刻意設計成「永遠不完整實體化」(見 1.0.10 的 `GetCachedEntityData`)，這個分支就會
  變成該類 entity 每次存檔都合法會走到的常態路徑，逐筆記 log 會在數量大時嚴重拖慢存檔
  (Console 寫入本身很慢)。fallback 邏輯本身不變，只是不再逐筆記錄。

## [1.0.10] - 2026-08-05

### Added
- `InfiniteWorldController.GetCachedEntityData(guid)`/`UpdateCachedEntityData(proto)`：
  開放讀寫內部的 `_entityStateCache`，讓上層可以在不將 entity 完整實體化(例如不需要建立
  完整 ECS 物件)的情況下，直接查詢/更新某個 entity 的最新 EntityProto。給大量、被動、
  不需要主動行為的地形類 entity 使用，避免為了查詢而被迫建立完整運行時物件。

## [1.0.9] - 2026-08-04

### Fixed
- `World.DestroyAndSaveAllAsync()` 未與背景執行的 `UpdateFocusAsync`(玩家移動、或
  `Controller.Tick` 的自我修復計時器觸發，`.Forget()` 執行、無人持有其 `UniTask`)互斥：
  兩者可能同時讀寫 `activeChunks`，導致某個 chunk(甚至含玩家自己)在換區存檔時被漏存，
  或背景工作在場景重載、實體已被清空後才寫入殘缺資料。新增 `_focusIdleSignal`
  completion signal，`DestroyAndSaveAllAsync` 於 `_isDestroyed=true` 後、真正開始存檔前
  先等待任何進行中的背景串流工作完全結束。
- `InfiniteWorldController.MoveRuntimeEntity` 在目標 chunk 尚未串流載入完成時，會把實體
  從舊 chunk 移除卻加不進新 chunk，造成該實體從此在 `activeChunks` 中完全追蹤不到、下次
  存檔時憑空消失。改為目標 chunk 不存在時整個不搬移，實體維持在舊 chunk 的紀錄裡。

## [1.0.8] - 2026-07-26

### Added
- `IGlobalSystem` 全域子系統架構：`InfiniteWorldController.RegisterGlobalSystem`，並在區塊載入／卸載、實體增刪移、存檔等時機提供生命週期 hook。

### Changed
- 焦點更新與邏輯 Tick 分離：原 `Tick(Vector3)` 拆為 `UpdateWorldFocus(Vector3)`（更新載入中心）與 `Tick(int)`（驅動子系統）。
- `package.json` 補齊 `type`、`repository.directory` 與 keywords 等欄位。

### Fixed
- Editor 模式下 `TransferEntityToZoneAsync` 的寫入路徑改為 editor-aware，與載入路徑一致；修正原本寫入 persistentData 卻從 Assets 讀取、導致跨 Zone 轉移不生效的問題。
- `_fileLocks` 靜態字典無限增長：新增 `CleanupIdleFileLocks()`，於 `Init()` 回收未被持有的閒置檔案鎖。
- `GetActiveChunks()` 移除每次呼叫的反射，改用 `World.ActiveChunks` 存取器，降低實體增刪移的 GC 與 CPU 開銷。
