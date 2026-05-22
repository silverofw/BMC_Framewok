using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

[MovedFrom(true, "Assembly-CSharp", null, null)]
[RequireComponent(typeof(CanvasRenderer))]
public class ConnectionDrawer : MaskableGraphic
{
    [System.Serializable]
    public class Connection
    {
        public RectTransform start;
        public RectTransform end;
    }

    public List<Connection> connections = new List<Connection>();

    [Header("Line Settings")]
    [Range(1f, 30f)]
    public float lineThickness = 5f;

    [Range(10, 150)]
    public int segments = 50;

    [Header("DOTween Settings")]
    public Ease lineEase = Ease.Linear;

    [Header("Lightning Noise Settings")]
    public bool useLightning = true;

    [Range(0f, 100f)]
    public float noiseStrength = 30f;

    [Range(1f, 30f)]
    public float noiseFrequency = 10f;

    [Range(0f, 50f)]
    public float noiseSpeed = 15f;

    [Header("Flicker Settings")]
    public bool useFlicker = true;

    [Range(0f, 1f)]
    public float minAlpha = 0.4f;

    [Range(0.1f, 20f)]
    public float flickerSpeed = 3f;

    private float timeOffset = 0f;
    private float currentFlickerAlpha = 1f;
    private float currentFlickerWidth = 1f;

    public void SetConnections(List<Connection> newConnections)
    {
        connections = newConnections;
        SetAllDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        // 在編輯模式下若開啟 Flicker，強制顯示 minAlpha 讓你預覽最暗的狀態
        if (!Application.isPlaying)
        {
            currentFlickerAlpha = useFlicker ? minAlpha : 1f;
            currentFlickerWidth = 1f;
            SetAllDirty();
        }
    }
#endif

    protected virtual void Update()
    {
        if (useLightning && Application.isPlaying)
        {
            timeOffset += Time.deltaTime * noiseSpeed;

            if (useFlicker)
            {
                // 使用 PerlinNoise 產生平滑的連續隨機起伏
                float flickerVal = Mathf.PerlinNoise(Time.time * flickerSpeed, 100f);
                currentFlickerAlpha = Mathf.Lerp(minAlpha, 1f, flickerVal);
                currentFlickerWidth = Mathf.Lerp(0.7f, 1.3f, flickerVal);
            }
            else
            {
                currentFlickerAlpha = 1f;
                currentFlickerWidth = 1f;
            }

            SetAllDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (connections == null || connections.Count == 0) return;

        foreach (var conn in connections)
        {
            if (conn.start == null || conn.end == null || !conn.start.gameObject.activeInHierarchy || !conn.end.gameObject.activeInHierarchy)
                continue;

            Vector2 localStart = GetLocalPosition(conn.start);
            Vector2 localEnd = GetLocalPosition(conn.end);

            DrawConnection(vh, localStart, localEnd);
        }
    }

    private Vector2 GetLocalPosition(RectTransform target)
    {
        Vector3 worldPos = target.position;
        Vector3 local3D = transform.InverseTransformPoint(worldPos);
        return new Vector2(local3D.x, local3D.y);
    }

    private void DrawConnection(VertexHelper vh, Vector2 start, Vector2 end)
    {
        float dist = Vector2.Distance(start, end);
        if (dist < 0.1f) return;

        Vector2 lastPoint = start;
        Vector2 overallDir = (end - start).normalized;
        Vector2 overallNormal = new Vector2(-overallDir.y, overallDir.x);

        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;

            float x = Mathf.Lerp(start.x, end.x, t);
            float y = DOVirtual.EasedValue(start.y, end.y, t, lineEase);
            Vector2 basePoint = new Vector2(x, y);

            if (useLightning && i < segments)
            {
                // 利用 Sin 讓兩端點的偏移量歸零，確保準確連接首尾
                float pinMask = Mathf.Sin(t * Mathf.PI);

                float noise1 = Mathf.PerlinNoise(t * noiseFrequency + timeOffset, 0f) * 2f - 1f;
                float noise2 = Mathf.PerlinNoise(t * noiseFrequency * 2.5f - timeOffset, 5f) * 2f - 1f;

                float totalNoise = (noise1 + noise2 * 0.3f) * noiseStrength * pinMask;
                basePoint += overallNormal * totalNoise;
            }

            DrawSegment(vh, lastPoint, basePoint);
            lastPoint = basePoint;
        }
    }

    private void DrawSegment(VertexHelper vh, Vector2 start, Vector2 end)
    {
        Vector2 dir = (end - start).normalized;
        float finalThickness = lineThickness * currentFlickerWidth;
        Vector2 normal = new Vector2(-dir.y, dir.x) * finalThickness * 0.5f;

        UIVertex[] verts = new UIVertex[4];

        // 使用 Color 處理透明度，避免 byte 型別造成的數值丟失
        Color finalColor = color;
        finalColor.a *= currentFlickerAlpha;

        for (int i = 0; i < 4; i++)
        {
            verts[i] = UIVertex.simpleVert;
            verts[i].color = finalColor;
        }

        verts[0].uv0 = new Vector2(0, 0);
        verts[1].uv0 = new Vector2(0, 1);
        verts[2].uv0 = new Vector2(1, 1);
        verts[3].uv0 = new Vector2(1, 0);

        verts[0].position = start - normal;
        verts[1].position = start + normal;
        verts[2].position = end + normal;
        verts[3].position = end - normal;

        vh.AddUIVertexQuad(verts);
    }
}