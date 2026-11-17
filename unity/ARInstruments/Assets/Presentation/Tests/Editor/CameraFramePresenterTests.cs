using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class CameraFramePresenterTests
{
    private GameObject root;
    private CameraDisplayView view;
    private CameraFramePresenter presenter;
    private RawImage image;
    private Texture2D texture;
    private Guid session;
    private double time;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        ResetBus();
        root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Presentation/Prefabs/CameraDisplay.prefab"));
        view = root.GetComponent<CameraDisplayView>();
        image = root.GetComponentInChildren<RawImage>(true);
        texture = new Texture2D(160, 90);
        session = Guid.NewGuid();
        time = 100;
        presenter = root.AddComponent<CameraFramePresenter>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
        if (texture != null) Object.DestroyImmediate(texture);
        ResetBus();
        yield return new ExitPlayMode();
    }

    private static void ResetBus() => typeof(GameEventBus).GetMethod("ResetCameraInput",
        BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    private CameraInputStateData State(CameraInputState state, CameraInputError error = CameraInputError.None)
    {
        return new CameraInputStateData
        {
            CameraId = "primary", SessionId = session, State = state, Error = error,
            DeviceName = "Phone camera", ChangedAtMs = ++time
        };
    }

    private CameraFrameData Frame() => new CameraFrameData
    {
        CameraId = "primary", SessionId = session, Texture = texture,
        ReceivedAtMs = ++time, ClockwiseRotation = 90, VerticallyMirrored = true
    };

    private void StartStreaming()
    {
        GameEventBus.PublishCameraInputState(State(CameraInputState.Waiting));
        GameEventBus.PublishCameraInputState(State(CameraInputState.Streaming));
        GameEventBus.PublishCameraFrame(Frame());
    }

    [Test]
    public void InspectorCameraIdSelectsLogicalSourceAndFramesAloneCannotStartIt()
    {
        presenter.enabled = false;
        var settings = new SerializedObject(presenter);
        settings.FindProperty("cameraId").stringValue = "phone";
        settings.ApplyModifiedPropertiesWithoutUndo();
        presenter.enabled = true;
        StartStreaming();
        Assert.That(image.texture, Is.Null);
        var frame = Frame();
        frame.CameraId = "phone";
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(image.texture, Is.Null, "An unknown session must not start via a frame alone.");
        var state = State(CameraInputState.Streaming);
        state.CameraId = "phone";
        GameEventBus.PublishCameraInputState(state);
        frame.ReceivedAtMs = ++time;
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(image.texture, Is.SameAs(texture));
    }

    [Test]
    public void FrameUsesViewOrientationAspectAndMirrorWithoutAnyMalletEvents()
    {
        view.SetMirrorMode(true);
        GameEventBus.PublishCameraInputState(State(CameraInputState.Waiting));
        GameEventBus.PublishCameraInputState(State(CameraInputState.Streaming));
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting));
        Assert.That(image.texture, Is.Null);
        GameEventBus.PublishCameraFrame(Frame());
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Assert.That(image.texture, Is.SameAs(texture));
        Assert.That(view.VideoAspectRatio, Is.EqualTo(90f / 160f).Within(0.0001f));
        Assert.That(image.uvRect, Is.EqualTo(new Rect(0, 1, 1, -1)));
        Assert.That(Quaternion.Angle(image.rectTransform.localRotation, Quaternion.Euler(0, 0, -90)), Is.LessThan(0.01f));
        Assert.That(view.MirrorEnabled, Is.True);
    }

    [TestCase(CameraInputState.Waiting)]
    [TestCase(CameraInputState.Stopped)]
    [TestCase(CameraInputState.Error)]
    public void InputInvalidationClearsImageAndOverlaysSynchronously(CameraInputState state)
    {
        StartStreaming();
        int invalidations = 0;
        view.InputInvalidated += () => invalidations++;
        if (state == CameraInputState.Waiting) session = Guid.NewGuid();
        GameEventBus.PublishCameraInputState(State(state, state == CameraInputState.Error
            ? CameraInputError.FrameTimeout : CameraInputError.None));
        Assert.That(image.texture, Is.Null);
        Assert.That(image.enabled, Is.False);
        Assert.That(root.transform.Find("PreviewFrame/VideoViewport/MirrorRoot").gameObject.activeSelf, Is.False);
        Assert.That(invalidations, Is.EqualTo(1));
        Assert.That(view.Status, Is.EqualTo(state == CameraInputState.Error
            ? CameraDisplayStatus.Error : CameraDisplayStatus.Waiting));
        GameEventBus.PublishCameraFrame(Frame());
        Assert.That(image.texture, Is.Null, "Frames cannot revive a non-streaming state.");
        Assert.That(texture.width, Is.EqualTo(160), "B must not destroy A's texture.");
    }

    [Test]
    public void RestartRejectsOldSessionAndRequiresNewFrameWhileKeepingMirrorSelection()
    {
        StartStreaming();
        view.SetMirrorMode(true);
        var oldFrame = Frame();
        var oldState = State(CameraInputState.Error, CameraInputError.FrameTimeout);
        GameEventBus.PublishCameraInputState(oldState);
        GameEventBus.PublishCameraInputState(State(CameraInputState.Streaming));
        GameEventBus.PublishCameraFrame(Frame());
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error), "Restart requires a new session.");

        session = Guid.NewGuid();
        GameEventBus.PublishCameraInputState(State(CameraInputState.Waiting));
        GameEventBus.PublishCameraInputState(State(CameraInputState.Streaming));
        Assert.That(image.texture, Is.Null);
        oldFrame.ReceivedAtMs = ++time;
        GameEventBus.PublishCameraFrame(oldFrame);
        oldState.ChangedAtMs = ++time;
        GameEventBus.PublishCameraInputState(oldState);
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting));
        GameEventBus.PublishCameraFrame(Frame());
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
        Assert.That(view.MirrorEnabled, Is.True);
    }

    [Test]
    public void OtherCameraAndOldOrInvalidTimestampsDoNotReplaceCurrentVideo()
    {
        StartStreaming();
        var error = State(CameraInputState.Error);
        error.CameraId = "secondary";
        GameEventBus.PublishCameraInputState(error);
        error.CameraId = "primary";
        error.ChangedAtMs = 0;
        GameEventBus.PublishCameraInputState(error);
        error.ChangedAtMs = double.NaN;
        GameEventBus.PublishCameraInputState(error);
        var frame = Frame();
        frame.Texture = null;
        frame.CameraId = "secondary";
        GameEventBus.PublishCameraFrame(frame);
        frame.CameraId = "primary";
        frame.ReceivedAtMs = 0;
        GameEventBus.PublishCameraFrame(frame);
        frame.ReceivedAtMs = double.PositiveInfinity;
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(image.texture, Is.SameAs(texture));
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Streaming));
    }

    [Test]
    public void DuplicateFrameIsIgnoredButNewInvalidInputClearsPreviousImage()
    {
        StartStreaming();
        var frame = Frame();
        GameEventBus.PublishCameraFrame(frame);
        frame.Texture = null;
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(image.texture, Is.SameAs(texture));
        frame.ReceivedAtMs = ++time;
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(image.texture, Is.Null);
        frame = Frame();
        GameEventBus.PublishCameraFrame(frame);
        frame.ReceivedAtMs = ++time;
        frame.ClockwiseRotation = 45;
        GameEventBus.PublishCameraFrame(frame);
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Error));
        Assert.That(image.texture, Is.Null);
    }

    [TestCase(CameraInputState.Waiting)]
    [TestCase(CameraInputState.Streaming)]
    [TestCase(CameraInputState.Error)]
    [TestCase(CameraInputState.Stopped)]
    public void ReenableQueriesLatestStateWithoutReplayingTexture(CameraInputState state)
    {
        StartStreaming();
        presenter.enabled = false;
        Assert.That(image.texture, Is.Null);
        session = Guid.NewGuid();
        GameEventBus.PublishCameraInputState(State(state, CameraInputError.FrameTimeout));
        GameEventBus.PublishCameraFrame(Frame());
        Assert.That(view.Status, Is.EqualTo(CameraDisplayStatus.Waiting), "Disabled receivers must unsubscribe.");
        presenter.enabled = true;
        Assert.That(view.Status, Is.EqualTo(state == CameraInputState.Error
            ? CameraDisplayStatus.Error : CameraDisplayStatus.Waiting));
        Assert.That(image.texture, Is.Null);
        if (state == CameraInputState.Streaming)
        {
            GameEventBus.PublishCameraFrame(Frame());
            Assert.That(image.texture, Is.SameAs(texture));
        }
    }

    [TestCase(CameraInputError.DeviceNotFound, "not found")]
    [TestCase(CameraInputError.StartFailed, "could not start")]
    [TestCase(CameraInputError.FrameTimeout, "stopped arriving")]
    public void ErrorCodesProvideGuidanceAndDescriptionCanOverrideIt(CameraInputError error, string expected)
    {
        GameEventBus.PublishCameraInputState(State(CameraInputState.Error, error));
        var detail = root.transform.Find("Footer/StatusDetail").GetComponent<Text>();
        Assert.That(detail.text, Does.Contain(expected));
        var state = State(CameraInputState.Error, error);
        state.Description = "Phone disconnected.";
        GameEventBus.PublishCameraInputState(state);
        Assert.That(detail.text, Is.EqualTo("Phone disconnected."));
    }
}
