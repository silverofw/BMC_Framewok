using System.Collections;
using UnityEngine;
using UniFramework.Machine;
using YooAsset;

public class FsmDownloadPackageFiles : IStateNode
{
    private StateMachine _machine;

    void IStateNode.OnCreate(StateMachine machine)
    {
        _machine = machine;
    }
    void IStateNode.OnEnter()
    {
        PatchEventDefine.PatchStepsChange.SendEventMessage("PATCH_STEP_DOWNLOADING");
        PatchWindow.behaviour.StartCoroutine(BeginDownload());
    }
    void IStateNode.OnUpdate()
    {
    }
    void IStateNode.OnExit()
    {
    }

    private IEnumerator BeginDownload()
    {
        var downloader = (ResourceDownloaderOperation)_machine.GetBlackboardValue("Downloader");
        downloader.DownloadError += PatchEventDefine.WebFileDownloadFailed.SendEventMessage;
        downloader.DownloadProgressChanged += PatchEventDefine.DownloadUpdate.SendEventMessage;
        downloader.StartDownload();
        yield return downloader;

        // 检测下载结果
        if (downloader.Status != EOperationStatus.Succeeded)
            yield break;

        _machine.ChangeState<FsmDownloadPackageOver>();
    }
}