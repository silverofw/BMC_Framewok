using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.UIToolkit.Samples.BasicUIFlow
{
    /// <summary>
    /// 範例場景的啟動腳本：不依賴 ResMgr／YooAsset 資源系統，
    /// 直接建立 UIDocument + PanelSettings，示範 UITMgr 最基礎的顯示流程。
    /// 掛在場景中任一 GameObject 上即可執行（場景已預先掛好）。
    /// </summary>
    public class UIToolkitSampleBootstrap : MonoBehaviour
    {
        private void Start()
        {
            var document = GetComponent<UIDocument>();
            if (document == null)
                document = gameObject.AddComponent<UIDocument>();

            if (document.panelSettings == null)
                document.panelSettings = CreateDefaultPanelSettings();

            UITMgr.Instance.UseRootDocument(document);

            var panelAsset = Resources.Load<VisualTreeAsset>("SamplePanel");
            if (panelAsset == null)
            {
                Debug.LogError("[UIToolkitSampleBootstrap] 找不到 Resources/SamplePanel.uxml");
                return;
            }

            UITMgr.Instance.ShowPanel<SamplePanel>(panelAsset, UILayer.UI_1);
        }

        private static PanelSettings CreateDefaultPanelSettings()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);

            // Unity 沒有提供可跨版本穩定取得的內建預設主題，這裡改用範例自帶的空白主題，
            // 只為了滿足 themeStyleSheet 不可為 null 的檢查；實際樣式由 SamplePanel.uss 負責。
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("SampleTheme");
            return settings;
        }
    }
}
