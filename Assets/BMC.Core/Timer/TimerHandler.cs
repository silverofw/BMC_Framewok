using System;
using System.Collections;
using System.Collections.Generic;

namespace BMC.Core
{
    public class TimerHandler
    {
        List<Timer> timers = new List<Timer>();

        public void clear()
        { 
            timers.Clear();
        }

        public Timer newTimer(int frame, Action action)
        {
            if (frame > 0)
            {
                Timer timer = new Timer(frame, action);
                timers.Add(timer);
                return timer;
            }
            else
            {
                action?.Invoke();
                return null;
            }
        }

        public void Tick(int scale)
        {
            for (int i = timers.Count - 1; i >= 0; i--)
            {
                var timer = timers[i];
                timer.curFrame += scale;
                if (timer.curFrame >= timer.frame)
                {
                    timer.action();
                    timers.RemoveAt(i);
                }
            }
        }
    }
}
