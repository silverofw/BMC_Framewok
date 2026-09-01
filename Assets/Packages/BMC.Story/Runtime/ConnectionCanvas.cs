using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace BMC.Story
{
    /// <summary>
    /// UI Toolkit 版節點連線特效，對應 uGUI 版的 ConnectionDrawer。
    /// 用 Painter2D 畫多段線取代手動組 UIVertex quad，噪聲搖擺/首尾歸零/緩動數學逐行對應原版的
    /// DrawConnection。Painter2D 一次 Stroke() 只有一種顏色/線寬，因此閃爍效果是整條線同步套用
    /// (currentFlickerAlpha/currentFlickerWidth)，不是原版理論上可能做到的逐頂點漸層——這是唯一的
    /// 視覺精度取捨，原本閃爍本來就是整條線套同一個 scalar，不影響觀感。
    /// </summary>
    public class ConnectionCanvas : VisualElement
    {
        public class Connection
        {
            public VisualElement start;
            public VisualElement end;
        }

        public List<Connection> connections = new List<Connection>();

        public float lineThickness = 5f;
        public int segments = 50;
        public Ease lineEase = Ease.Linear;

        public bool useLightning = true;
        public float noiseStrength = 30f;
        public float noiseFrequency = 10f;
        public float noiseSpeed = 15f;

        public bool useFlicker = true;
        public float minAlpha = 0.4f;
        public float flickerSpeed = 3f;

        public Color color = Color.white;

        private float timeOffset;
        private float currentFlickerAlpha = 1f;
        private float currentFlickerWidth = 1f;
        private IVisualElementScheduledItem tickHandle;

        public ConnectionCanvas()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                tickHandle = schedule.Execute(Tick).Every(16);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                tickHandle?.Pause();
                tickHandle = null;
            });
        }

        public void SetConnections(List<Connection> newConnections)
        {
            connections = newConnections ?? new List<Connection>();
            MarkDirtyRepaint();
        }

        private void Tick()
        {
            if (!useLightning)
                return;

            timeOffset += (16f / 1000f) * noiseSpeed;

            if (useFlicker)
            {
                float flickerVal = Mathf.PerlinNoise(Time.time * flickerSpeed, 100f);
                currentFlickerAlpha = Mathf.Lerp(minAlpha, 1f, flickerVal);
                currentFlickerWidth = Mathf.Lerp(0.7f, 1.3f, flickerVal);
            }
            else
            {
                currentFlickerAlpha = 1f;
                currentFlickerWidth = 1f;
            }

            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (connections == null || connections.Count == 0)
                return;

            var painter = mgc.painter2D;
            foreach (var conn in connections)
            {
                if (conn.start?.panel == null || conn.end?.panel == null)
                    continue;
                if (conn.start.resolvedStyle.display == DisplayStyle.None ||
                    conn.end.resolvedStyle.display == DisplayStyle.None)
                    continue;

                Vector2 localStart = this.WorldToLocal(conn.start.worldBound.center);
                Vector2 localEnd = this.WorldToLocal(conn.end.worldBound.center);

                DrawConnection(painter, localStart, localEnd);
            }
        }

        private void DrawConnection(Painter2D painter, Vector2 start, Vector2 end)
        {
            float dist = Vector2.Distance(start, end);
            if (dist < 0.1f)
                return;

            Color finalColor = color;
            finalColor.a *= currentFlickerAlpha;
            painter.strokeColor = finalColor;
            painter.lineWidth = lineThickness * currentFlickerWidth;

            Vector2 overallDir = (end - start).normalized;
            Vector2 overallNormal = new Vector2(-overallDir.y, overallDir.x);

            painter.BeginPath();
            painter.MoveTo(start);

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;

                float x = Mathf.Lerp(start.x, end.x, t);
                float y = DOVirtual.EasedValue(start.y, end.y, t, lineEase);
                Vector2 basePoint = new Vector2(x, y);

                if (useLightning && i < segments)
                {
                    // Sin 讓兩端點的偏移量歸零，確保準確連接首尾
                    float pinMask = Mathf.Sin(t * Mathf.PI);

                    float noise1 = Mathf.PerlinNoise(t * noiseFrequency + timeOffset, 0f) * 2f - 1f;
                    float noise2 = Mathf.PerlinNoise(t * noiseFrequency * 2.5f - timeOffset, 5f) * 2f - 1f;

                    float totalNoise = (noise1 + noise2 * 0.3f) * noiseStrength * pinMask;
                    basePoint += overallNormal * totalNoise;
                }

                painter.LineTo(basePoint);
            }

            painter.Stroke();
        }
    }
}
