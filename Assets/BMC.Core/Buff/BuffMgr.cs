using BMC.Core;
using System.Collections.Generic;

namespace BMC.Core
{
    public class BuffMgr : Singleton<BuffMgr>
    {
        Dictionary<int, Buff> dic = new();
        public void InitDic(Dictionary<int, Buff> dic)
        {
            this.dic = dic;
        }

        public Buff GetBuff(int id)
        { 
            if(dic.TryGetValue (id, out var buff))
                return buff;
            return null;
        }
    }
}