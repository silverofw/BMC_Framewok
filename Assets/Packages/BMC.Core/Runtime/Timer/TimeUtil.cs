using System;

namespace BMC.Core
{

    public class TimeUtil
    {
        public static int TICK_TO_SEC = 10000000;
        public static DateTime Now => DateTime.Now;
        public static long Ticks => DateTime.Now.Ticks;

        public static DateTime TickToDateTime(long ticks) => new DateTime(ticks);
        public static long AddSec(long ticks, long seconds) => ticks + (seconds * TICK_TO_SEC);
        public static long DeltaSec(long ticks, long ticks2) => (ticks - ticks2) / TICK_TO_SEC;
    }
}
