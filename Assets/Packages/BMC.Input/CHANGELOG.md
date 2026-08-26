# Changelog

## [1.2.0]

### Added
- `InputService.ActiveScheme`：玩家最後一次實際操作的 control scheme
  （`Gamepad` / `Keyboard&Mouse`），按鍵與方向輸入都會更新它，改變時發出
  `ActiveSchemeChanged`。
- `InputService.GetDisplayString(InputButton)`：把裝置無關的 `InputButton` 翻成
  目前裝置上的實體按鍵名（手把 `A`、鍵盤 `J`）。走 InputSystem 內建的
  `GetBindingDisplayString`，所以鍵位改在 `.inputactions`、或玩家自己重新綁定，
  畫面上的提示都會跟著變，不必另外維護一張對照表。

用途是畫面上的按鍵提示（「按 A 繼續」這類），提示要能跟著玩家手上的裝置換字。
需要資產有設定 control scheme 分組，名稱要與 `SchemeGamepad` / `SchemeKeyboard` 一致。

## [1.1.0]

改用 Unity 6 的 Project-wide Actions，不再自己輪詢裝置。

### Changed
- `InputService` 改成綁定 `InputSystem.actions` 裡的 action，按鍵事件直接接
  `action.started` / `action.canceled`。鍵位、死區、composite（WASD 合成方向）
  全部回歸 `.inputactions` 資產，程式碼裡不再有第二張對應表。
- 每幀的部分改掛 `InputSystem.onAfterUpdate`。

### Removed
- `InputLoop.cs`：不再需要自己注入 PlayerLoop。

### Regained
執行期改鍵（`PerformInteractiveRebinding`）、control scheme、`<Joystick>` 與
`<XRController>` 綁定、interaction 與 processor、Input Debugger —— 這些在 1.0.0
的自行輪詢版本裡是拿不到的。

### Notes
仍然不使用 `PlayerInput`。Project-wide Actions 由 InputSystem 在
`InitializeInPlayer` 自動 Enable 並全程保持，本來就是全域單例；`PlayerInput` 是
「每個玩家一份」的舊模型，兩顆指向同一份資產時會在 `OnDisable` 互相關掉對方的
action map。

## [1.0.0]

### Added
- `InputService`：全域唯一的輸入來源，送出裝置無關的按鍵、方向與右類比事件，
  並提供「持續按住」的狀態查詢。
- `InputLoop`：把 `InputService.Tick` 插進 PlayerLoop。
- `InputButton` / `InputDirection`：裝置無關的按鍵與方向代號。

### Notes
- 必須放在 AOT 組件。HybridCLR 的熱更 DLL 不會被掃描
  `RuntimeInitializeOnLoadMethod`，放在熱更側不會被呼叫。
