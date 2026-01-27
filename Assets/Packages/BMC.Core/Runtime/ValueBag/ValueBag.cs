using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace BMC.Core
{
    public class ValueBag
    {
        public Dictionary<int, ValueInst> Dic = new();
        public void add(int index, long value)
        {
            if (Dic.TryGetValue(index, out var inst))
            {
                inst.value += value;
            }
            else
            {
                Dic.Add(index, new ValueInst(index, value));
            }
        }
        public List<ValueInst> getValues()
        {
            return Dic.Values.ToList();
        }

        public void loadValues(List<ValueInst> values)
        {
            Dic.Clear();
            foreach (var inst in values)
            {
                Dic.Add(inst.index, inst);
            }
        }

        public bool getBool(int index)
        {
            if (Dic.TryGetValue(index, out var inst))
            {
                return inst.value == 1;
            }
            else
            {
                return false;
            }
        }
        /// <summary>
        /// 沒資料給0
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public long getValue(int index)
        {
            if (Dic.TryGetValue(index, out var inst))
            {
                return inst.value;
            }
            else
            {
                return 0;
            }
        }

        public long getV(ValueType valueType)
        {
            return getValue((int)valueType);
        }

        public void setValue(int index, long value)
        {
            if(value == 0)
            {
                Dic.Remove(index);
                return;
            }

            if (Dic.TryGetValue(index, out var inst))
            {
                inst.value = value;
            }
            else
            {
                add(index, value);
            }
        }

        public void setValue(ValueType valueType, long value)
        {
            setValue((int)valueType, value);
        }
    }
}