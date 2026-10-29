using BMC.UIToolkit;
using UniFramework.Event;
using UnityEngine;

/// <summary>
/// 熱更新畫面：把 Patch 流程的事件轉成 PatchView（BMC.UIToolkit 套件）的畫面更新。
/// 版面與內建多語表都在套件裡。要換美術就寫一份換膚 USS 拖進 PatchView 的 skin 欄位，
/// 要換標題就在 Awake 用 PatchLocal.Set("PATCH_TITLE", …) 蓋掉預設的「讀取中 ...」。
/// </summary>
[RequireComponent(typeof(PatchView))]
public class PatchWindow : MonoBehaviour
{
    public static string cdnUrl = "https://cdn.pages.dev/";
    public static MonoBehaviour behaviour;

    private readonly EventGroup _eventGroup = new EventGroup();

    private PatchView _view;

    void Awake()
    {
        behaviour = this;
        _view = GetComponent<PatchView>();

        _eventGroup.AddListener<PatchEventDefine.InitializeFailed>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.PatchStepsChange>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.FoundUpdateFiles>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.DownloadUpdate>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.PackageVersionRequestFailed>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.PackageManifestUpdateFailed>(OnHandleEventMessage);
        _eventGroup.AddListener<PatchEventDefine.WebFileDownloadFailed>(OnHandleEventMessage);
    }
    void Start()
    {
        _view.SetTips(PatchLocal.Get("PATCH_STEP_INIT"));
    }
    void OnDestroy()
    {
        _eventGroup.RemoveAllListener();
    }

    /// <summary>
    /// 接收事件
    /// </summary>
    private void OnHandleEventMessage(IEventMessage message)
    {
        if (message is PatchEventDefine.InitializeFailed)
        {
            _view.ShowMessageBox(PatchLocal.Get("PATCH_INIT_FAILED"), PatchLocal.Get("PATCH_BTN_RETRY"),
                () => UserEventDefine.UserTryInitialize.SendEventMessage());
        }
        else if (message is PatchEventDefine.PatchStepsChange)
        {
            var msg = message as PatchEventDefine.PatchStepsChange;
            _view.SetTips(PatchLocal.Get(msg.Tips));
            UnityEngine.Debug.Log(msg.Tips);
        }
        else if (message is PatchEventDefine.FoundUpdateFiles)
        {
            var msg = message as PatchEventDefine.FoundUpdateFiles;
            float sizeMB = msg.TotalSizeBytes / 1048576f;
            sizeMB = Mathf.Clamp(sizeMB, 0.1f, float.MaxValue);
            _view.ShowMessageBox(PatchLocal.Format("PATCH_FOUND_UPDATE", msg.TotalCount, sizeMB.ToString("f1")), PatchLocal.Get("PATCH_BTN_DOWNLOAD"),
                () => UserEventDefine.UserBeginDownloadWebFiles.SendEventMessage());
        }
        else if (message is PatchEventDefine.DownloadUpdate)
        {
            var msg = message as PatchEventDefine.DownloadUpdate;
            _view.SetProgress(msg.TotalDownloadCount > 0 ? (float)msg.CurrentDownloadCount / msg.TotalDownloadCount : 0f);
            string currentSizeMB = (msg.CurrentDownloadSizeBytes / 1048576f).ToString("f1");
            string totalSizeMB = (msg.TotalDownloadSizeBytes / 1048576f).ToString("f1");
            _view.SetTips(PatchLocal.Format("PATCH_DOWNLOAD_PROGRESS", msg.CurrentDownloadCount, msg.TotalDownloadCount, currentSizeMB, totalSizeMB));
        }
        else if (message is PatchEventDefine.PackageVersionRequestFailed)
        {
            _view.ShowMessageBox(PatchLocal.Get("PATCH_VERSION_REQUEST_FAILED"), PatchLocal.Get("PATCH_BTN_RETRY"),
                () => UserEventDefine.UserTryRequestPackageVersion.SendEventMessage());
        }
        else if (message is PatchEventDefine.PackageManifestUpdateFailed)
        {
            _view.ShowMessageBox(PatchLocal.Get("PATCH_MANIFEST_FAILED"), PatchLocal.Get("PATCH_BTN_RETRY"),
                () => UserEventDefine.UserTryUpdatePackageManifest.SendEventMessage());
        }
        else if (message is PatchEventDefine.WebFileDownloadFailed)
        {
            var msg = message as PatchEventDefine.WebFileDownloadFailed;
            _view.ShowMessageBox(PatchLocal.Format("PATCH_DOWNLOAD_FAILED", msg.FileName), PatchLocal.Get("PATCH_BTN_RETRY"),
                () => UserEventDefine.UserTryDownloadWebFiles.SendEventMessage());
        }
        else
        {
            throw new System.NotImplementedException($"{message.GetType()}");
        }
    }

    /// <summary>
    /// 显示对话框（按鈕顯示「確認」）。content 是已經翻好的文字
    /// </summary>
    public void ShowMessageBox(string content, System.Action ok)
    {
        _view.ShowMessageBox(content, ok);
    }

    public void UpdatePatchInfo()
    {
        _view.SetProgress(1f);
        var package = YooAsset.YooAssets.GetPackage("DefaultPackage");
        _view.SetVersion(PatchLocal.Format("PATCH_VERSION_INFO", Application.version, package.GetPackageVersion()));
    }
}
