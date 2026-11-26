using UnityEditor;
using UnityEngine;

internal sealed class InstrumentAudioTuningWindow : EditorWindow
{
    private const string DefaultProfilePath = "Assets/Performance/Audio/JangguSoundProfile.asset";

    [SerializeField] private InstrumentSoundProfile profile;
    private Editor profileEditor;
    private Vector2 scroll;
    private GUIStyle saveButtonStyle;

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

        foreach (var preview in FindObjectsByType<InstrumentSamplePlayer>(FindObjectsSortMode.None))
        {
            if (preview.SaveSessionTuning())
            {
                EditorUtility.SetDirty(preview.SavedProfile);
                AssetDatabase.SaveAssetIfDirty(preview.SavedProfile);
            }
        }
    }

    [MenuItem("ARInstruments/Audio Tuning/Janggu")]
    private static void Open()
    {
        var window = GetWindow<InstrumentAudioTuningWindow>("Instrument Audio Tuning");
        window.profile = AssetDatabase.LoadAssetAtPath<InstrumentSoundProfile>(DefaultProfilePath);
    }

    [MenuItem("ARInstruments/Audio Tuning/Buk")]
    private static void OpenBuk()
    {
        var window = GetWindow<InstrumentAudioTuningWindow>("Instrument Audio Tuning");
        window.profile = AssetDatabase.LoadAssetAtPath<InstrumentSoundProfile>("Assets/Performance/Audio/BukSoundProfile.asset");
    }

    private void OnEnable()
    {
        minSize = new Vector2(500, 380);
        if (profile == null)
        {
            profile = AssetDatabase.LoadAssetAtPath<InstrumentSoundProfile>(DefaultProfilePath);
        }
    }

    private void OnGUI()
    {
        profile = (InstrumentSoundProfile)EditorGUILayout.ObjectField("악기 음원 프로필", profile,
            typeof(InstrumentSoundProfile), false);
        Editor.CreateCachedEditor(profile, typeof(InstrumentSoundProfileEditor), ref profileEditor);
        if (profileEditor == null)
        {
            return;
        }

        saveButtonStyle ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
        bool saveRequested;
        using (var view = new EditorGUILayout.ScrollViewScope(scroll))
        {
            scroll = view.scrollPosition;
            profileEditor.OnInspectorGUI();

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.46f, 0.74f, 1f);
                saveRequested = GUILayout.Button("프로필 저장", saveButtonStyle, GUILayout.Width(140), GUILayout.Height(28));
                GUI.backgroundColor = previousBackground;
            }
        }

        if (saveRequested)
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
