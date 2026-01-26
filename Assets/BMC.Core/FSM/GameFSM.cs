using System;
using System.Collections;
using System.Collections.Generic;

namespace BMC.Core
{
    public class GameFSM
    {
        Dictionary<Type, IFSMState> fsmStateDic = new Dictionary<Type, IFSMState>();
        public IFSMState CurFsmState = null;

        Dictionary<int, int> datas = new Dictionary<int, int>();

        public GameFSM()
        {
            TransTo<InitFSMState>();
        }

        protected void TransTo(Type t, StateTransParam callback = null)
        {
            /* 太慢先關閉判定
            if (!typeof(IFSMState).IsAssignableFrom(t))
            {
                Log.ERROR($"ERROR state {t}");
                return;
            }*/

            if (CurFsmState != null && CurFsmState.GetType() == t)
            {
                Log.Info($"[FSM] It is already in {t} now!");
                return;
            }
            if (!fsmStateDic.ContainsKey(t))
            {
                //Log.SEND($"[FSM] {typeof(T)} is not in dic!");
                IFSMState stateInstance = (IFSMState)Activator.CreateInstance(t);
                stateInstance.gameFSM = this;
                fsmStateDic.Add(t, stateInstance);
            }

            if (CurFsmState != null)
            {
                CurFsmState.StateEnd();
            }

            CurFsmState = fsmStateDic[t];
            CurFsmState.StateBegin(callback);
        }

        public void TransTo<T>(StateTransParam callback = null) where T : IFSMState
        {
            if (CurFsmState != null && CurFsmState.GetType() == typeof(T))
            {
                Log.Info($"[FSM] It is already in {typeof(T)} now!");
                return;
            }
            if (!fsmStateDic.ContainsKey(typeof(T)))
            {
                //Log.SEND($"[FSM] {typeof(T)} is not in dic!");
                T stateInstance = (T)Activator.CreateInstance(typeof(T));
                stateInstance.gameFSM = this;
                fsmStateDic.Add(typeof(T), stateInstance);
            }

            if (CurFsmState != null)
            {
                CurFsmState.StateEnd();
            }

            CurFsmState = fsmStateDic[typeof(T)];
            CurFsmState.StateBegin(callback);
        }

        public void Tick(int scale)
        {
            CurFsmState.StateTick(scale);
        }

        public void Depose()
        {
            if (CurFsmState != null)
            {
                CurFsmState.StateEnd();
            }
        }


        public void SetData(int index, int value)
        {
            if (datas.ContainsKey(index))
            {
                datas[index] = value;
            }
            else
            {
                datas.Add(index, value);
            }
        }
        public int GetData(int index)
        {
            if (datas.TryGetValue(index, out var d))
            {
                return d;
            }
            else
            {
                return -1;
            }
        }
    }

    public class StateTransParam
    {
        public int delay;
        public List<int> ints = new List<int>();
        public System.Action BeginCallback;
        public System.Action FinishCallback;
    }
}