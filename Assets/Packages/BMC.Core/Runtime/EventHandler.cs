using System;
using System.Collections.Generic;

namespace BMC.Core
{
    public class EventHandler
    {
        public readonly Dictionary<int, Delegate> callbackDic = new Dictionary<int, Delegate>();

        // =======================================================
        // Enum 擴充支援 (恢復成 System.Enum，確保語法簡潔)
        // =======================================================
        public void Register(Enum id, Action callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister(Enum id, Action callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send(Enum id) => Send(Convert.ToInt32(id));

        public void Register<T>(Enum id, Action<T> callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister<T>(Enum id, Action<T> callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send<T>(Enum id, T arg) => Send(Convert.ToInt32(id), arg);

        public void Register<T, T1>(Enum id, Action<T, T1> callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister<T, T1>(Enum id, Action<T, T1> callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send<T, T1>(Enum id, T arg, T1 arg1) => Send(Convert.ToInt32(id), arg, arg1);

        public void Register<T, T1, T2>(Enum id, Action<T, T1, T2> callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister<T, T1, T2>(Enum id, Action<T, T1, T2> callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send<T, T1, T2>(Enum id, T arg, T1 arg1, T2 arg2) => Send(Convert.ToInt32(id), arg, arg1, arg2);

        public void Register<T, T1, T2, T3>(Enum id, Action<T, T1, T2, T3> callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister<T, T1, T2, T3>(Enum id, Action<T, T1, T2, T3> callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send<T, T1, T2, T3>(Enum id, T arg, T1 arg1, T2 arg2, T3 arg3) => Send(Convert.ToInt32(id), arg, arg1, arg2, arg3);

        public void Register<T, T1, T2, T3, T4>(Enum id, Action<T, T1, T2, T3, T4> callback) => Register(Convert.ToInt32(id), callback);
        public void UnRegister<T, T1, T2, T3, T4>(Enum id, Action<T, T1, T2, T3, T4> callback) => UnRegister(Convert.ToInt32(id), callback);
        public void Send<T, T1, T2, T3, T4>(Enum id, T arg, T1 arg1, T2 arg2, T3 arg3, T4 arg4) => Send(Convert.ToInt32(id), arg, arg1, arg2, arg3, arg4);


        // =======================================================
        // 核心實作 (保留 Delegate.Combine / Remove 的簡化與安全防護)
        // =======================================================

        private void AddDelegate(int id, Delegate callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            try
            {
                callbackDic[id] = Delegate.Combine(del, callback);
            }
            catch (ArgumentException)
            {
                // Game.Log($"[Event] Event ID {id} type mismatch!");
            }
        }

        private void RemoveDelegate(int id, Delegate callback)
        {
            if (callbackDic.TryGetValue(id, out Delegate del))
            {
                Delegate currentDel = Delegate.Remove(del, callback);
                if (currentDel == null)
                    callbackDic.Remove(id);
                else
                    callbackDic[id] = currentDel;
            }
        }

        // 0 Args
        public void Register(int id, Action callback) => AddDelegate(id, callback);
        public void UnRegister(int id, Action callback) => RemoveDelegate(id, callback);
        public void Send(int id) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action)?.Invoke(); }

        // 1 Arg
        public void Register<T>(int id, Action<T> callback) => AddDelegate(id, callback);
        public void UnRegister<T>(int id, Action<T> callback) => RemoveDelegate(id, callback);
        public void Send<T>(int id, T arg) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action<T>)?.Invoke(arg); }

        // 2 Args
        public void Register<T, T1>(int id, Action<T, T1> callback) => AddDelegate(id, callback);
        public void UnRegister<T, T1>(int id, Action<T, T1> callback) => RemoveDelegate(id, callback);
        public void Send<T, T1>(int id, T arg, T1 arg1) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action<T, T1>)?.Invoke(arg, arg1); }

        // 3 Args
        public void Register<T, T1, T2>(int id, Action<T, T1, T2> callback) => AddDelegate(id, callback);
        public void UnRegister<T, T1, T2>(int id, Action<T, T1, T2> callback) => RemoveDelegate(id, callback);
        public void Send<T, T1, T2>(int id, T arg, T1 arg1, T2 arg2) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action<T, T1, T2>)?.Invoke(arg, arg1, arg2); }

        // 4 Args
        public void Register<T, T1, T2, T3>(int id, Action<T, T1, T2, T3> callback) => AddDelegate(id, callback);
        public void UnRegister<T, T1, T2, T3>(int id, Action<T, T1, T2, T3> callback) => RemoveDelegate(id, callback);
        public void Send<T, T1, T2, T3>(int id, T arg, T1 arg1, T2 arg2, T3 arg3) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action<T, T1, T2, T3>)?.Invoke(arg, arg1, arg2, arg3); }

        // 5 Args
        public void Register<T, T1, T2, T3, T4>(int id, Action<T, T1, T2, T3, T4> callback) => AddDelegate(id, callback);
        public void UnRegister<T, T1, T2, T3, T4>(int id, Action<T, T1, T2, T3, T4> callback) => RemoveDelegate(id, callback);
        public void Send<T, T1, T2, T3, T4>(int id, T arg, T1 arg1, T2 arg2, T3 arg3, T4 arg4) { if (callbackDic.TryGetValue(id, out Delegate del)) (del as Action<T, T1, T2, T3, T4>)?.Invoke(arg, arg1, arg2, arg3, arg4); }
    }
}