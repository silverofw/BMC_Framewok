# Changelog

本套件的重要變更皆記錄於此。
格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版本號採用[語意化版本](https://semver.org/lang/zh-TW/)。

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
