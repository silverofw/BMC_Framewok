# Changelog

本套件的重要變更皆記錄於此。
格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/)，版本號採用[語意化版本](https://semver.org/lang/zh-TW/)。

## [1.1.1] - 2026-08-25

### Fixed
- 手把面板在「建立的那一幀」不再接受輸入。按鍵可能同時走兩條互不相通的路：一條讓遊戲
  邏輯把面板開起來，另一條是本套件 `JoypadInput` 每幀輪詢 `wasPressedThisFrame` 送進來的
  UI 事件。兩者沒有「事件已被消耗」的協調，同一次按壓會被剛開好的面板再吃一次 ——
  等同玩家一打開就立刻確認了游標所在的項目。實際案例：按 A 叫出環形互動選單，
  第一項是對話，結果一叫出來就直接進對話。
  `UIPanel` 新增 `OpenFrame`，`UITMgr.IsJoypadInputBlocked` 據此擋掉那一幀。

## [1.1.0] - 2026-04-09

### Changed
- 將 `BMC.UIToolkit.UIMgr` 更名為 `UITMgr`，避免與 uGUI 版 `BMC.UI.UIMgr` 在同一個檔案裡撞名。
  呼叫端改寫 `UITMgr.Instance`；uGUI 仍用 `UIMgr.Instance`。這是 breaking change。

## [1.0.1]

- 初版 UI Toolkit 介面系統。
