using UnityEngine;

public class SafeAreaHelper : MonoBehaviour 
{
    public void Start()
    {
        StretchToSafeArea(GetComponent<RectTransform>(), true);
    }
    public void StretchToSafeArea(RectTransform rectTransform, bool forceUpdate = false)
    {
        //Debug.Log($"[StretchToSafeArea][{Screen.safeArea}][{Screen.width}:{Screen.height}]");
        Rect safeRect = Screen.safeArea;

        // Convert safe area rectangle from absolute pixels to normalized anchor coordinates
        var anchorMin = safeRect.position;
        var anchorMax = safeRect.position + safeRect.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;

        if (forceUpdate)
        {
            rectTransform.ForceUpdateRectTransforms();
        }
    }
}
