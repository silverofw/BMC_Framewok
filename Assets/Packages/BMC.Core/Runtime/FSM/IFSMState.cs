namespace BMC.Core
{
    public interface IFSMState
    {
        GameFSM gameFSM { get; set; }
        void StateBegin(StateTransParam callback);
        void StateEnd();
        void StateTick(int scale);

        string Info();
    }
}