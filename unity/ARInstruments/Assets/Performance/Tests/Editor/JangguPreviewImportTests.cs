using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class JangguPreviewImportTests
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Type editorType;
    private JangguSoundProfile profile;
    private Editor editor;
    private string fixturePath;
    private BuildTargetGroup platform;

    [SetUp]
    public void SetUp()
    {
        editorType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("InstrumentSoundProfileEditor"))
            .First(type => type != null);
        profile = ScriptableObject.CreateInstance<JangguSoundProfile>();
        editor = Editor.CreateEditor(profile, editorType);
        platform = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        fixturePath = $"Assets/Editor/JangguImportTest-{Guid.NewGuid():N}.wav";
        Assert.That(AssetDatabase.CopyAsset(
            "Assets/Performance/Audio/Janggu/Janggu_1_1.wav", fixturePath), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(editor);
        UnityEngine.Object.DestroyImmediate(profile);
        if (!string.IsNullOrEmpty(fixturePath)) AssetDatabase.DeleteAsset(fixturePath);
    }

    [TestCase(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.PCM, true)]
    [TestCase(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, false)]
    [TestCase(AudioClipLoadType.Streaming, AudioCompressionFormat.PCM, false)]
    [TestCase(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.Vorbis, true)]
    public void PreviewMix_RespectsPcmReadability(AudioClipLoadType loadType,
        AudioCompressionFormat format, bool allowed)
    {
        var importer = (AudioImporter)AssetImporter.GetAtPath(fixturePath);
        importer.ClearSampleSettingOverride(platform);
        var settings = importer.defaultSampleSettings;
        settings.loadType = loadType;
        settings.compressionFormat = format;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = false;
        importer.SaveAndReimport();

        AssertPreview(allowed);
    }

    [TestCase(AudioCompressionFormat.Vorbis, AudioCompressionFormat.PCM, true)]
    [TestCase(AudioCompressionFormat.PCM, AudioCompressionFormat.Vorbis, false)]
    public void PreviewMix_UsesActivePlatformCompression(AudioCompressionFormat defaultFormat,
        AudioCompressionFormat overrideFormat, bool allowed)
    {
        var importer = (AudioImporter)AssetImporter.GetAtPath(fixturePath);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.CompressedInMemory;
        settings.compressionFormat = defaultFormat;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        settings.compressionFormat = overrideFormat;
        Assert.That(importer.SetOverrideSampleSettings(platform, settings), Is.True);
        importer.loadInBackground = false;
        importer.SaveAndReimport();

        AssertPreview(allowed);
    }

    [UnityTest]
    public IEnumerator BackgroundLoading_PreviewCanRetryWithoutImportChanges()
    {
        var importer = (AudioImporter)AssetImporter.GetAtPath(fixturePath);
        importer.ClearSampleSettingOverride(platform);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = true;
        importer.SaveAndReimport();

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(fixturePath);
        Assert.That(clip.UnloadAudioData(), Is.True);
        Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Unloaded));

        object[] arguments = { clip, 0.35f, null, 0f, 1f, 1f, null };
        var mixed = (AudioClip)editorType.GetMethod("ImportPreviewMix", InstanceFlags).Invoke(editor, arguments);
        if (mixed == null)
        {
            Assert.That((string)arguments[6], Does.Contain("로딩"));
            Assert.That((string)arguments[6], Does.Contain("다시 시도"));
            Assert.That((string)arguments[6], Does.Not.Contain("Decompress On Load"));
            Assert.That(editorType.GetField("previewAssetPath", InstanceFlags).GetValue(editor), Is.Null);
        }
        else
        {
            // A short clip can finish background loading within the first request.
            Assert.That(arguments[6], Is.Null);
        }

        double deadline = EditorApplication.timeSinceStartup + 10;
        while (clip.loadState == AudioDataLoadState.Loading && EditorApplication.timeSinceStartup < deadline)
        {
            yield return null;
        }
        Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));

        editorType.GetMethod("StopPlayback", InstanceFlags).Invoke(editor, null);
        AssertPreview(true);
        Assert.That(clip.loadInBackground, Is.True);
        Assert.That(clip.preloadAudioData, Is.False);
    }

    private void AssertPreview(bool allowed)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(fixturePath);
        bool requiresSettings = (bool)editorType.GetMethod("RequiresPreviewImportSettings",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { clip });
        Assert.That(requiresSettings, Is.EqualTo(!allowed), "Inspector warning must match playback eligibility.");

        // Exercise the actual PCM mixing and temporary WAV import path without playing audio.
        object[] arguments = { clip, 0.35f, null, 0f, 1f, 1f, null };
        var mixed = (AudioClip)editorType.GetMethod("ImportPreviewMix", InstanceFlags).Invoke(editor, arguments);
        if (allowed)
        {
            Assert.That(arguments[6], Is.Null);
            Assert.That(mixed, Is.Not.Null);
            var samples = new float[mixed.samples * mixed.channels];
            Assert.That(mixed.GetData(samples, 0), Is.True);
            Assert.That(samples.Any(sample => Mathf.Abs(sample) > 0.001f), Is.True,
                "Successful preview must contain audible samples.");
        }
        else
        {
            Assert.That(mixed, Is.Null);
            Assert.That((string)arguments[6], Does.Contain("Decompress On Load"));
        }
    }
}
