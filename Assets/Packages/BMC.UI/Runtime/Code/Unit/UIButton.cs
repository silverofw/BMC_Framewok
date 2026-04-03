using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting.APIUpdating;

namespace BMC.UI
{
    [MovedFrom(true, "Assembly-CSharp", null, null)]
    public class UIButton : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject[] sendObjs;
        [SerializeField] private GameObject[] sendHObjs;
        [SerializeField] private RectTransform[] targets;
        [SerializeField] private float scale = 0.9f;
        [SerializeField] private float during = 0.1f;
        [SerializeField, Header("音效(空為靜音)")] private AudioClip clickAudio;

        private bool isPressing;
        private bool isDrag;
        private bool isDragV;
        private bool isDragH;
        private List<Tween> tweens = new List<Tween>();
        private CancellationTokenSource cts = new CancellationTokenSource();

        public Action OnClick;
        public Action OnEnter;
        public Action OnExit;
        public Action BeginDrag;
        public Action Drag;
        public Action EndDrag;

        private void OnDisable()
        {
            foreach (var tween in tweens)
            {
                tween.Kill();
            }
            tweens.Clear();
        }

        private void OnDestroy()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = new CancellationTokenSource();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!isPressing)
                return;
            Anima(scale);
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            //Debug.Log("Pointer exit!");
            Anima();
        }
        public void OnPointerDown(PointerEventData eventData)
        {
            //Debug.Log("Pointer down!");
            isPressing = true;
            isDrag = false;
            isDragV = false;
            isDragH = false;
            Anima(scale);
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            //Debug.Log("Pointer up!"); 
            isPressing = false;
            if (!isDrag)
            {
                //Toast.Show("CHICK");
                OnClick?.Invoke();
                playClickAudio();
            }
            Anima();
        }

        void Anima(float scale = 1)
        {
            foreach (var item in targets)
            {
                if (item == null)
                {
                    Debug.LogWarning($"[BreatheButton][{name}] null ref");
                    continue;
                }
                tweens.Add(item.DOScale(scale, during));
            }
        }

        public void playClickAudio()
        {
            UIMgr.Instance.eventHandler.Send((int)UIEvent.AUDIO_BUTTON_CLICK);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            isDrag = true;

            // 傳遞 PointerEventData
            if (sendObjs != null)
            {
                foreach (var obj in sendObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.beginDragHandler);
                }
            }

            if (sendHObjs != null)
            {
                foreach (var obj in sendHObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.beginDragHandler);
                }
            }
        }
        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragH && !isDragV)
            {
                if (Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y))
                {
                    isDragH = true;
                }
                else
                {
                    isDragV = true;
                }
            }

            if (isDragV)
            {
                // 傳遞 PointerEventData
                if (sendObjs == null)
                    return;
                foreach (var obj in sendObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.dragHandler);
                }
            }
            if (isDragH)
            {
                // 傳遞 PointerEventData
                if (sendHObjs == null)
                    return;
                foreach (var obj in sendHObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.dragHandler);
                }
            }
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            // 傳遞 PointerEventData
            if (sendObjs != null)
            {
                foreach (var obj in sendObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.endDragHandler);
                }
            }

            if (sendHObjs != null)
            {
                foreach (var obj in sendHObjs)
                {
                    ExecuteEvents.Execute(obj, eventData, ExecuteEvents.endDragHandler);
                }
            }
        }
    }
}
