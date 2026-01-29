using UnityEngine;
using UnityEditor;

public class RectTransformContext
{
    // 定義選單的基礎順序，確保出現在預設選單的下方
    private const int ORDER_MAIN = 1000;
    private const int ORDER_SIDE = 1020; // 差距 > 10，會產生分隔線

    // =========================================================
    // 第一區：常用功能 (Stretch & Center)
    // =========================================================

    [MenuItem("CONTEXT/RectTransform/Anchor: Stretch All (填滿)", false, ORDER_MAIN + 1)]
    static void AnchorStretchAll(MenuCommand command)
    {
        RectTransform rt = (RectTransform)command.context;
        Undo.RecordObject(rt, "Anchor Stretch All");

        // 設定錨點為全開
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        // 歸零邊距 (Left, Right, Top, Bottom)
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    [MenuItem("CONTEXT/RectTransform/Anchor: Center (置中)", false, ORDER_MAIN + 2)]
    static void AnchorCenter(MenuCommand command)
    {
        // 置中：錨點 0.5, 軸心 0.5
        SetSmartAnchor(command, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    }

    // =========================================================
    // 第二區：方向對齊 (Top, Bottom, Left, Right)
    // =========================================================

    [MenuItem("CONTEXT/RectTransform/Snap: Top (靠上)", false, ORDER_SIDE + 1)]
    static void SnapTop(MenuCommand command)
    {
        // 靠上中：錨點 (0.5, 1), 軸心 (0.5, 1)
        SetSmartAnchor(command, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
    }

    [MenuItem("CONTEXT/RectTransform/Snap: Bottom (靠下)", false, ORDER_SIDE + 2)]
    static void SnapBottom(MenuCommand command)
    {
        // 靠下中：錨點 (0.5, 0), 軸心 (0.5, 0)
        SetSmartAnchor(command, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
    }

    [MenuItem("CONTEXT/RectTransform/Snap: Left (靠左)", false, ORDER_SIDE + 3)]
    static void SnapLeft(MenuCommand command)
    {
        // 靠左中：錨點 (0, 0.5), 軸心 (0, 0.5)
        SetSmartAnchor(command, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
    }

    [MenuItem("CONTEXT/RectTransform/Snap: Right (靠右)", false, ORDER_SIDE + 4)]
    static void SnapRight(MenuCommand command)
    {
        // 靠右中：錨點 (1, 0.5), 軸心 (1, 0.5)
        SetSmartAnchor(command, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
    }

    // =========================================================
    // 內部共用邏輯 (處理 Undo 與 座標歸零)
    // =========================================================

    private static void SetSmartAnchor(MenuCommand command, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        RectTransform rt = (RectTransform)command.context;

        // 紀錄 Undo，讓你可以按 Ctrl+Z 復原
        Undo.RecordObject(rt, "Change Anchor");

        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero; // 讓 UI 直接貼齊該位置
    }
}