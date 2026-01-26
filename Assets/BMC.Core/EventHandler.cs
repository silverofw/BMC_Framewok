using System;
using System.Collections.Generic;

namespace BMC.Core
{
    public class EventHandler
    {
        public Dictionary<int, Delegate> callbackDic = new Dictionary<int, Delegate>();

        public void Register(int id, Action callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister(int id, Action callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send(int id)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action)?.Invoke();
        }

        public void Register<T>(int id, Action<T> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action<T> callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister<T>(int id, Action<T> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action<T> callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send<T>(int id, T arg)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action<T>)?.Invoke(arg);
        }

        public void Register<T, T1>(int id, Action<T, T1> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action<T, T1> callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister<T, T1>(int id, Action<T, T1> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action<T, T1> callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send<T, T1>(int id, T arg, T1 arg1)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action<T, T1>)?.Invoke(arg, arg1);
        }

        public void Register<T, T1, T2>(int id, Action<T, T1, T2> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action<T, T1, T2> callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister<T, T1, T2>(int id, Action<T, T1, T2> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action<T, T1, T2> callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send<T, T1, T2>(int id, T arg, T1 arg1, T2 arg2)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action<T, T1, T2>)?.Invoke(arg, arg1, arg2);
        }

        public void Register<T, T1, T2, T3>(int id, Action<T, T1, T2, T3> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action<T, T1, T2, T3> callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister<T, T1, T2, T3>(int id, Action<T, T1, T2, T3> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action<T, T1, T2, T3> callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send<T, T1, T2, T3>(int id, T arg, T1 arg1, T2 arg2, T3 arg3)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action<T, T1, T2, T3>)?.Invoke(arg, arg1, arg2, arg3);
        }

        public void Register<T, T1, T2, T3, T4>(int id, Action<T, T1, T2, T3, T4> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del == null)
            {
                callbackDic[id] = callback;
            }
            else if (del is Action<T, T1, T2, T3, T4> callbacks)
            {
                callbackDic[id] = callbacks + callback;
            }
            else
            {
                //Game.Log("[Event] cannot register different types of callback in same event id!");
            }
        }

        public void UnRegister<T, T1, T2, T3, T4>(int id, Action<T, T1, T2, T3, T4> callback)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            if (del is Action<T, T1, T2, T3, T4> callbacks)
            {
                callbackDic[id] = callbacks - callback;
            }
        }

        public void Send<T, T1, T2, T3, T4>(int id, T arg, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            callbackDic.TryGetValue(id, out Delegate del);
            (del as Action<T, T1, T2, T3, T4>)?.Invoke(arg, arg1, arg2, arg3, arg4);
        }
    }
}
