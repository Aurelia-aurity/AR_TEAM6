#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AndroidPerformanceSettings
{
    [MenuItem("ARInstruments/Apply Android performance settings")]
    public static void Apply()
    {
        BuildTargetGroup android = BuildTargetGroup.Android;
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.SetScriptingBackend(
            android,
            ScriptingImplementation.IL2CPP);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "Android performance settings applied: Vulkan, frame timing stats, and IL2CPP.");
    }
}
#endif
