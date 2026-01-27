namespace BMC.Core
{
    public class InitFSMState : IFSMState
    {
        public GameFSM gameFSM { get; set; }
        public virtual void StateBegin(StateTransParam callback) { }
        public virtual void StateEnd() { }
        public virtual void StateTick(int scale) { }

        public virtual string Info() { return ""; }
    }
}