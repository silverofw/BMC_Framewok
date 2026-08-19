using Cysharp.Threading.Tasks;
using UnityEngine;
using UniFramework.Machine;
using UniFramework.Event;
using YooAsset;

/// <summary>
/// 單一資源包的補丁更新流程。
/// YooAsset v3 移除了 GameAsyncOperation（且沒有公開 API 可以啟動自訂 operation），
/// 因此改為官方 v3 範例的作法：自行持有狀態機並每幀驅動 Update。
/// </summary>
public class PatchOperation
{
    private enum ESteps
    {
        None,
        Update,
        Done,
    }

    private readonly EventGroup _eventGroup = new EventGroup();
    private readonly StateMachine _machine;
    private readonly string _packageName;
    private ESteps _steps = ESteps.None;

    public PatchOperation(string packageName, EPlayMode playMode)
    {
        _packageName = packageName;

        // 注册监听事件
        _eventGroup.AddListener<UserEventDefine.UserTryInitialize>(OnHandleEventMessage);
        _eventGroup.AddListener<UserEventDefine.UserBeginDownloadWebFiles>(OnHandleEventMessage);
        _eventGroup.AddListener<UserEventDefine.UserTryRequestPackageVersion>(OnHandleEventMessage);
        _eventGroup.AddListener<UserEventDefine.UserTryUpdatePackageManifest>(OnHandleEventMessage);
        _eventGroup.AddListener<UserEventDefine.UserTryDownloadWebFiles>(OnHandleEventMessage);

        // 创建状态机
        _machine = new StateMachine(this);
        _machine.AddNode<FsmInitializePackage>();
        _machine.AddNode<FsmRequestPackageVersion>();
        _machine.AddNode<FsmUpdatePackageManifest>();
        _machine.AddNode<FsmCreateDownloader>();
        _machine.AddNode<FsmDownloadPackageFiles>();
        _machine.AddNode<FsmDownloadPackageOver>();
        _machine.AddNode<FsmClearCacheBundle>();
        _machine.AddNode<FsmStartGame>();

        _machine.SetBlackboardValue("PackageName", packageName);
        _machine.SetBlackboardValue("PlayMode", playMode);
    }

    /// <summary>
    /// 執行補丁流程，直到 FsmStartGame 呼叫 SetFinish 為止。
    /// 更新失敗時流程會停在對應狀態等待使用者重試，因此這裡不會結束。
    /// </summary>
    public async UniTask ExecuteAsync()
    {
        if (_steps != ESteps.None)
            return;

        _steps = ESteps.Update;
        _machine.Run<FsmInitializePackage>();

        while (_steps == ESteps.Update)
        {
            _machine.Update();
            await UniTask.Yield(PlayerLoopTiming.Update);
        }
    }

    public void SetFinish()
    {
        _steps = ESteps.Done;
        _eventGroup.RemoveAllListener();
        Debug.Log($"Package {_packageName} patch done !");
    }

    /// <summary>
    /// 接收事件
    /// </summary>
    private void OnHandleEventMessage(IEventMessage message)
    {
        if (message is UserEventDefine.UserTryInitialize)
        {
            _machine.ChangeState<FsmInitializePackage>();
        }
        else if (message is UserEventDefine.UserBeginDownloadWebFiles)
        {
            _machine.ChangeState<FsmDownloadPackageFiles>();
        }
        else if (message is UserEventDefine.UserTryRequestPackageVersion)
        {
            _machine.ChangeState<FsmRequestPackageVersion>();
        }
        else if (message is UserEventDefine.UserTryUpdatePackageManifest)
        {
            _machine.ChangeState<FsmUpdatePackageManifest>();
        }
        else if (message is UserEventDefine.UserTryDownloadWebFiles)
        {
            _machine.ChangeState<FsmCreateDownloader>();
        }
        else
        {
            throw new System.NotImplementedException($"{message.GetType()}");
        }
    }
}
