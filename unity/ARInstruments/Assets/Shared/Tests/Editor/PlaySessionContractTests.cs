using System;
using NUnit.Framework;

public sealed class PlaySessionContractTests
{
    [SetUp]
    public void SetUp()
    {
        GameEventBus.PublishSessionRunningChanged(false);
        GameEventBus.PublishInstrumentSelected(InstrumentType.Janggu);
    }

    [Test]
    public void PublishRunSessionRequestAndResult_DeliversToSubscribers()
    {
        RunSessionRequest receivedRequest = default;
        RunSessionResult receivedResult = default;
        int requestCount = 0;
        int resultCount = 0;

        void OnRequested(RunSessionRequest req)
        {
            requestCount++;
            receivedRequest = req;
        }

        void OnCompleted(RunSessionResult res)
        {
            resultCount++;
            receivedResult = res;
        }

        GameEventBus.OnRunSessionRequested += OnRequested;
        GameEventBus.OnRunSessionCompleted += OnCompleted;
        try
        {
            GameEventBus.PublishRunSessionRequest(new RunSessionRequest(42, true));
            GameEventBus.PublishRunSessionCompleted(new RunSessionResult(42, false, "Camera missing"));

            Assert.That(requestCount, Is.EqualTo(1));
            Assert.That(receivedRequest.RequestId, Is.EqualTo(42));
            Assert.That(receivedRequest.Run, Is.True);

            Assert.That(resultCount, Is.EqualTo(1));
            Assert.That(receivedResult.RequestId, Is.EqualTo(42));
            Assert.That(receivedResult.Succeeded, Is.False);
            Assert.That(receivedResult.ErrorMessage, Is.EqualTo("Camera missing"));
        }
        finally
        {
            GameEventBus.OnRunSessionRequested -= OnRequested;
            GameEventBus.OnRunSessionCompleted -= OnCompleted;
        }
    }

    [Test]
    public void PublishSessionRunningAndInstrument_UpdatesCachedStateAndNotifies()
    {
        bool? observedRunning = null;
        InstrumentType? observedInstrument = null;

        void OnRunning(bool running) => observedRunning = running;
        void OnInstrument(InstrumentType instrument) => observedInstrument = instrument;

        GameEventBus.OnSessionRunningChanged += OnRunning;
        GameEventBus.OnInstrumentSelected += OnInstrument;
        try
        {
            GameEventBus.PublishSessionRunningChanged(true);
            Assert.That(GameEventBus.IsSessionRunning, Is.True);
            Assert.That(observedRunning, Is.True);

            GameEventBus.PublishInstrumentSelected(InstrumentType.Buk);
            Assert.That(GameEventBus.SelectedInstrument, Is.EqualTo(InstrumentType.Buk));
            Assert.That(observedInstrument, Is.EqualTo(InstrumentType.Buk));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GameEventBus.PublishInstrumentSelected((InstrumentType)99));
        }
        finally
        {
            GameEventBus.OnSessionRunningChanged -= OnRunning;
            GameEventBus.OnInstrumentSelected -= OnInstrument;
            GameEventBus.PublishSessionRunningChanged(false);
            GameEventBus.PublishInstrumentSelected(InstrumentType.Janggu);
        }
    }

    [Test]
    public void PublishCameraDeviceSelected_NormalizesNullCachesNameAndNotifies()
    {
        string observed = null;
        int callCount = 0;
        void OnDevice(string name)
        {
            callCount++;
            observed = name;
        }

        GameEventBus.OnCameraDeviceSelected += OnDevice;
        try
        {
            GameEventBus.PublishCameraDeviceSelected("Iriun Webcam");
            Assert.That(GameEventBus.SelectedCameraDeviceName, Is.EqualTo("Iriun Webcam"));
            Assert.That(observed, Is.EqualTo("Iriun Webcam"));

            GameEventBus.PublishCameraDeviceSelected(null);
            Assert.That(GameEventBus.SelectedCameraDeviceName, Is.EqualTo(string.Empty));
            Assert.That(observed, Is.EqualTo(string.Empty));
            Assert.That(callCount, Is.EqualTo(2));
        }
        finally
        {
            GameEventBus.OnCameraDeviceSelected -= OnDevice;
            GameEventBus.PublishCameraDeviceSelected(string.Empty);
        }
    }

    [Test]
    public void PublishCalibrationReset_NotifiesSubscribers()
    {
        int callCount = 0;
        void OnReset() => callCount++;

        GameEventBus.OnCalibrationReset += OnReset;
        try
        {
            GameEventBus.PublishCalibrationReset();
            Assert.That(callCount, Is.EqualTo(1));
        }
        finally
        {
            GameEventBus.OnCalibrationReset -= OnReset;
        }
    }
}
