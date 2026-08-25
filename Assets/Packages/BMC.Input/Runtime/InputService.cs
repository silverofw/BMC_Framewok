using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BMC
{
    /// <summary>
    /// 全域唯一的輸入來源，建立在 Unity 6 的 Project-wide Actions 之上。
    ///
    /// 【為什麼不用 PlayerInput】Project-wide Actions（Edit &gt; Project Settings &gt;
    /// Input System Package）本來就是全域單例：InputSystem 在 InitializeInPlayer 時
    /// 自動 Enable，之後全程保持啟用，不屬於任何場景。PlayerInput 是「每個玩家一份」
    /// 的舊模型，兩顆指向同一份資產時會在 OnDisable 互相關掉對方的 action map ——
    /// Game 與 Gate 各掛一顆時實際踩過，症狀是回到大廳之後 A 鈕失效。
    /// 移除 PlayerInput、直接用 InputSystem.actions 就沒有這個問題。
    ///
    /// 【為什麼不自己輪詢裝置】鍵位、死區、composite（WASD 合成方向）、interaction
    /// 全都已經定義在 .inputactions 資產裡。自己讀 Gamepad.current／Keyboard.current
    /// 等於把那張表重寫一次，還會連帶失去執行期改鍵、Joystick／XR 綁定與
    /// Input Debugger。這裡只做兩件資產做不到的事：把方向 snap 成四方向，
    /// 以及選單游標要的長按重複。
    ///
    /// 這一層刻意不認識任何 UI 系統 —— 事件要送給誰由專案端決定。
    /// </summary>
    public static class InputService
    {
        // ==========================================
        // 可調參數
        // ==========================================

        /// <summary>方向 snap 成四方向的門檻。資產自己的死區處理更早、更細，這裡只管方向判定。</summary>
        public const float DirectionThreshold = 0.5f;

        /// <summary>方向持續按住時，第一次重複觸發前的等待秒數</summary>
        public const float RepeatDelay = 0.4f;

        /// <summary>方向持續按住時的重複間隔秒數</summary>
        public const float RepeatInterval = 0.12f;

        /// <summary>對應的 action map 名稱</summary>
        const string Map = "Player";

        // ==========================================
        // 開關
        // ==========================================

        /// <summary>關掉之後不再送出任何事件</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// 由專案指派：回傳 true 時暫停送出事件（例如玩家正在輸入框打字）。
        /// 暫停的當下會先把還按著的鍵補送 ButtonUp，避免上層卡在按住狀態。
        /// </summary>
        public static Func<bool> ShouldSuspend;

        // ==========================================
        // 事件
        // ==========================================

        public static event Action<InputButton> ButtonDown;
        public static event Action<InputButton> ButtonUp;

        /// <summary>方向改變的那一幀。長按不重複，適合遊戲操作</summary>
        public static event Action<InputDirection> DirectionDown;

        /// <summary>方向改變的那一幀，加上長按後的重複觸發。適合選單游標</summary>
        public static event Action<InputDirection> DirectionRepeat;

        /// <summary>右類比推到某一方向的那一幀</summary>
        public static event Action<InputDirection> StickRDown;

        /// <summary>右類比的原始值</summary>
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
        {
            var action = Find(button);
            return action != null && action.IsPressed();
        }

        // ==========================================
        // 綁定
        // ==========================================

        /// <summary>
        /// InputButton 對應到 .inputactions 裡的 action 名稱。
        /// 索引與 InputButton 的值一致，None 佔第 0 格。
        /// </summary>
        static readonly string[] ActionNames =
        {
            null,
            "A", "B", "X", "Y",
            "Shoulder_L", "Shoulder_R", "Trigger_L", "Trigger_R",
            "StickPress_L", "StickPress_R",
            "Start", "Select",
        };

        static InputAction[] buttons;
        static InputAction moveAction, dpadAction, stickRAction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bind()
        {
            var asset = InputSystem.actions;
            if (asset == null)
            {
                Debug.LogError("[BMC.Input] 沒有指定 Project-wide Actions"
                             + "（Edit > Project Settings > Input System Package），輸入不會運作");
                return;
            }

            buttons = new InputAction[ActionNames.Length];
            for (int i = 1; i < ActionNames.Length; i++)
            {
                var action = asset.FindAction($"{Map}/{ActionNames[i]}");
                if (action == null)
                {
                    Debug.LogWarning($"[BMC.Input] 資產裡找不到 {Map}/{ActionNames[i]}");
                    continue;
                }

                buttons[i] = action;
                var button = (InputButton)i;
                action.started += _ => Raise(ButtonDown, button);
                action.canceled += _ => Raise(ButtonUp, button);
            }

            moveAction = asset.FindAction($"{Map}/StickMove_L");
            dpadAction = asset.FindAction($"{Map}/DPad");
            stickRAction = asset.FindAction($"{Map}/StickMove_R");

            InputSystem.onAfterUpdate -= Tick;
            InputSystem.onAfterUpdate += Tick;

            Application.quitting -= Unbind;
            Application.quitting += Unbind;
        }

        /// <summary>
        /// 離開 Play Mode／關閉程式時解除。static 事件不會自己清空，
        /// 留著的話下一輪會拿到指向已銷毀物件的委派。
        /// </summary>
        static void Unbind()
        {
            Application.quitting -= Unbind;
            InputSystem.onAfterUpdate -= Tick;

            ButtonDown = null;
            ButtonUp = null;
            DirectionDown = null;
            DirectionRepeat = null;
            StickRDown = null;
            StickRMoved = null;
            Ticked = null;
            ShouldSuspend = null;
        }

        static InputAction Find(InputButton button)
        {
            int index = (int)button;
            return buttons != null && index > 0 && index < buttons.Length ? buttons[index] : null;
        }

        /// <summary>事件送出前的統一守門。暫停與關閉都在這裡擋掉。</summary>
        static void Raise<T>(Action<T> handler, T arg)
        {
            if (Enabled && !suspended)
                handler?.Invoke(arg);
        }

        // ==========================================
        // 每幀：方向 snap／長按重複／持續按住
        // ==========================================

        static bool suspended;
        static InputDirection lastDirection;
        static InputDirection lastStickR;
        static float directionHoldTime;
        static float nextRepeatTime;

        static void Tick()
        {
            if (!Enabled)
                return;

            UpdateSuspended();
            if (suspended)
                return;

            UpdateDirection();

            UpdateStickR();

            Ticked?.Invoke();
        }

        /// <summary>
        /// 進入暫停的那一幀，把還按著的鍵補送 ButtonUp。
        /// 少了這一步，上層會停在「這顆鍵一直被按著」的狀態。
        /// </summary>
        static void UpdateSuspended()
        {
            bool now = ShouldSuspend != null && ShouldSuspend();
            if (now == suspended)
                return;

            suspended = now;
            if (!now)
                return;

            for (int i = 1; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].IsPressed())
                    ButtonUp?.Invoke((InputButton)i);
            }

            MoveRaw = Vector2.zero;
            Direction = InputDirection.None;
            lastDirection = InputDirection.None;
            directionHoldTime = 0f;
        }

        static void UpdateDirection()
        {
            var move = Vector2.zero;
            if (moveAction != null)
                move += moveAction.ReadValue<Vector2>();
            if (dpadAction != null)
                move += dpadAction.ReadValue<Vector2>();

            MoveRaw = move;

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

        /// <summary>
        /// 【為什麼不用資產裡那四個 StickMove_R_* Button action】它們是四個獨立的按鍵，
        /// 斜推時 up 與 right 會同時成立，攝影機會收到兩個方向。這裡跟左類比一樣
        /// 只取絕對值較大的那一軸，斜推只出一個方向。
        /// </summary>
        static void UpdateStickR()
        {
            StickRRaw = stickRAction != null ? stickRAction.ReadValue<Vector2>() : Vector2.zero;

            if (StickRRaw.sqrMagnitude > DirectionThreshold * DirectionThreshold)
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
            if (value.x >= DirectionThreshold) return InputDirection.Right;
            if (value.x <= -DirectionThreshold) return InputDirection.Left;
            if (value.y >= DirectionThreshold) return InputDirection.Up;
            if (value.y <= -DirectionThreshold) return InputDirection.Down;
            return InputDirection.None;
        }
    }
}
