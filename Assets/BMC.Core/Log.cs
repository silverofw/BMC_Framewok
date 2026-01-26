using UnityEngine;

public class Log
{
    public static string DefaultInfoColor = "#2ECC71"; // 一個舒服的綠色
    public static void Info(string log)
    {
#if UNITY_EDITOR
        Debug.Log($"<color={DefaultInfoColor}>{log}</color>");
#else
        Debug.Log(log);
#endif
    }

    public static void Warning(string log)
    {
        Debug.LogWarning(log);
    }

    public static void Error(string log)
    {
        Debug.LogError(log);
    }
}
