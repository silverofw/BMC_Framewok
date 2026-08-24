# Changelog

本套件的重要變更皆記錄於此。
格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版本號採用[語意化版本](https://semver.org/lang/zh-TW/)。

## [1.1.0] - 2026-04-09

### Changed
- 將 `BMC.UIToolkit.UIMgr` 更名為 `UITMgr`，避免與 uGUI 版 `BMC.UI.UIMgr` 在同一個檔案裡撞名。
  呼叫端改寫 `UITMgr.Instance`；uGUI 仍用 `UIMgr.Instance`。這是 breaking change。

## [1.0.1]

- 初版 UI Toolkit 介面系統。
