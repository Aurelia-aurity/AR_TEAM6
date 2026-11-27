using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class InstrumentPreviewOwnershipTests
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private Type editorType;
    private JangguSoundProfile profile;
    private AudioClip clip;
    private Editor first;
    private Editor second;

    private object ActivePreview => editorType.GetField("activePreview",
        BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    [SetUp]
    public void SetUp()
    {
        // 테스트 어셈블리에서 기본 Editor 어셈블리의 타입을 찾아 사용한다.
        editorType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("InstrumentSoundProfileEditor"))
            .First(type => type != null);
        profile = ScriptableObject.CreateInstance<JangguSoundProfile>();
        clip = AudioClip.Create("Ownership test", 44100, 1, 44100, false);
        first = Editor.CreateEditor(profile, editorType);
        second = Editor.CreateEditor(profile, editorType);
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(second);
        UnityEngine.Object.DestroyImmediate(first);
        UnityEngine.Object.DestroyImmediate(clip);
        UnityEngine.Object.DestroyImmediate(profile);
    }

    [Test]
    public void NewPreview_ReplacesOwnerAndCleansPreviousTemporaryClip()
    {
        Play(first);
        string oldPath = PreviewPath(first);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(oldPath), Is.Not.Null);

        Play(second);

        Assert.That(ActivePreview, Is.SameAs(second));
        Assert.That(PreviewPath(first), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(oldPath), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(PreviewPath(second)), Is.Not.Null);
    }

    [Test]
    public void PreviousTimerExpiry_PreservesCurrentPreview()
    {
        Play(first);
        Play(second);

        editorType.GetField("previewEnd", InstanceFlags).SetValue(first, -1d);
        Call(first, "FinishPreviewWhenElapsed");

        Assert.That(ActivePreview, Is.SameAs(second));
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(PreviewPath(second)), Is.Not.Null);
    }

    [Test]
    public void ClosingPreviousEditor_PreservesCurrentPreview()
    {
        Play(first);
        Play(second);

        UnityEngine.Object.DestroyImmediate(first);
        first = null;

        Assert.That(ActivePreview, Is.SameAs(second));
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(PreviewPath(second)), Is.Not.Null);
    }

    [Test]
    public void StoppingCurrentOwner_ReleasesOwnerAndTemporaryClip()
    {
        Play(second);
        string path = PreviewPath(second);

        Call(second, "StopPlayback");

        Assert.That(ActivePreview, Is.Null);
        Assert.That(PreviewPath(second), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Null);
    }

    [Test]
    public void ReturningToPreviousEditor_PreservesNewestPreview()
    {
        Play(first);
        Play(second);
        string replacedPath = PreviewPath(second);
        Play(first);

        editorType.GetField("previewEnd", InstanceFlags).SetValue(second, -1d);
        Call(second, "FinishPreviewWhenElapsed");
        UnityEngine.Object.DestroyImmediate(second);
        second = null;

        Assert.That(ActivePreview, Is.SameAs(first));
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(PreviewPath(first)), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(replacedPath), Is.Null);
    }

    [Test]
    public void CurrentTimerExpiry_ReleasesOwnerAndTemporaryClip()
    {
        Play(first);
        string path = PreviewPath(first);

        editorType.GetField("previewEnd", InstanceFlags).SetValue(first, -1d);
        Call(first, "FinishPreviewWhenElapsed");

        Assert.That(ActivePreview, Is.Null);
        Assert.That(PreviewPath(first), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Null);
    }

    [Test]
    public void ClosingCurrentEditor_ReleasesOwnerAndTemporaryClip()
    {
        Play(second);
        string path = PreviewPath(second);

        UnityEngine.Object.DestroyImmediate(second);
        second = null;

        Assert.That(ActivePreview, Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Null);
    }

    [Test]
    public void RepeatedStopAndCleanup_AreSafe()
    {
        Play(first);
        string path = PreviewPath(first);

        Assert.DoesNotThrow(() =>
        {
            Call(first, "StopPlayback");
            Call(first, "StopPlayback");
            editorType.GetField("previewEnd", InstanceFlags).SetValue(first, -1d);
            Call(first, "FinishPreviewWhenElapsed");
            Call(first, "OnDisable");
        });

        Assert.That(ActivePreview, Is.Null);
        Assert.That(PreviewPath(first), Is.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Null);
    }

    private void Play(Editor editor)
    {
        Call(editor, "Play", "Test preview", clip, 1f, 1f, (Action)(() => { }),
            null, 0f, 1f, false);
        Assert.That(ActivePreview, Is.SameAs(editor));
    }

    [Test]
    public void SwitchingFromJangguToBuk_OldTimerAndEditorCannotStopBuk()
    {
        var buk = ScriptableObject.CreateInstance<BukSoundProfile>();
        Editor bukEditor = Editor.CreateEditor(buk, editorType);
        try
        {
            Play(first);
            string previousPath = PreviewPath(first);
            Play(bukEditor);
            Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(previousPath), Is.Null);
            editorType.GetField("previewEnd", InstanceFlags).SetValue(first, -1d);
            Call(first, "FinishPreviewWhenElapsed");
            UnityEngine.Object.DestroyImmediate(first);
            Assert.That(ActivePreview, Is.SameAs(bukEditor));
            Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(PreviewPath(bukEditor)), Is.Not.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bukEditor);
            UnityEngine.Object.DestroyImmediate(buk);
        }
    }

    private string PreviewPath(Editor editor)
    {
        return (string)editorType.GetField("previewAssetPath", InstanceFlags).GetValue(editor);
    }

    private void Call(Editor editor, string method, params object[] arguments)
    {
        editorType.GetMethod(method, InstanceFlags).Invoke(editor, arguments);
    }
}
