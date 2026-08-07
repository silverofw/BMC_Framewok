# Changelog

本套件的重要變更皆記錄於此。
格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版本號採用[語意化版本](https://semver.org/lang/zh-TW/)。

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
