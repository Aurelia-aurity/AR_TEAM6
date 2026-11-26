#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class BukHitPreviewTests
{
    private const string ScenePath = "Assets/Performance/Scenes/BukHitPreview.unity";

    [UnityTest]
    public IEnumerator BothFaces_SelectSameTunedStrengthSamples_WithdrawalDoesNotReplay()
    {
        yield return Load();
        var audio = Find<InstrumentSamplePlayer>("Buk Audio Preview");
        audio.SetPitch(0, 1.2f);
        audio.SetPitch(1, 0.9f);
        audio.SetPitch(2, 0.7f);
        MeshFilter a = Find<MeshFilter>("Head_A_Inner");
        MeshFilter b = Find<MeshFilter>("Head_B_Inner");
        Vector3 centerA = a.transform.TransformPoint(a.sharedMesh.bounds.center);
        Vector3 centerB = b.transform.TransformPoint(b.sharedMesh.bounds.center);
        Vector3 center = (centerA + centerB) * 0.5f;
        foreach (bool faceA in new[] { true, false })
        {
            string region = faceA ? "buk_head_a_inner" : "buk_head_b_inner";
            Vector3 point = faceA ? centerA : centerB;
            Vector3 outward = (point - center).normalized;
            Transform surface = Find<Transform>(faceA ? "Head_A_Inner Strike Surface" : "Head_B_Inner Strike Surface");
            for (int strength = 0; strength < 3; strength++)
            {
                audio.Stop();
                using (var core = new HitDetectionCore())
                {
                    core.RegisterPad(new InstrumentPad
                    {
                        PadId = region, HitSurface = surface, HitBox2D = new Rect(-0.1f, -0.1f, 0.2f, 0.2f)
                    });
                    float depth = strength == 0 ? 0.4f : strength == 1 ? 0.15f : 0.05f;
                    core.ProcessMalletState(Sample(point + outward * depth, 0, 0));
                    core.ProcessMalletState(Sample(point - outward * 0.05f, 33, 0));
                    Assert.That(Playing(audio), Is.EqualTo(1), region);
                    foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
                        if (source.isPlaying) Assert.That(source.pitch, Is.EqualTo(audio.GetPitch(strength)), region);
                    core.ProcessMalletState(Sample(point + outward * 0.4f, 66, 0));
                    Assert.That(Playing(audio), Is.EqualTo(1), "Withdrawal must not start another sound.");
                }
            }
        }
    }

    [UnityTest]
    public IEnumerator TwoTrackedMallets_PlayTogether_AndDisabledComponentsStaySilent()
    {
        yield return Load();
        var audio = Find<InstrumentSamplePlayer>("Buk Audio Preview");
        var preview = Find<BukHitPreview>("Preview Mallets");
        MeshFilter a = Find<MeshFilter>("Head_A_Inner");
        MeshFilter b = Find<MeshFilter>("Head_B_Inner");
        Vector3 centerA = a.transform.TransformPoint(a.sharedMesh.bounds.center);
        Vector3 centerB = b.transform.TransformPoint(b.sharedMesh.bounds.center);
        Vector3 outward = (centerA - centerB).normalized;
        audio.SetPitch(1, 0.85f);
        GameEventBus.OnMalletsTracked?.Invoke(Sample(centerA + outward * 0.15f, 0, 0), Sample(centerB - outward * 0.15f, 0, 1));
        GameEventBus.OnMalletsTracked?.Invoke(Sample(centerA - outward * 0.05f, 33, 0), Sample(centerB + outward * 0.05f, 33, 1));
        Assert.That(Playing(audio), Is.EqualTo(2));
        foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
            if (source.isPlaying) Assert.That(source.pitch, Is.EqualTo(0.85f));
        audio.Stop();
        preview.enabled = false;
        yield return null;
        var hit = new HitEvent { ContextVersion = GameEventBus.ContextVersion, InstrumentId = "buk_head_a_inner", HitForce = 1f };
        GameEventBus.OnValidHit?.Invoke(hit);
        Assert.That(Playing(audio), Is.Zero, "A disabled hit preview must unsubscribe its sound routing.");
        preview.enabled = true;
        yield return null;
        GameEventBus.OnValidHit?.Invoke(hit);
        Assert.That(Playing(audio), Is.EqualTo(1), "Reenable must restore exactly one subscription.");
        audio.enabled = false;
        yield return null;
        GameEventBus.OnValidHit?.Invoke(hit);
        Assert.That(Playing(audio), Is.Zero, "Hit events must not bypass a disabled sample player.");
    }

    private static MalletState Sample(Vector3 point, double time, int mallet) => new MalletState
    {
        ContextVersion = GameEventBus.ContextVersion, Position = point, TimestampMs = time, Confidence = 1f, MalletId = mallet
    };

    private static int Playing(InstrumentSamplePlayer audio)
    {
        int playing = 0;
        foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
            if (source.isPlaying) playing++;
        return playing;
    }

    [UnityTest]
    public IEnumerator PoseAndScaleControls_ResetAndRetainWorkingStrikeSurfaces()
    {
        yield return Load();
        AssertFaceLabelsInView();
        Transform model = Find<Transform>("Buk");
        Find<Slider>("Position X Slider").value = 0.1f;
        Find<Slider>("Position Y Slider").value = 0.05f;
        Find<Slider>("Position Z Slider").value = -0.1f;
        Find<Slider>("Rotation Y Slider").value = 35f;
        Find<Slider>("Scale Slider").value = 0.5f;
        yield return null;
        Assert.That(Vector3.Distance(model.localPosition, new Vector3(0.1f, 0.05f, -0.1f)), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(model.localRotation, Quaternion.Euler(0f, 35f, 0f)), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(model.localScale, Vector3.one * 0.5f), Is.LessThan(0.0001f));
        Assert.That(CountSurfaces(model), Is.EqualTo(2));
        AssertFaceLabelsInView();
        yield return Scenario("Hit and stay behind", 0, 1, "buk_head_a_inner");
        Find<Slider>("Scale Slider").value = 1.5f;
        yield return null;
        Assert.That(CountSurfaces(model), Is.EqualTo(2));
        yield return Scenario("Lift and hit again", 1, 2, "buk_head_a_inner");
        Find<Button>("Reset pose").onClick.Invoke();
        yield return null;
        Assert.That(Vector3.Distance(model.localPosition, Vector3.zero), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(model.localRotation, Quaternion.identity), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(model.localScale, Vector3.one), Is.LessThan(0.0001f));
        Assert.That(Find<Slider>("Scale Slider").value, Is.EqualTo(1f));
        Assert.That(CountSurfaces(model), Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator BothFacesAndMallets_RunScenariosWithSharedAudio()
    {
        yield return Load();
        for (int face = 0; face < 2; face++)
        {
            string region = face == 0 ? "buk_head_a_inner" : "buk_head_b_inner";
            for (int mallet = 0; mallet < 2; mallet++)
            {
                yield return Scenario("Hit and stay behind", mallet, 1, region);
                yield return Scenario("Lift and hit again", mallet, 2, region);
                yield return Scenario("Cross outside pad", mallet, 0, region);
                yield return Scenario("Stop before face", mallet, 0, region);
            }
            Find<Button>("Show other face").onClick.Invoke();
            yield return null;
            AssertFaceLabelsInView();
        }
        Assert.That(Find<Text>("Audio Notice").text, Is.EqualTo("A/B share the same sound settings"));
    }

    private static IEnumerator Scenario(string name, int mallet, int expected, string region)
    {
        var hits = new List<HitEvent>();
        var audio = Find<InstrumentSamplePlayer>("Buk Audio Preview");
        audio.Stop();
        GameEventBus.OnValidHit += hits.Add;
        try
        {
            Find<Button>(name + (mallet == 0 ? " - Left" : " - Right")).onClick.Invoke();
            var preview = Find<BukHitPreview>("Preview Mallets");
            double deadline = Time.realtimeSinceStartupAsDouble + 6.0;
            while (preview.IsScenarioPlaying && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(preview.IsScenarioPlaying, Is.False, "The scenario must finish and return the mallet.");
            Assert.That(hits, Has.Count.EqualTo(expected), name);
            int playing = 0;
            foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying) playing++;
            Assert.That(playing, Is.EqualTo(expected), name + " must play exactly once per hit.");
            foreach (HitEvent hit in hits)
            {
                Assert.That(hit.MalletId, Is.EqualTo(mallet));
                Assert.That(hit.InstrumentId, Is.EqualTo(region));
            }
        }
        finally { GameEventBus.OnValidHit -= hits.Add; }
    }

    private static int CountSurfaces(Transform model)
    {
        int count = 0;
        foreach (Transform child in model.GetComponentsInChildren<Transform>())
            if (child.name.EndsWith(" Strike Surface")) count++;
        return count;
    }

    private static void AssertFaceLabelsInView()
    {
        foreach (string name in new[] { "Face A Indicator", "Face B Indicator" })
        {
            RectTransform label = Find<Text>(name).rectTransform;
            RectTransform parent = (RectTransform)label.parent;
            Assert.That(parent.rect.Contains(parent.InverseTransformPoint(label.position)), Is.True,
                name + " must remain within the preview canvas.");
        }
    }

    private static Scene Scene => SceneManager.GetSceneByPath(ScenePath);

    private static T Find<T>(string name) where T : Component
    {
        foreach (GameObject root in Scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>())
                if (component.name == name) return component;
        Assert.Fail($"Missing {typeof(T).Name}: {name}");
        return null;
    }

    private static IEnumerator Load()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Unload()
    {
        if (Scene.IsValid() && Scene.isLoaded) yield return SceneManager.UnloadSceneAsync(Scene);
    }
}
#endif
