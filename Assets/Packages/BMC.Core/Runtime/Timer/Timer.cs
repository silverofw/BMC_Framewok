using System;
using System.Collections;
using System.Collections.Generic;

namespace BMC.Core
{
    public class Timer
    {
        public int frame;
        public Action action;

        public int curFrame;
        public Timer(int frame, Action action) 
        { 
            this.frame = frame;
            this.action = action;
        }
    }
}
