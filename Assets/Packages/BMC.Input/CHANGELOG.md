# Changelog

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
