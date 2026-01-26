using System;

namespace BMC.Core
{
    [Serializable]
    public class ValueInst
    {
        public int index; 
        public long value;
        public ValueInst(int index, long value)
        {
            this.index = index;
            this.value = value;
        }
    }
}
