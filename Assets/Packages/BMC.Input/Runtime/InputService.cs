using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BMC
{
    /// <summary>
    /// 全域唯一的輸入來源。每幀輪詢 Gamepad／Keyboard，轉成裝置無關的按鍵與方向事件。
    ///
    /// 【為什麼是 static 而不是 MonoBehaviour】輸入是行程等級的服務，不屬於任何場景。
    /// 掛在場景物件上會帶來兩個實際發生過的問題：換場景時新舊兩份的生命週期重疊，
    /// 以及每個場景都要在 Inspector 重接一次事件（漏接就靜默失效）。改成 static
    /// 之後這兩件事都不存在，驅動它的 Update 由 InputLoop 直接插進 PlayerLoop。
    ///
    /// 【為什麼不用 PlayerInput／.inputactions】那套需要一份資產，而資產的啟用停用
    /// 是有狀態的：兩顆 PlayerInput 指向同一份資產時會互相關掉對方的 action map。
    /// 輪詢裝置沒有狀態可以壞掉。代價是失去執行期改鍵（rebinding），需要的話在這一層
    /// 加一張映射表即可，上層不受影響。
    ///
    /// 這一層刻意不認識任何 UI 系統 —— 事件要送給誰、被什麼遮罩擋住，都由專案端決定。
    /// </summary>
    public static class InputService
    {
        // ==========================================
        // 可調參數
        // ==========================================

        /// <summary>類比搖桿的判定門檻，低於此值視為沒有推動</summary>
        public const float StickDeadzone = 0.5f;

        /// <summary>方向持續按住時，第一次重複觸發前的等待秒數</summary>
        public const float RepeatDelay = 0.4f;

        /// <summary>方向持續按住時的重複間隔秒數</summary>
        public const float RepeatInterval = 0.12f;

        // ==========================================
        // 開關
        // ==========================================

        /// <summary>關掉之後不再讀裝置、也不再送出任何事件</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// 由專案指派：回傳 true 時這一幀不讀裝置（例如玩家正在輸入框打字）。
        /// 暫停的當下會先把還按著的鍵送出 ButtonUp，避免上層卡在按住狀態。
        /// </summary>
        public static Func<bool> ShouldSuspend;

        // ==========================================
        // 事件
        // ==========================================

        /// <summary>按下的那一幀</summary>
        public static event Action<InputButton> ButtonDown;

        /// <summary>放開的那一幀</summary>
        public static event Action<InputButton> ButtonUp;

        /// <summary>方向改變的那一幀。長按不重複，適合遊戲操作</summary>
        public static event Action<InputDirection> DirectionDown;

        /// <summary>方向改變的那一幀，加上長按後的重複觸發。適合選單游標</summary>
        public static event Action<InputDirection> DirectionRepeat;

        /// <summary>右類比推到某一方向的那一幀</summary>
        public static event Action<InputDirection> StickRDown;

        /// <summary>右類比的原始值，超過死區才送</summary>
        public static event Action<Vector2> StickRMoved;

        /// <summary>每幀最後觸發。需要「持續按住」語意的呼叫端在這裡讀狀態</summary>
        public static event Action Ticked;

        // ==========================================
        // 狀態
        // ==========================================

        /// <summary>移動輸入的原始值（左類比＋十字鍵＋WASD＋方向鍵，未 snap）</summary>
        public static Vector2 MoveRaw { get; private set; }

        /// <summary>右類比的原始值</summary>
        public static Vector2 StickRRaw { get; private set; }

        /// <summary>目前的移動方向，已 snap 成四方向。沒有推動時是 None</summary>
        public static InputDirection Direction { get; private set; }

        public static bool IsPressed(InputButton button)
            => button != InputButton.None && pressed[(int)button];

        // ==========================================
        // 內部
        // ==========================================

        static readonly InputButton[] AllButtons =
        {
            InputButton.A, InputButton.B, InputButton.X, InputButton.Y,
            InputButton.ShoulderL, InputButton.ShoulderR,
            InputButton.TriggerL, InputButton.TriggerR,
            InputButton.StickPressL, InputButton.StickPressR,
            InputButton.Start, InputButton.Select,
        };

        static readonly bool[] pressed = new bool[(int)InputButton.Select + 1];

        static InputDirection lastDirection;
        static float directionHoldTime;
        static float nextRepeatTime;
        static InputDirection lastStickR;

        /// <summary>由 InputLoop 每幀呼叫。這是整個服務唯一的進入點。</summary>
        internal static void Tick()
        {
            if (!Enabled)
                return;

            if (ShouldSuspend != null && ShouldSuspend())
            {
                ResetState();
                return;
            }

            var pad = Gamepad.current;
            var keyboard = Keyboard.current;

            UpdateButtons(pad, keyboard);
            UpdateDirection(pad, keyboard);
            UpdateStickR(pad);

            Ticked?.Invoke();
        }

        /// <summary>
        /// 暫停或關閉時把狀態清乾淨。還按著的鍵要補送 ButtonUp ——
        /// 少了這一步，上層會停在「這顆鍵一直被按著」的狀態。
        /// </summary>
        internal static void ResetState()
        {
            for (int i = 0; i < AllButtons.Length; i++)
            {
                var button = AllButtons[i];
                if (!pressed[(int)button])
                    continue;

                pressed[(int)button] = false;
                ButtonUp?.Invoke(button);
            }

            MoveRaw = Vector2.zero;
            StickRRaw = Vector2.zero;
            Direction = InputDirection.None;
            lastDirection = InputDirection.None;
            lastStickR = InputDirection.None;
            directionHoldTime = 0f;
        }

        /// <summary>把所有訂閱者清掉。離開 Play Mode 時用，避免 static 事件留著上一輪的委派。</summary>
        internal static void ClearSubscribers()
        {
            ButtonDown = null;
            ButtonUp = null;
            DirectionDown = null;
            DirectionRepeat = null;
            StickRDown = null;
            StickRMoved = null;
            Ticked = null;
            ShouldSuspend = null;
        }

        // ------------------------------------------
        // 按鍵
        // ------------------------------------------

        static void UpdateButtons(Gamepad pad, Keyboard keyboard)
        {
            for (int i = 0; i < AllButtons.Length; i++)
            {
                var button = AllButtons[i];
                bool now = Read(button, pad, keyboard);
                int index = (int)button;

                if (now == pressed[index])
                    continue;

                pressed[index] = now;
                if (now)
                    ButtonDown?.Invoke(button);
                else
                    ButtonUp?.Invoke(button);
            }
        }

        /// <summary>
        /// 實體按鍵對應表。內容沿用原本 InputSystem_Actions.inputactions 的 Player map，
        /// 讓這一層換掉之後手感不變。
        /// 注意 Start／Select 原本就只綁手把，沒有鍵盤鍵，這裡照舊。
        /// </summary>
        static bool Read(InputButton button, Gamepad pad, Keyboard keyboard)
        {
            switch (button)
            {
                case InputButton.A: return Down(pad?.buttonSouth) || Down(keyboard?.jKey);
                case InputButton.B: return Down(pad?.buttonEast) || Down(keyboard?.kKey);
                case InputButton.X: return Down(pad?.buttonWest) || Down(keyboard?.uKey);
                case InputButton.Y: return Down(pad?.buttonNorth) || Down(keyboard?.iKey);

                case InputButton.ShoulderL: return Down(pad?.leftShoulder) || Down(keyboard?.nKey);
                case InputButton.ShoulderR: return Down(pad?.rightShoulder) || Down(keyboard?.mKey);
                case InputButton.TriggerL: return Down(pad?.leftTrigger);
                case InputButton.TriggerR: return Down(pad?.rightTrigger) || Down(keyboard?.lKey);

                case InputButton.StickPressL: return Down(pad?.leftStickButton) || Down(keyboard?.leftShiftKey);
                case InputButton.StickPressR: return Down(pad?.rightStickButton) || Down(keyboard?.rightShiftKey);

                case InputButton.Start: return Down(pad?.startButton);
                case InputButton.Select: return Down(pad?.selectButton);
            }
            return false;
        }

        static bool Down(ButtonControl control) => control != null && control.isPressed;

        // ------------------------------------------
        // 方向
        // ------------------------------------------

        static void UpdateDirection(Gamepad pad, Keyboard keyboard)
        {
            MoveRaw = ReadMove(pad, keyboard);

            var move = MoveRaw;

            // 只取絕對值較大的那一軸，避免斜推同時往兩個方向跑
            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                move.y = 0f;
            else if (Mathf.Abs(move.y) > Mathf.Abs(move.x))
                move.x = 0f;
            else
                move = Vector2.zero;

            var dir = ToDirection(move);
            Direction = dir;

            if (dir == InputDirection.None)
            {
                lastDirection = InputDirection.None;
                directionHoldTime = 0f;
                return;
            }

            if (dir != lastDirection)
            {
                // 換方向：立即觸發，並重新起算長按
                lastDirection = dir;
                directionHoldTime = 0f;
                nextRepeatTime = RepeatDelay;
                DirectionDown?.Invoke(dir);
                DirectionRepeat?.Invoke(dir);
                return;
            }

            directionHoldTime += Time.unscaledDeltaTime;
            if (directionHoldTime < nextRepeatTime)
                return;

            nextRepeatTime = directionHoldTime + RepeatInterval;
            DirectionRepeat?.Invoke(dir);
        }

        static Vector2 ReadMove(Gamepad pad, Keyboard keyboard)
        {
            var move = Vector2.zero;

            if (pad != null)
            {
                move += pad.leftStick.ReadValue();
                move += pad.dpad.ReadValue();
            }

            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
            }

            return move;
        }

        // ------------------------------------------
        // 右類比
        // ------------------------------------------

        static void UpdateStickR(Gamepad pad)
        {
            StickRRaw = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;

            if (StickRRaw.sqrMagnitude > StickDeadzone * StickDeadzone)
                StickRMoved?.Invoke(StickRRaw);

            var value = StickRRaw;
            if (Mathf.Abs(value.x) > Mathf.Abs(value.y))
                value.y = 0f;
            else if (Mathf.Abs(value.y) > Mathf.Abs(value.x))
                value.x = 0f;
            else
                value = Vector2.zero;

            var dir = ToDirection(value);
            if (dir == lastStickR)
                return;

            lastStickR = dir;
            if (dir != InputDirection.None)
                StickRDown?.Invoke(dir);
        }

        static InputDirection ToDirection(Vector2 value)
        {
            if (value.x >= StickDeadzone) return InputDirection.Right;
            if (value.x <= -StickDeadzone) return InputDirection.Left;
            if (value.y >= StickDeadzone) return InputDirection.Up;
            if (value.y <= -StickDeadzone) return InputDirection.Down;
            return InputDirection.None;
        }
    }
}
