# Changelog

## [1.0.0]

### Added
- `InputService`：全域唯一的輸入來源，每幀輪詢 Gamepad／Keyboard，送出裝置無關的
  按鍵、方向與右類比事件，並提供「持續按住」的狀態查詢。
- `InputLoop`：把 `InputService.Tick` 插進 PlayerLoop 的 `ScriptRunBehaviourUpdate`
  之前。沒有 MonoBehaviour、沒有 GameObject。
- `InputButton` / `InputDirection`：裝置無關的按鍵與方向代號。

### Notes
- 不使用 `PlayerInput` 與 `.inputactions`。那套的 action map 啟用停用是有狀態的，
  兩顆 `PlayerInput` 指向同一份資產時會互相關掉對方的 map；輪詢裝置沒有這個失效模式。
  代價是失去執行期改鍵，需要時在 `InputService.Read` 那一層加映射表即可。
- 必須放在 AOT 組件。HybridCLR 的熱更 DLL 不會被掃描
  `RuntimeInitializeOnLoadMethod`，放在熱更側不會被呼叫。
