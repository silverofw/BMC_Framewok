using BMC.Core;
using BMC.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;
namespace BMC.Patch.Core
{
    public class SceneMgr : Singleton<SceneMgr>
    {
        public void GotoScene(string sceneName, bool showLoading = true)
        {
            System.Action action = async () => { 
                UIMgr.Instance.ResetSceneUIRoot();
                await ResMgr.Instance.LoadSceneAsync(sceneName);
                await UIMgr.Instance.CreateSceneUIRoot(sceneName);            
            };
            if (showLoading)
            {
                LoadPanel.Show(action, null, true);
            }
            else
            {
                action.Invoke();
            }
        }
    }

}