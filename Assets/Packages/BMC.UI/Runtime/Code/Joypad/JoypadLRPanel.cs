using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BMC.UI
{
    public class JoypadLRPanel : JoypadPanel
    {
        [SerializeField] protected List<JoypadItem> joypadPageItems = new List<JoypadItem>();
        [SerializeField] private GameObject item;
        [SerializeField] private JoypadItem pageItem;

        protected List<(string, List<Action<GameObject, int>>)> actionDic = new();

        protected int selectedPageIndex { get; private set; } = 0;
        protected int ItemIndex => (selectedPageIndex * gridWidth * gridWidth) + selectedItemIndex;
        private List<GameObject> itemObjs = new();
        private List<JoypadItem> pages = new();

        protected override void Show()
        {
            if (item != null)
                item.gameObject.SetActive(false);
            if (pageItem != null)
                pageItem.gameObject.SetActive(false);

            // 呼叫基底 Show 時，就會自動幫我們註冊所有的輸入事件
            base.Show();

            InitDic();
        }

        public void InitDic(List<(string, List<Action<GameObject, int>>)> dic)
        {
            actionDic = dic;
            InitDic();
        }

        public void InitDic()
        {
            // 清除舊的頁面
            foreach (var go in pages)
            {
                GameObject.Destroy(go.gameObject);
            }
            pages.Clear();

            for (int i = 0; i < actionDic.Count; i++)
            {
                var go = GameObject.Instantiate(pageItem.gameObject, pageItem.transform.parent);
                var index = i;
                // todo 多語言
                go.GetComponentInChildren<TMP_Text>().text = actionDic[index].Item1;// LocalMgr.Instance.Local(actionDic[index].Item1);
                var item = go.GetComponent<JoypadItem>();
                pages.Add(item);
                item.Init(() =>
                {
                    selectedPageIndex = index;
                    updateUI();
                });
                go.SetActive(true);
            }

            updateUI();
        }

        public override void close()
        {
            // 呼叫基底 close 時，就會自動幫我們反註冊所有的輸入事件
            base.close();
        }

        protected virtual void updateUI()
        {
            foreach (var go in itemObjs)
            {
                GameObject.Destroy(go);
            }
            itemObjs.Clear();
            joypadItems.Clear();

            if (actionDic.Count > selectedItemIndex)
            {
                var actions = actionDic[selectedPageIndex];
                for (int i = 0; i < actions.Item2.Count; i++)
                {
                    var item = actions.Item2[i];
                    var index = i + (selectedPageIndex * gridWidth * gridWidth);
                    createBtn(index, item);
                }
            }
            updateJoyItems();

            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].SetSelected(i == selectedPageIndex);
            }
        }

        void createBtn(int index, Action<GameObject, int> onclick)
        {
            var go = GameObject.Instantiate(this.item, this.item.transform.parent);
            itemObjs.Add(go);
            var item = go.GetComponent<JoypadItem>();
            onclick.Invoke(go, index);

            joypadItems.Add(item);
            go.SetActive(true);
        }

        // ==========================================
        // 透過 Override 複寫基底方法，不再需要手動註冊
        // 已經更新為最新的命名：OnInputShoulderLeft / OnInputShoulderRight
        // ==========================================
        public override void OnInputShoulderLeft()
        {
            selectedPageIndex--;
            if (selectedPageIndex < 0)
                selectedPageIndex = actionDic.Count - 1;
            selectedItemIndex = 0;
            updateUI();
        }

        public override void OnInputShoulderRight()
        {
            selectedPageIndex++;
            if (selectedPageIndex >= actionDic.Count)
                selectedPageIndex = 0;
            selectedItemIndex = 0;
            updateUI();
        }

        // ==========================================
        // 封裝的輔助方法 (Helper Methods)
        // ==========================================

        /// <summary>
        /// 創建基礎的 Joypad 按鈕綁定委派
        /// </summary>
        protected System.Action<GameObject, int> CreateJoypadAction(string name, System.Action onClick)
        {
            return (go, i) => go.GetComponent<JoypadItem>().Init(name, onClick);
        }

        /// <summary>
        /// 創建開啟標準 UI Panel 的按鈕綁定委派
        /// </summary>
        protected System.Action<GameObject, int> CreatePanelAction<T>(string name = null) where T : UIPanel
        {
            return CreateJoypadAction(name ?? typeof(T).Name, () => UIMgr.Instance.ShowPanel<T>().Forget());
        }

        /// <summary>
        /// 創建開啟 SubPanel 的按鈕綁定委派
        /// </summary>
        protected System.Action<GameObject, int> CreateSubPanelAction<T>(string name = null) where T : UIPanel
        {
            return CreateJoypadAction(name ?? typeof(T).Name, () => OpenSubPanel<T>().Forget());
        }
    }
}