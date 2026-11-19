using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JangguSoundProfile))]
internal sealed class JangguSoundProfileEditor : Editor
{
    private static readonly string[] Strengths = { "강", "중", "약" };
    private static readonly Type AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private const BindingFlags PreviewFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo PlayPreview = AudioUtil?.GetMethod("PlayPreviewClip", PreviewFlags,
        null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
    private static readonly MethodInfo StopPreview = AudioUtil?.GetMethod("StopAllPreviewClips", PreviewFlags);

    private string previewAssetPath;
    private bool ownsEditorPreview;
    private string status;

    public JangguAudioPreview ScenePreview { get; set; }

    private bool showReferences;
    private GUIStyle columnHeading;
    private double previewEnd;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var profile = (JangguSoundProfile)target;
        var deong = serializedObject.FindProperty("deong");
        var deok = serializedObject.FindProperty("deok");
        var kung = serializedObject.FindProperty("kung");

        EditorGUILayout.LabelField("원본 덩", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            for (int strength = 0; strength < Strengths.Length; strength++)
            {
                if (GUILayout.Button($"{Strengths[strength]} 재생"))
                {
                    int selection = strength;
                    Play($"덩 {Strengths[strength]}", GetClip(deong, strength), 1f, 1f,
                        () => ScenePreview?.PlayDeong(selection));
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("덕·쿵 샘플 설정", EditorStyles.boldLabel);
        int sampleToPlay = DrawSampleSettings("덕", "deokVolumes", "deokPitches", 0);
        int kungToPlay = DrawSampleSettings("쿵", "kungVolumes", "kungPitches", Strengths.Length);

        if (serializedObject.ApplyModifiedProperties() && Application.isPlaying && ScenePreview != null)
        {
            foreach (var controls in FindObjectsByType<JangguAudioPreviewControls>(FindObjectsSortMode.None))
            {
                controls.RefreshFromInspector(ScenePreview);
            }
        }

        int sample = kungToPlay >= 0 ? kungToPlay : sampleToPlay;
        if (sample >= 0)
        {
            bool isKung = sample >= Strengths.Length;
            int strength = sample % Strengths.Length;
            Play($"{(isKung ? "쿵" : "덕")} {Strengths[strength]}",
                GetClip(isKung ? kung : deok, strength), profile.GetGain(sample), profile.GetPitch(sample),
                () => ScenePreview?.PlaySample(sample));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("동시 타격", EditorStyles.boldLabel);
        columnHeading ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("덕 / 쿵", GUILayout.Width(55));
            foreach (string strength in Strengths)
            {
                GUILayout.Label($"쿵 {strength}", columnHeading, GUILayout.ExpandWidth(true));
            }
        }

        for (int row = 0; row < Strengths.Length; row++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"덕 {Strengths[row]}", GUILayout.Width(55));
                for (int column = 0; column < Strengths.Length; column++)
                {
                    if (GUILayout.Button($"{Strengths[row]} + {Strengths[column]}"))
                    {
                        int pair = row * Strengths.Length + column;
                        int kungSample = Strengths.Length + column;
                        Play($"덕 {Strengths[row]} + 쿵 {Strengths[column]}",
                            GetClip(deok, row), profile.GetGain(row), profile.GetPitch(row),
                            () => ScenePreview?.PlayPair(pair), GetClip(kung, column),
                            profile.GetGain(kungSample), profile.GetPitch(kungSample), true);
                    }
                }
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("재생 중지"))
        {
            StopPlayback();
            if (Application.isPlaying && ScenePreview != null)
            {
                ScenePreview.Stop();
            }

            status = null;
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.Space();
        showReferences = EditorGUILayout.Foldout(showReferences, "원본 음원", true);
        if (showReferences)
        {
            EditorGUILayout.PropertyField(deong);
            EditorGUILayout.PropertyField(deok);
            EditorGUILayout.PropertyField(kung);
        }
        serializedObject.ApplyModifiedProperties();
    }

    private int DrawSampleSettings(string stroke, string volumeProperty, string pitchProperty, int offset)
    {
        var volume = serializedObject.FindProperty(volumeProperty);
        var pitch = serializedObject.FindProperty(pitchProperty);
        Vector3 volumes = volume.vector3Value;
        Vector3 pitches = pitch.vector3Value;
        int selected = -1;
        for (int index = 0; index < Strengths.Length; index++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{stroke} {Strengths[index]}", GUILayout.Width(45));
                float oldLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 35f;
                volumes[index] = EditorGUILayout.Slider("음량", volumes[index], 0f, JangguSoundProfile.MaxGain);
                GUILayout.Space(16f);
                pitches[index] = EditorGUILayout.Slider("피치", pitches[index],
                    JangguSoundProfile.MinPitch, JangguSoundProfile.MaxPitch);
                EditorGUIUtility.labelWidth = oldLabelWidth;
                if (GUILayout.Button("재생", GUILayout.Width(42)))
                {
                    selected = offset + index;
                }
            }
        }

        if (volumes != volume.vector3Value) volume.vector3Value = volumes;
        if (pitches != pitch.vector3Value) pitch.vector3Value = pitches;
        return selected;
    }

    private static AudioClip GetClip(SerializedProperty clips, int index)
    {
        return index < clips.arraySize ? clips.GetArrayElementAtIndex(index).objectReferenceValue as AudioClip : null;
    }

    private void Play(string label, AudioClip first, float firstVolume, float firstPitch, Action playInGame,
        AudioClip second = null, float secondVolume = 0f, float secondPitch = 1f, bool isCombination = false)
    {
        StopPlayback();
        if (first == null || (isCombination && second == null))
        {
            status = "연결된 원본 음원이 없습니다.";
            return;
        }

        if (Application.isPlaying && ScenePreview != null)
        {
            playInGame();
        }
        else
        {
            if (PlayPreview == null || StopPreview == null)
            {
                status = "이 Unity 버전에서 편집 모드 오디오 미리듣기를 사용할 수 없습니다.";
                return;
            }

            firstVolume *= JangguSoundProfile.PlaybackGain;
            secondVolume *= JangguSoundProfile.PlaybackGain;

            // Unity의 에셋 미리듣기는 임포트된 클립을 사용한다.
            AudioClip clip = second == null && firstVolume == 1f && firstPitch == 1f
                ? first
                : ImportPreviewMix(first, firstVolume, second, secondVolume, firstPitch, secondPitch);
            if (clip == null)
            {
                status = "원본의 PCM 데이터를 읽거나 재생용으로 변환할 수 없습니다.";
                return;
            }

            PlayPreview.Invoke(null, new object[] { clip, 0, false });
            ownsEditorPreview = true;
            previewEnd = EditorApplication.timeSinceStartup + clip.length + 0.25;
            EditorApplication.update += FinishPreviewWhenElapsed;
        }

        status = label;
    }

    private AudioClip ImportPreviewMix(AudioClip first, float firstVolume, AudioClip second, float secondVolume, float firstPitch, float secondPitch)
    {
        if (second != null && (first.channels != second.channels || first.frequency != second.frequency))
        {
            return null;
        }

        int frames = Mathf.CeilToInt(first.samples / firstPitch);
        if (second != null)
        {
            frames = Mathf.Max(frames, Mathf.CeilToInt(second.samples / secondPitch));
        }
        var mixedSamples = new float[frames * first.channels];
        foreach (var (clip, volume, pitch) in new[] { (first, firstVolume, firstPitch), (second, secondVolume, secondPitch) })
        {
            if (clip == null)
            {
                continue;
            }

            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0))
            {
                return null;
            }

            // 원본 프레임을 보간해서 재생 속도와 음높이를 함께 바꾼다.
            int outputFrames = Mathf.CeilToInt(clip.samples / pitch);
            for (int frame = 0; frame < outputFrames; frame++)
            {
                float position = frame * pitch;
                int lower = Mathf.Min((int)position, clip.samples - 1);
                int upper = Mathf.Min(lower + 1, clip.samples - 1);
                for (int channel = 0; channel < clip.channels; channel++)
                {
                    float sample = Mathf.Lerp(samples[lower * clip.channels + channel],
                        samples[upper * clip.channels + channel], position - lower);
                    mixedSamples[frame * clip.channels + channel] += sample * volume;
                }
            }
        }

        previewAssetPath = $"Assets/Editor/JangguPreview-{Guid.NewGuid():N}.wav";
        try
        {
            using (var writer = new BinaryWriter(File.Create(previewAssetPath)))
            {
                int dataSize = mixedSamples.Length * sizeof(short);
                writer.Write(0x46464952); // RIFF
                writer.Write(36 + dataSize);
                writer.Write(0x45564157); // WAVE
                writer.Write(0x20746D66); // fmt
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)first.channels);
                writer.Write(first.frequency);
                writer.Write(first.frequency * first.channels * sizeof(short));
                writer.Write((short)(first.channels * sizeof(short)));
                writer.Write((short)16);
                writer.Write(0x61746164); // data
                writer.Write(dataSize);
                foreach (float sample in mixedSamples)
                {
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32768f), short.MinValue, short.MaxValue));
                }
            }

            AssetDatabase.ImportAsset(previewAssetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(previewAssetPath);
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(previewAssetPath);
            if (clip == null)
            {
                DeletePreviewAsset();
            }

            return clip;
        }
        catch
        {
            DeletePreviewAsset();
            throw;
        }
    }

    private void OnDisable()
    {
        StopPlayback();
    }

    private void FinishPreviewWhenElapsed()
    {
        if (EditorApplication.timeSinceStartup >= previewEnd)
        {
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        EditorApplication.update -= FinishPreviewWhenElapsed;

        if (ownsEditorPreview)
        {
            StopPreview.Invoke(null, null);
            ownsEditorPreview = false;
        }

        DeletePreviewAsset();
    }

    private void DeletePreviewAsset()
    {
        if (string.IsNullOrEmpty(previewAssetPath))
        {
            return;
        }

        string path = previewAssetPath;
        previewAssetPath = null;
        if (!AssetDatabase.DeleteAsset(path))
        {
            File.Delete(path);
        }
    }
}

[CustomEditor(typeof(JangguAudioPreview))]
public sealed class JangguAudioPreviewEditor : Editor
{
    private Editor profileEditor;
    private bool showSceneBindings;

    public override void OnInspectorGUI()
    {
        var preview = (JangguAudioPreview)target;
        var profile = preview.Profile;
        if (profile != null)
        {
            CreateCachedEditor(profile, typeof(JangguSoundProfileEditor), ref profileEditor);
            ((JangguSoundProfileEditor)profileEditor).ScenePreview = preview;
            profileEditor.OnInspectorGUI();
        }
        else
        {
            EditorGUILayout.HelpBox("장구 음원 프로필을 연결하세요.", MessageType.Warning);
        }

        serializedObject.Update();
        showSceneBindings = EditorGUILayout.Foldout(showSceneBindings, "씬 AudioSource 및 프로필", true);
        if (showSceneBindings || profile == null)
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("primarySource"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("secondarySource"));
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void OnDisable()
    {
        if (profileEditor != null)
        {
            DestroyImmediate(profileEditor);
        }
    }
}
