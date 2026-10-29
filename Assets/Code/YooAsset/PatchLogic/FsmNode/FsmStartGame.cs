using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniFramework.Machine;

internal class FsmStartGame : IStateNode
{
    private PatchOperation _owner;

    void IStateNode.OnCreate(StateMachine machine)
    {
        _owner = machine.Owner as PatchOperation;
    }
    void IStateNode.OnEnter()
    {
        PatchEventDefine.PatchStepsChange.SendEventMessage("PATCH_STEP_DONE");
        _owner.SetFinish();
    }
    void IStateNode.OnUpdate()
    {
    }
    void IStateNode.OnExit()
    {
    }
}