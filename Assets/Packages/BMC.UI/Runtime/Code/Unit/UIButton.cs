using DG.Tweening;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BMC.UI
{
    public class UIButton : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject[] sendObjs;
        [SerializeField] private GameObject[] sendHObjs;
        [SerializeField] private RectTransform[] targets;
        [SerializeField] private float scale = 0.9f;
        [SerializeField] private float during = 0.1f;

        [Header("狀態切換物件")]
        [SerializeField] private GameObject[] OnEnterActive;
        [SerializeField] private GameObject[] OnPressActive;

        [Header("點擊判定")]
        [SerializeField, Tooltip("移動距離超過此像素值則取消 Click 觸發")]
        private float clickTolerance = 10f;

        [SerializeField, Header("音效(空為靜音)")] private AudioClip clickAudio;

        private bool isPressing;
        private bool isDrag;
        private bool isDragV;
        private bool isDragH;
        private Vector2 pressPos; // 記錄按下的座標

        private List<Tween> tweens = new List<Tween>();
        private CancellationTokenSource cts = new CancellationTokenSource();

        public Action OnClick;
        public Action OnEnter;
        public Action OnExit;

        public Action<Vector2> BeginDrag;
        public Action<Vector2> Drag;
        public Action<Vector2> EndDrag;

        private void OnEnable()
        {
            // 當 UI 啟用時，還原所有狀態
            isPressing = false;
            isDrag = false;
            isDragV = false;
            isDragH = false;

            ToggleEnterObjects(false);
            TogglePressObjects(false);

            // 還原縮放動畫
            Anima();
        }

        private void OnDisable()
        {
            ClearTweens();
        }

        private void OnDestroy()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = new CancellationTokenSource();
            }
            ClearTweens();
        }

        private void ClearTweens()
        {
            foreach (var tween in tweens)
            {
                if (tween != null && tween.IsActive())
                    tween.Kill();
            }
            tweens.Clear();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            OnEnter?.Invoke();

            // 處理進入時的物件顯示
            ToggleEnterObjects(true);

            // 如果沒有按住，就不執行後續的按壓動畫與狀態
            if (!isPressing)
                return;

            // 如果是在按住的狀態下拖回按鈕範圍內，重新開啟按壓狀態的視覺
            TogglePressObjects(true);
            Anima(scale);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnExit?.Invoke();

            // 離開時隱藏進入物件
            ToggleEnterObjects(false);

            // 如果在按住的狀態下拉出按鈕範圍，關閉按壓狀態的視覺
            if (isPressing)
            {
                TogglePressObjects(false);
            }

            // 還原動畫
            Anima();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressing = true;
            isDrag = false;
            isDragV = false;
            isDragH = false;
            pressPos = eventData.position;

            // 處理按住時的物件顯示
            TogglePressObjects(true);

            Anima(scale);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressing = false;

            // 放開時隱藏物件
            TogglePressObjects(false);

            float dist = Vector2.Distance(pressPos, eventData.position);

            if (!isDrag && dist <= clickTolerance)
            {
                OnClick?.Invoke();
                playClickAudio();
            }

            Anima();
        }

        private void ToggleEnterObjects(bool isVisible)
        {
            if (OnEnterActive == null) return;
            foreach (var obj in OnEnterActive)
            {
                if (obj != null) obj.SetActive(isVisible);
            }
        }

        private void TogglePressObjects(bool isVisible)
        {
            if (OnPressActive == null) return;
            foreach (var obj in OnPressActive)
            {
                if (obj != null) obj.SetActive(isVisible);
            }
        }

        void Anima(float scale = 1)
        {
            ClearTweens();
            foreach (var item in targets)
            {
                if (item == null)
                    continue;
                tweens.Add(item.DOScale(scale, during).SetEase(Ease.OutQuad));
            }
        }

        public void playClickAudio()
        {
            if (UIMgr.Instance != null)
                UIMgr.Instance.eventHandler.Send((int)UIEvent.AUDIO_BUTTON_CLICK);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            isDrag = true;
            BeginDrag?.Invoke(eventData.position);

            if (sendObjs != null)
            {
                foreach (var obj in sendObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.beginDragHandler);
            }

            if (sendHObjs != null)
            {
                foreach (var obj in sendHObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.beginDragHandler);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            Drag?.Invoke(eventData.position);

            if (!isDragH && !isDragV)
            {
                if (Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y))
                    isDragH = true;
                else
                    isDragV = true;
            }

            if (isDragV && sendObjs != null)
            {
                foreach (var obj in sendObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.dragHandler);
            }

            if (isDragH && sendHObjs != null)
            {
                foreach (var obj in sendHObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.dragHandler);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndDrag?.Invoke(eventData.position);

            if (sendObjs != null)
            {
                foreach (var obj in sendObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.endDragHandler);
            }

            if (sendHObjs != null)
            {
                foreach (var obj in sendHObjs)
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.endDragHandler);
            }
        }
    }
}