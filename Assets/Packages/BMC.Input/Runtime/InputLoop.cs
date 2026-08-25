using System;
using UnityEngine;
using UnityEngine.LowLevel;

namespace BMC
{
    /// <summary>
    /// 把 InputService.Tick 插進 Unity 的 PlayerLoop。
    ///
    /// 【為什麼不開一個 DontDestroyOnLoad 的 MonoBehaviour】那需要一個 GameObject，
    /// 而 GameObject 會出現在 Hierarchy、會被場景工具掃到、會被誤刪，也會讓「輸入是
    /// 行程等級服務」這件事看起來像是場景的一部分。直接掛進 PlayerLoop 沒有這些副作用。
    ///
    /// 掛在 ScriptRunBehaviourUpdate 之前，所以同一幀的 MonoBehaviour.Update 讀到的
    /// 已經是這一幀的輸入狀態。
    ///
    /// 註：這支必須待在 AOT 組件裡。HybridCLR 的熱更 DLL 不會被 Unity 掃描
    /// RuntimeInitializeOnLoadMethod，放在熱更側不會被呼叫。
    /// </summary>
    internal static class InputLoop
    {
        /// <summary>只當作 PlayerLoop 節點的識別用，不會被實體化</summary>
        struct BMCInputUpdate { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();

            // 先移除再插入：關掉 Domain Reload 時 static 狀態會留到下一次進 Play Mode，
            // 這樣寫才不會重複掛上去
            TryRemove(ref root, typeof(BMCInputUpdate));

            var entry = new PlayerLoopSystem
            {
                type = typeof(BMCInputUpdate),
                updateDelegate = InputService.Tick,
            };

            if (!TryInsertBefore(ref root, typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), entry))
            {
                Debug.LogError("[BMC.Input] 在 PlayerLoop 找不到 ScriptRunBehaviourUpdate，輸入服務沒有啟動");
                return;
            }

            PlayerLoop.SetPlayerLoop(root);

            Application.quitting -= Uninstall;
            Application.quitting += Uninstall;
        }

        /// <summary>
        /// 離開 Play Mode／關閉程式時拆掉。
        /// static 事件不會自己清空，留著的話下一輪會拿到指向已銷毀物件的委派。
        /// </summary>
        static void Uninstall()
        {
            Application.quitting -= Uninstall;

            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (TryRemove(ref root, typeof(BMCInputUpdate)))
                PlayerLoop.SetPlayerLoop(root);

            InputService.ResetState();
            InputService.ClearSubscribers();
        }

        // ==========================================
        // PlayerLoop 樹的增刪。節點是 struct，改子樹之後要寫回父節點。
        // ==========================================

        static bool TryInsertBefore(ref PlayerLoopSystem node, Type anchor, PlayerLoopSystem entry)
        {
            var subs = node.subSystemList;
            if (subs == null)
                return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == anchor)
                {
                    var list = new PlayerLoopSystem[subs.Length + 1];
                    Array.Copy(subs, 0, list, 0, i);
                    list[i] = entry;
                    Array.Copy(subs, i, list, i + 1, subs.Length - i);
                    node.subSystemList = list;
                    return true;
                }

                var child = subs[i];
                if (!TryInsertBefore(ref child, anchor, entry))
                    continue;

                subs[i] = child;
                node.subSystemList = subs;
                return true;
            }

            return false;
        }

        static bool TryRemove(ref PlayerLoopSystem node, Type target)
        {
            var subs = node.subSystemList;
            if (subs == null)
                return false;

            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type == target)
                {
                    var list = new PlayerLoopSystem[subs.Length - 1];
                    Array.Copy(subs, 0, list, 0, i);
                    Array.Copy(subs, i + 1, list, i, subs.Length - i - 1);
                    node.subSystemList = list;
                    return true;
                }

                var child = subs[i];
                if (!TryRemove(ref child, target))
                    continue;

                subs[i] = child;
                node.subSystemList = subs;
                return true;
            }

            return false;
        }
    }
}
