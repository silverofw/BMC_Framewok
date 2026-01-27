using DG.Tweening; // 1. 引入 DOTween
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
    public float lineThickness = 5f;
    public int segments = 20;

    [Header("DOTween Settings")]
    // 2. 新增 Ease 變數，讓你可以在 Inspector 選擇曲線類型
    // 推薦使用 InOutSine, InOutCubic, 或 InOutQuint 來獲得漂亮的 S 型曲線
    public Ease lineEase = Ease.InOutCubic;

    // 原本的 curveStrength 不再需要了，因為 Ease 函數決定了彎曲的形狀
    // public float curveStrength = 50f; 

    public void SetConnections(List<Connection> newConnections)
    {
        connections = newConnections;
        SetAllDirty();
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

    // 3. 改寫繪製邏輯，使用 DOTween
    private void DrawConnection(VertexHelper vh, Vector2 start, Vector2 end)
    {
        float dist = Vector2.Distance(start, end);
        if (dist < 0.1f) return;

        Vector2 lastPoint = start;

        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;

            // X 軸：線性插值 (Linear)
            // 讓線條在水平方向上均勻前進
            float x = Mathf.Lerp(start.x, end.x, t);

            // Y 軸：使用 DOTween 的 Ease 曲線插值
            // 這會根據選定的 Ease 類型 (如 InOutCubic) 計算高度變化，形成 S 曲線
            float y = DOVirtual.EasedValue(start.y, end.y, t, lineEase);

            Vector2 newPoint = new Vector2(x, y);

            DrawSegment(vh, lastPoint, newPoint);
            lastPoint = newPoint;
        }
    }

    private void DrawSegment(VertexHelper vh, Vector2 start, Vector2 end)
    {
        Vector2 dir = (end - start).normalized;
        Vector2 normal = new Vector2(-dir.y, dir.x) * lineThickness * 0.5f;

        UIVertex[] verts = new UIVertex[4];

        for (int i = 0; i < 4; i++)
        {
            verts[i] = UIVertex.simpleVert;
            verts[i].color = color;
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