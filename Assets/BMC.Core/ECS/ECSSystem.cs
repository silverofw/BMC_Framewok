using System;

namespace BMC.Core
{
    public abstract class ECSSystem
    {
        public abstract void Tick(int scale);
    }
}
