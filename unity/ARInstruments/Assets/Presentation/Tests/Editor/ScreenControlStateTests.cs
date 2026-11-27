using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEditor.SceneManagement;

public sealed class ScreenControlStateTests
{
    private static int receivedRequestId;
    [UnityTest]
    public IEnumerator MainSceneButtonsDriveStateAndMenuOpensAboveBar()
    {
        EditorSceneManager.OpenScene("Assets/Shared/Scenes/Main.unity");
        yield return new EnterPlayMode();
        var bar = Object.FindFirstObjectByType<ScreenControlBar>();
        Assert.IsNotNull(bar);
        yield return null;
        Canvas.ForceUpdateCanvases();
        Transform root = bar.transform.Find("ScreenControls/SafeArea");
        var run = root.Find("BottomBar/RunStop").GetComponent<Button>();
        var instrument = root.Find("BottomBar/Instrument").GetComponent<Button>();
        var mode = root.Find("BottomBar/Mode").GetComponent<Button>();
        GameObject popup = root.Find("InstrumentPopup").gameObject;
        Assert.IsFalse(popup.activeSelf);
        instrument.onClick.Invoke();
        Assert.IsTrue(popup.activeSelf);
        RectTransform options = (RectTransform)popup.transform.Find("Options");
        Assert.Greater(options.anchoredPosition.y, 64);
        string[] names = { "장구", "북", "징", "꽹과리" };
        for (int i = 0; i < names.Length; i++) Assert.AreEqual(names[i], options.GetChild(i).name);
        options.GetChild(3).GetComponent<Button>().onClick.Invoke();
        Assert.AreEqual(ScreenInstrument.Kkwaenggwari, bar.State.Instrument);
        Assert.IsFalse(popup.activeSelf);
        mode.onClick.Invoke();
        Assert.AreEqual(ScreenMode.Administrator, bar.State.Mode);
        receivedRequestId = 0;
        bar.State.RunRequested += (id, running) => receivedRequestId = id;
        run.onClick.Invoke();
        Assert.IsFalse(run.interactable);
        Assert.AreEqual("시작 중…", run.GetComponentInChildren<Text>().text);
        bar.State.CompleteRunRequest(receivedRequestId, true);
        Assert.AreEqual("정지", run.GetComponentInChildren<Text>().text);
        Assert.IsTrue(run.interactable);
        yield return new ExitPlayMode();
    }

    [Test]
    public void StartWaitsForAcknowledgementAndIgnoresDoubleClick()
    {
        var state = new ScreenControlState();
        int id = 0, calls = 0;
        state.RunRequested += (request, running) => { id = request; calls++; Assert.IsTrue(running); };
        state.ToggleRun();
        state.ToggleRun();
        Assert.IsFalse(state.IsRunning);
        Assert.IsTrue(state.IsPending);
        Assert.AreEqual(1, calls);
        state.CompleteRunRequest(id, true);
        Assert.IsTrue(state.IsRunning);
        Assert.IsFalse(state.IsPending);
    }

    [Test]
    public void FailedStopKeepsRunningAndSuccessfulRetryStops()
    {
        var state = new ScreenControlState();
        int id = 0;
        state.RunRequested += (request, running) => id = request;
        state.ReportRunning(true);
        state.ToggleRun();
        state.CompleteRunRequest(id, false);
        Assert.IsTrue(state.IsRunning);
        state.ToggleRun();
        state.CompleteRunRequest(id, true);
        Assert.IsFalse(state.IsRunning);
    }

    [Test]
    public void LateAcknowledgementCannotCompleteNewRequest()
    {
        var state = new ScreenControlState();
        int id = 0;
        state.RunRequested += (request, running) => id = request;
        state.ToggleRun();
        int expired = id;
        state.CancelPending("timeout");
        state.ToggleRun();
        state.CompleteRunRequest(expired, true);
        Assert.IsFalse(state.IsRunning);
        Assert.IsTrue(state.IsPending);
        state.CompleteRunRequest(id, true);
        Assert.IsTrue(state.IsRunning);
    }

    [Test]
    public void MissingBackendDoesNotPretendToStart()
    {
        var state = new ScreenControlState();
        state.ToggleRun();
        Assert.IsFalse(state.IsRunning);
        Assert.IsFalse(state.IsPending);
        Assert.IsNotEmpty(state.Message);
    }

    [Test]
    public void ModeAndInstrumentSelectionsRetainIndependentState()
    {
        var state = new ScreenControlState();
        int changes = 0;
        state.InstrumentChanged += _ => changes++;
        state.SelectInstrument(ScreenInstrument.Jing);
        state.ToggleMode();
        Assert.AreEqual(ScreenMode.Administrator, state.Mode);
        state.ToggleMode();
        Assert.AreEqual(ScreenMode.User, state.Mode);
        Assert.AreEqual(ScreenInstrument.Jing, state.Instrument);
        Assert.AreEqual(1, changes);
        Assert.IsFalse(state.IsRunning);
    }

    [Test]
    public void SetModeUpdatesStateIdempotentlyAndRejectsInvalidValues()
    {
        var state = new ScreenControlState();
        int modeChanges = 0;
        ScreenMode latest = state.Mode;
        state.ModeChanged += mode => { modeChanges++; latest = mode; };

        state.SetMode(ScreenMode.User);
        Assert.AreEqual(0, modeChanges);

        state.SetMode(ScreenMode.Administrator);
        Assert.AreEqual(ScreenMode.Administrator, state.Mode);
        Assert.AreEqual(ScreenMode.Administrator, latest);
        Assert.AreEqual(1, modeChanges);

        state.SetMode(ScreenMode.Administrator);
        Assert.AreEqual(1, modeChanges);

        Assert.Throws<System.ArgumentOutOfRangeException>(() => state.SetMode((ScreenMode)99));
    }
}
