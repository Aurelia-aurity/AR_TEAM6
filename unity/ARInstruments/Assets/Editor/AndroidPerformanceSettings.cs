#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.Build;
using UnityEngine.Rendering;

public static class AndroidPerformanceSettings
{
    [MenuItem("ARInstruments/Apply Android performance settings")]
    public static void Apply()
    {
        NamedBuildTarget androidTarget = NamedBuildTarget.Android;

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan });

        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.SetScriptingBackend(androidTarget, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        Object[] audioAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
        if (audioAssets != null && audioAssets.Length > 0)
        {
            SerializedObject audioManager = new SerializedObject(audioAssets[0]);
            SerializedProperty dspBufferSize = audioManager.FindProperty("m_DSPBufferSize");
            SerializedProperty requestedDspSize = audioManager.FindProperty("m_RequestedDSPBufferSize");
            if (dspBufferSize != null)
            {
                dspBufferSize.intValue = 256;
            }
            if (requestedDspSize != null)
            {
                requestedDspSize.intValue = 256;
            }
            audioManager.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        Debug.Log(
            "Android performance settings applied: Vulkan-only, IL2CPP, ARM64, frame timing stats, and Audio DSP 256 samples.");
    }
}
#endif
