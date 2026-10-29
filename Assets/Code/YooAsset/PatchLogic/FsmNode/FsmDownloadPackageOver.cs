using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniFramework.Machine;

internal class FsmDownloadPackageOver : IStateNode
{
    private StateMachine _machine;

    void IStateNode.OnCreate(StateMachine machine)
    {
        _machine = machine;
    }
    void IStateNode.OnEnter()
    {
        PatchEventDefine.PatchStepsChange.SendEventMessage("PATCH_STEP_DOWNLOAD_OVER");
        _machine.ChangeState<FsmClearCacheBundle>();
    }
    void IStateNode.OnUpdate()
    {
    }
    void IStateNode.OnExit()
    {
    }
}