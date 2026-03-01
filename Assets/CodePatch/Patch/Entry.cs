using BMC.Patch.Core;
using UnityEngine;

public class Entry : MonoBehaviour
{
    private void Start()
    {
        // 初始化 Debug 註冊器，確保在遊戲啟動時就註冊好 Debug 功能
        CommonDebugRegister.Init();
    }
}
