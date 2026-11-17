using UnityEditor;
using UnityEngine;

internal sealed class JangguAudioTuningWindow : EditorWindow
{
    private const string DefaultProfilePath = "Assets/Performance/Audio/JangguSoundProfile.asset";

    [SerializeField] private JangguSoundProfile profile;
    private Editor profileEditor;
    private Vector2 scroll;

    [InitializeOnLoadMethod]
    private static void RegisterPlayModeSave()
    {
        EditorApplication.playModeStateChanged += SaveCheckedPlayModeTuning;
    }

    private static void SaveCheckedPlayModeTuning(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode)
        {
            return;
        }

        foreach (var preview in FindObjectsByType<JangguAudioPreview>(FindObjectsSortMode.None))
        {
            if (preview.SaveSessionTuning())
            {
                EditorUtility.SetDirty(preview.SavedProfile);
                AssetDatabase.SaveAssetIfDirty(preview.SavedProfile);
            }
        }
    }

    [MenuItem("ARInstruments/Janggu Audio Tuning")]
    private static void Open()
    {
        GetWindow<JangguAudioTuningWindow>("Janggu Audio Tuning");
    }

    private void OnEnable()
    {
        minSize = new Vector2(500, 380);
        if (profile == null)
        {
            profile = AssetDatabase.LoadAssetAtPath<JangguSoundProfile>(DefaultProfilePath);
        }
    }

    private void OnGUI()
    {
        profile = (JangguSoundProfile)EditorGUILayout.ObjectField("장구 음원 프로필", profile,
            typeof(JangguSoundProfile), false);
        Editor.CreateCachedEditor(profile, typeof(JangguSoundProfileEditor), ref profileEditor);
        if (profileEditor == null)
        {
            return;
        }

        using (var view = new EditorGUILayout.ScrollViewScope(scroll))
        {
            scroll = view.scrollPosition;
            profileEditor.OnInspectorGUI();
        }

        if (GUILayout.Button("프로필 저장"))
        {
            AssetDatabase.SaveAssetIfDirty(profile);
        }
    }

    private void OnDisable()
    {
        if (profileEditor != null)
        {
            DestroyImmediate(profileEditor);
        }
    }
}
