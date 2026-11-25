#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class MainSceneIntegrationTests
{
    private const string MainScenePath = "Assets/Shared/Scenes/Main.unity";
    private Texture2D frameTexture;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (frameTexture != null)
        {
            UnityEngine.Object.Destroy(frameTexture);
            frameTexture = null;
        }

        GameEventBus.PublishSessionRunningChanged(false);
        GameEventBus.PublishInstrumentSelected(InstrumentType.Janggu);
        yield return null;
    }

    [UnityTest]
    public IEnumerator MainScene_EndToEndPipeline_StreamsFramesDetectsHitsPlaysAudioAndVibrates()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            MainScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;

        GameObject janggu = GameObject.Find("Janggu");
        Assert.That(janggu, Is.Not.Null, "Main.unity must contain an active Janggu instance.");
        Transform headAInner = FindChildByName(janggu.transform, "Head_A_Inner");
        Assert.That(headAInner, Is.Not.Null, "Janggu must contain Head_A_Inner.");

        var hitDetector = UnityEngine.Object.FindFirstObjectByType<InstrumentHitDetector>();
        var audioController = UnityEngine.Object.FindFirstObjectByType<InstrumentAudioController>();
        Assert.That(hitDetector, Is.Not.Null);
        Assert.That(audioController, Is.Not.Null);
        Assert.That(hitDetector.ActivePadCount, Is.EqualTo(4));

        frameTexture = new Texture2D(64, 36, TextureFormat.RGBA32, false);
        Guid sessionId = Guid.NewGuid();
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = "primary",
            SessionId = sessionId,
            DeviceName = "IntegrationTestCamera",
            State = CameraInputState.Waiting,
            ChangedAtMs = nowMs
        });
        GameEventBus.PublishCameraInputState(new CameraInputStateData
        {
            CameraId = "primary",
            SessionId = sessionId,
            DeviceName = "IntegrationTestCamera",
            State = CameraInputState.Streaming,
            ChangedAtMs = nowMs + 1.0
        });
        GameEventBus.PublishCameraFrame(new CameraFrameData
        {
            CameraId = "primary",
            SessionId = sessionId,
            Texture = frameTexture,
            ReceivedAtMs = nowMs + 2.0,
            FrameSequence = 1
        });
        yield return null;

        Vector3 restLocalPosition = headAInner.localPosition;
        var hits = new List<HitEvent>();
        GameEventBus.OnValidHit += hits.Add;
        try
        {
            PublishStrikeAcrossHeadAInner(headAInner, malletId: 1, baseTimestampMs: nowMs + 10.0);
        }
        finally
        {
            GameEventBus.OnValidHit -= hits.Add;
        }

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].InstrumentId, Is.EqualTo("janggu_head_a_inner"));
        Assert.That(hits[0].MalletId, Is.EqualTo(1));
        Assert.That(audioController.IsAnyVoicePlaying, Is.True, "Valid hit must start audio playback.");
        Assert.That(UnityEngine.Object.FindFirstObjectByType<JangguOutputLimiter>(), Is.Not.Null,
            "Active AudioListener must have JangguOutputLimiter attached.");
        Assert.That(Vector3.Distance(headAInner.localPosition, restLocalPosition), Is.GreaterThan(0.0005f),
            "HitRegionVibration must displace the struck region.");
    }

    [UnityTest]
    public IEnumerator MainScene_SwitchingInstrumentToBuk_RebuildsPadsAndPlaysBukAudio()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            MainScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;

        var hitDetector = UnityEngine.Object.FindFirstObjectByType<InstrumentHitDetector>();
        var audioController = UnityEngine.Object.FindFirstObjectByType<InstrumentAudioController>();
        Transform instrumentsRoot = GameObject.Find("Instruments").transform;
        GameObject janggu = instrumentsRoot.Find("Janggu").gameObject;
        GameObject buk = instrumentsRoot.Find("Buk").gameObject;

        GameEventBus.PublishInstrumentSelected(InstrumentType.Buk);
        yield return null;

        Assert.That(buk.activeInHierarchy, Is.True, "Buk must become active when selected.");
        Assert.That(janggu.activeInHierarchy, Is.False, "Janggu must become inactive when Buk is selected.");
        Assert.That(hitDetector.ActivePadCount, Is.EqualTo(4));

        Transform bukHeadAInner = FindChildByName(buk.transform, "Head_A_Inner");
        Assert.That(bukHeadAInner, Is.Not.Null);

        var hits = new List<HitEvent>();
        GameEventBus.OnValidHit += hits.Add;
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        try
        {
            PublishStrikeAcrossHeadAInner(bukHeadAInner, malletId: 0, baseTimestampMs: nowMs + 10.0);
        }
        finally
        {
            GameEventBus.OnValidHit -= hits.Add;
        }

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].InstrumentId, Is.EqualTo("buk_head_a_inner"));
        Assert.That(hits[0].MalletId, Is.EqualTo(0));
        Assert.That(audioController.IsAnyVoicePlaying, Is.True, "Buk hit must trigger audio playback.");
    }

    [UnityTest]
    public IEnumerator MainScene_SimultaneousTwoMalletStrike_PlaysBothVoicesAndVibratesBothFaces()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            MainScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;

        GameObject janggu = GameObject.Find("Janggu");
        Transform headA = FindChildByName(janggu.transform, "Head_A_Inner");
        Transform headB = FindChildByName(janggu.transform, "Head_B_Inner");
        Assert.That(headA, Is.Not.Null);
        Assert.That(headB, Is.Not.Null);

        Vector3 restA = headA.localPosition;
        Vector3 restB = headB.localPosition;
        var hits = new List<HitEvent>();
        GameEventBus.OnValidHit += hits.Add;
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        try
        {
            PublishSimultaneousDualStrike(headA, headB, nowMs + 10.0);
        }
        finally
        {
            GameEventBus.OnValidHit -= hits.Add;
        }

        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits.Exists(h => h.InstrumentId == "janggu_head_a_inner" && h.MalletId == 1), Is.True);
        Assert.That(hits.Exists(h => h.InstrumentId == "janggu_head_b_inner" && h.MalletId == 0), Is.True);

        int playingSources = 0;
        foreach (AudioSource src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (src.isPlaying)
            {
                playingSources++;
            }
        }

        Assert.That(playingSources, Is.GreaterThanOrEqualTo(2), "Simultaneous strikes must play independently.");
        Assert.That(Vector3.Distance(headA.localPosition, restA), Is.GreaterThan(0.0005f));
        Assert.That(Vector3.Distance(headB.localPosition, restB), Is.GreaterThan(0.0005f));
    }

    [UnityTest]
    public IEnumerator MainScene_MovingInstrumentPose_HitsNewLocationAndIgnoresOldLocation()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
            MainScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        yield return null;

        GameObject janggu = GameObject.Find("Janggu");
        Transform headA = FindChildByName(janggu.transform, "Head_A_Inner");
        Bounds bounds = headA.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 oldSurfacePoint = headA.TransformPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
        Vector3 oldOutward = headA.TransformDirection(Vector3.right).normalized;

        janggu.transform.position += new Vector3(0.45f, 0f, 0f);
        yield return null;

        var hits = new List<HitEvent>();
        GameEventBus.OnValidHit += hits.Add;
        double nowMs = Time.realtimeSinceStartupAsDouble * 1000.0;
        try
        {
            // Striking old position must miss completely.
            PublishWorldStrike(oldSurfacePoint, oldOutward, malletId: 0, baseTimestampMs: nowMs + 10.0);
            Assert.That(hits, Is.Empty, "Old world position must not register hits after moving instrument.");

            // Striking new position must register 1 hit (exceed 150ms tracking gap for a fresh stroke).
            PublishStrikeAcrossHeadAInner(headA, malletId: 0, baseTimestampMs: nowMs + 250.0);
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0].InstrumentId, Is.EqualTo("janggu_head_a_inner"));
        }
        finally
        {
            GameEventBus.OnValidHit -= hits.Add;
        }
    }

    private static void PublishSimultaneousDualStrike(Transform headA, Transform headB, double baseTimestampMs)
    {
        Bounds boundsA = headA.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 surfaceA = headA.TransformPoint(new Vector3(boundsA.max.x, boundsA.center.y, boundsA.center.z));
        Vector3 outwardA = headA.TransformDirection(Vector3.right).normalized;

        Bounds boundsB = headB.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 surfaceB = headB.TransformPoint(new Vector3(boundsB.min.x, boundsB.center.y, boundsB.center.z));
        Vector3 outwardB = headB.TransformDirection(Vector3.left).normalized;

        long version = GameEventBus.ContextVersion;
        GameEventBus.OnMalletsTracked?.Invoke(
            new MalletState { MalletId = 0, Position = surfaceB + outwardB * 0.25f, Confidence = 1f, TimestampMs = baseTimestampMs, ContextVersion = version },
            new MalletState { MalletId = 1, Position = surfaceA + outwardA * 0.25f, Confidence = 1f, TimestampMs = baseTimestampMs, ContextVersion = version });
        GameEventBus.OnMalletsTracked?.Invoke(
            new MalletState { MalletId = 0, Position = surfaceB - outwardB * 0.08f, Confidence = 1f, TimestampMs = baseTimestampMs + 20.0, ContextVersion = version },
            new MalletState { MalletId = 1, Position = surfaceA - outwardA * 0.08f, Confidence = 1f, TimestampMs = baseTimestampMs + 20.0, ContextVersion = version });
    }

    private static void PublishWorldStrike(Vector3 surfacePoint, Vector3 outward, int malletId, double baseTimestampMs)
    {
        var first = new MalletState
        {
            MalletId = malletId,
            Position = surfacePoint + outward * 0.25f,
            Confidence = 1f,
            TimestampMs = baseTimestampMs,
            ContextVersion = GameEventBus.ContextVersion
        };
        var second = new MalletState
        {
            MalletId = malletId,
            Position = surfacePoint - outward * 0.08f,
            Confidence = 1f,
            TimestampMs = baseTimestampMs + 20.0,
            ContextVersion = GameEventBus.ContextVersion
        };
        var missingFirst = new MalletState { MalletId = 1 - malletId, TimestampMs = baseTimestampMs, ContextVersion = GameEventBus.ContextVersion };
        var missingSecond = new MalletState { MalletId = 1 - malletId, TimestampMs = baseTimestampMs + 20.0, ContextVersion = GameEventBus.ContextVersion };
        if (malletId == 0)
        {
            GameEventBus.OnMalletsTracked?.Invoke(first, missingFirst);
            GameEventBus.OnMalletsTracked?.Invoke(second, missingSecond);
        }
        else
        {
            GameEventBus.OnMalletsTracked?.Invoke(missingFirst, first);
            GameEventBus.OnMalletsTracked?.Invoke(missingSecond, second);
        }
    }

    private static void PublishStrikeAcrossHeadAInner(Transform headAInner, int malletId, double baseTimestampMs)
    {
        Bounds bounds = headAInner.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 surfacePoint = headAInner.TransformPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
        Vector3 outward = headAInner.TransformDirection(Vector3.right).normalized;

        var first = new MalletState
        {
            MalletId = malletId,
            Position = surfacePoint + outward * 0.25f,
            Confidence = 1f,
            TimestampMs = baseTimestampMs,
            ContextVersion = GameEventBus.ContextVersion
        };
        var second = new MalletState
        {
            MalletId = malletId,
            Position = surfacePoint - outward * 0.08f,
            Confidence = 1f,
            TimestampMs = baseTimestampMs + 20.0,
            ContextVersion = GameEventBus.ContextVersion
        };
        var missingFirst = new MalletState
        {
            MalletId = 1 - malletId,
            TimestampMs = baseTimestampMs,
            ContextVersion = GameEventBus.ContextVersion
        };
        var missingSecond = new MalletState
        {
            MalletId = 1 - malletId,
            TimestampMs = baseTimestampMs + 20.0,
            ContextVersion = GameEventBus.ContextVersion
        };

        if (malletId == 0)
        {
            GameEventBus.OnMalletsTracked?.Invoke(first, missingFirst);
            GameEventBus.OnMalletsTracked?.Invoke(second, missingSecond);
        }
        else
        {
            GameEventBus.OnMalletsTracked?.Invoke(missingFirst, first);
            GameEventBus.OnMalletsTracked?.Invoke(missingSecond, second);
        }
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName)
            {
                return child;
            }
        }

        return null;
    }
}
#endif
