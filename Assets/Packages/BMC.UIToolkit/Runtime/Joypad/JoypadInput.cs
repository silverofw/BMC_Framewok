using UnityEngine;
using UnityEngine.InputSystem;

namespace BMC.UIToolkit
{
    /// <summary>
    /// BMC.UIToolkit 的預設手把／鍵盤輸入來源。
    ///
    /// 讀 InputSystem 的 Gamepad 與 Keyboard，轉成 UIEvent.INPUT_* 送進
    /// UITMgr.Instance.eventHandler，由 UITMgr 轉發給最上層的 JoypadPanel。
    /// 事件名稱與語意刻意與 uGUI 版 BMC.UI 相同。
    ///
    /// 獨立成 BMC.UIToolkit.Joypad 組件的原因：核心的 BMC.UIToolkit 不相依
    /// InputSystem。專案已有自己的輸入層時，把 Enabled 設成 false（或整個
    /// Runtime/Joypad 資料夾刪掉），改由既有輸入層送出同樣的事件即可。
    ///
    /// 註：UI Toolkit 內建的 Navigation 事件（InputSystemProvider 的 UI/Navigate
    /// 那一套）走的是焦點導覽，與這裡的格線游標是兩套機制。JoypadItem 一律
    /// focusable = false，就是為了不讓兩者同時搬動選取。
    /// </summary>
    [DisallowMultipleComponent]
    public class JoypadInput : MonoBehaviour
    {
        /// <summary>關掉之後就不再送出任何輸入事件，交由專案自己的輸入層負責</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>類比搖桿的判定門檻，低於此值視為沒有推動</summary>
        private const float STICK_DEADZONE = 0.5f;

        /// <summary>方向持續按住時，第一次重複觸發前的等待秒數</summary>
        private const float REPEAT_DELAY = 0.4f;

        /// <summary>方向持續按住時的重複間隔秒數</summary>
        private const float REPEAT_INTERVAL = 0.12f;

        private static JoypadInput instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (instance != null)
                return;

            var go = new GameObject("[BMC.UIToolkit.JoypadInput]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<JoypadInput>();
        }

        /// <summary>上一幀的方向輸入，用來做邊緣偵測</summary>
        private Vector2 lastMove;

        /// <summary>方向輸入保持在同一格的累計秒數</summary>
        private float moveHoldTime;

        /// <summary>下一次允許重複觸發的時間點（相對於 moveHoldTime）</summary>
        private float nextRepeatTime;

        private void Update()
        {
            if (!Enabled)
                return;

            // 沒有面板在等輸入就不必讀裝置：專案只用 uGUI 那一套時，這裡每幀直接跳過
            if (!UITMgr.Instance.HasJoypadPanel)
                return;

            // 玩家正在輸入框打字時，方向鍵與 Enter 屬於文字編輯，不轉成手把操作
            if (UITMgr.Instance.IsTextInputFocused())
            {
                lastMove = Vector2.zero;
                return;
            }

            UpdateMove();
            UpdateButtons();
        }

        // -----------------------------------------------------------------------
        // 方向：十字鍵、左類比、鍵盤方向鍵合併成一個向量，
        // 按下當下先送一次，持續按住則延遲後開始重複。
        // -----------------------------------------------------------------------
        private void UpdateMove()
        {
            var move = ReadMove();

            // 只取絕對值較大的那一軸，避免斜推同時往兩個方向跑
            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                move.y = 0f;
            else if (Mathf.Abs(move.y) > Mathf.Abs(move.x))
                move.x = 0f;

            var dir = new Vector2(Snap(move.x), Snap(move.y));

            if (dir == Vector2.zero)
            {
                lastMove = Vector2.zero;
                moveHoldTime = 0f;
                return;
            }

            if (dir != lastMove)
            {
                // 換方向：立即觸發，並重新起算長按
                lastMove = dir;
                moveHoldTime = 0f;
                nextRepeatTime = REPEAT_DELAY;
                SendMove(dir);
                return;
            }

            moveHoldTime += Time.unscaledDeltaTime;
            if (moveHoldTime < nextRepeatTime)
                return;

            nextRepeatTime = moveHoldTime + REPEAT_INTERVAL;
            SendMove(dir);
        }

        private static float Snap(float value)
        {
            if (value >= STICK_DEADZONE) return 1f;
            if (value <= -STICK_DEADZONE) return -1f;
            return 0f;
        }

        private static Vector2 ReadMove()
        {
            var move = Vector2.zero;

            var pad = Gamepad.current;
            if (pad != null)
            {
                move += pad.dpad.ReadValue();
                move += pad.leftStick.ReadValue();
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.downArrowKey.isPressed) move.y -= 1f;
            }

            return move;
        }

        private static void SendMove(Vector2 dir)
        {
            if (dir.x > 0f) Send(UIEvent.INPUT_RIGHT);
            else if (dir.x < 0f) Send(UIEvent.INPUT_LEFT);
            else if (dir.y > 0f) Send(UIEvent.INPUT_UP);
            else if (dir.y < 0f) Send(UIEvent.INPUT_DOWN);
        }

        // -----------------------------------------------------------------------
        // 按鍵：全部只在按下的那一幀觸發，不做長按重複
        // -----------------------------------------------------------------------
        private void UpdateButtons()
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) Send(UIEvent.INPUT_A);
                if (pad.buttonEast.wasPressedThisFrame) Send(UIEvent.INPUT_B);
                if (pad.buttonWest.wasPressedThisFrame) Send(UIEvent.INPUT_X);
                if (pad.buttonNorth.wasPressedThisFrame) Send(UIEvent.INPUT_Y);

                if (pad.leftShoulder.wasPressedThisFrame) Send(UIEvent.INPUT_SHOULDER_L);
                if (pad.rightShoulder.wasPressedThisFrame) Send(UIEvent.INPUT_SHOULDER_R);
                if (pad.leftTrigger.wasPressedThisFrame) Send(UIEvent.INPUT_TRIGGER_L);
                if (pad.rightTrigger.wasPressedThisFrame) Send(UIEvent.INPUT_TRIGGER_R);

                if (pad.startButton.wasPressedThisFrame) Send(UIEvent.INPUT_START);
                if (pad.selectButton.wasPressedThisFrame) Send(UIEvent.INPUT_SELECT);

                var stickR = pad.rightStick.ReadValue();
                if (stickR.sqrMagnitude > STICK_DEADZONE * STICK_DEADZONE)
                    UITMgr.Instance.eventHandler.Send((int)UIEvent.INPUT_STICK_R, stickR);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 鍵盤對應刻意避開字母鍵：輸入框有焦點時雖然已經擋掉，
            // 但字母鍵當快捷鍵在中文輸入等情境仍容易誤觸。
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                Send(UIEvent.INPUT_A);

            if (keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
                Send(UIEvent.INPUT_B);

            if (keyboard.pageUpKey.wasPressedThisFrame) Send(UIEvent.INPUT_SHOULDER_L);
            if (keyboard.pageDownKey.wasPressedThisFrame) Send(UIEvent.INPUT_SHOULDER_R);
        }

        private static void Send(UIEvent id) => UITMgr.Instance.eventHandler.Send((int)id);
    }
}
