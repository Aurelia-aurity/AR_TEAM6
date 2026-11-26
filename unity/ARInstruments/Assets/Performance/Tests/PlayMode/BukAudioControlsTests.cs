#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class BukAudioControlsTests
{
    private const string Path = "Assets/Performance/Scenes/BukAudioPreview.unity";
    private static Scene Scene => SceneManager.GetSceneByPath(Path);

    [UnityTest]
    public IEnumerator ComparisonButtons_TuneSessionAndRetainRingingUntilStopped()
    {
        yield return Load();
        var player = Find<InstrumentSamplePlayer>("Buk Audio");
        Assert.That(Find<RectTransform>("Audio Controls").gameObject.activeInHierarchy, Is.True);
        float savedGain = player.SavedProfile.GetGain(0);
        Find<InputField>("Strong Gain Input").onEndEdit.Invoke("2.40");
        Find<Slider>("Strong Pitch Slider").value = 1.2f;
        Assert.That(player.GetGain(0), Is.EqualTo(2.4f));
        Assert.That(player.GetPitch(0), Is.EqualTo(1.2f));
        Assert.That(player.SavedProfile.GetGain(0), Is.EqualTo(savedGain));
        Find<Button>("Strong Play").onClick.Invoke();
        Find<Button>("Strong Play").onClick.Invoke();
        Assert.That(Playing(player), Is.EqualTo(2));
        foreach (AudioSource source in player.GetComponentsInChildren<AudioSource>())
            if (source.isPlaying) Assert.That(source.pitch, Is.EqualTo(1.2f));
        Find<Button>("Stop playback").onClick.Invoke();
        Assert.That(Playing(player), Is.Zero);
        Find<Button>("Strong + Strong").onClick.Invoke();
        Assert.That(Playing(player), Is.EqualTo(2));
        Find<Button>("Stop playback").onClick.Invoke();
        Assert.That(Playing(player), Is.Zero);
        var save = Find<Toggle>("Save tuning on exit");
        Assert.That(save.isOn, Is.False);
        Assert.That(player.KeepSessionTuning, Is.False);
        save.isOn = true;
        Assert.That(player.KeepSessionTuning, Is.True);
        save.isOn = false;
    }

    [UnityTest]
    public IEnumerator SceneButtons_PlayEverySingleAndAllNineFaceCombinations()
    {
        yield return Load();
        var player = Find<InstrumentSamplePlayer>("Buk Audio");
        string[] names = { "Strong", "Medium", "Weak" };
        float[] pitches = { 1.2f, 0.9f, 0.7f };
        for (int sample = 0; sample < 3; sample++) player.SetPitch(sample, pitches[sample]);
        for (int a = 0; a < 3; a++)
        {
            player.Stop();
            Button single = Find<Button>(names[a] + " Play");
            Assert.That(single.onClick.GetPersistentTarget(0), Is.SameAs(player));
            single.onClick.Invoke();
            Assert.That(Playing(player), Is.EqualTo(1));
            foreach (AudioSource source in player.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying) Assert.That(source.pitch, Is.EqualTo(pitches[a]));
            for (int b = 0; b < 3; b++)
            {
                player.Stop();
                Button pair = Find<Button>(names[a] + " + " + names[b]);
                Assert.That(pair.onClick.GetPersistentTarget(0), Is.SameAs(player));
                pair.onClick.Invoke();
                Assert.That(Playing(player), Is.EqualTo(2), pair.name);
                var actual = new System.Collections.Generic.List<float>();
                foreach (AudioSource source in player.GetComponentsInChildren<AudioSource>())
                    if (source.isPlaying) actual.Add(source.pitch);
                Assert.That(actual, Is.EquivalentTo(new[] { pitches[a], pitches[b] }), pair.name);
            }
        }
    }

    [UnityTest]
    public IEnumerator DisabledPlayer_ActualButtonsCannotPlayWithoutLimiter()
    {
        yield return Load();
        var player = Find<InstrumentSamplePlayer>("Buk Audio");
        player.enabled = false;
        yield return null;
        Assert.That(player.GetComponent<JangguOutputLimiter>() == null, Is.True);
        Find<Button>("Strong Play").onClick.Invoke();
        Find<Button>("Strong + Strong").onClick.Invoke();
        player.PlaySample(1);
        Assert.That(Playing(player), Is.Zero);
        player.enabled = true;
        yield return null;
        Find<Button>("Strong Play").onClick.Invoke();
        Assert.That(Playing(player), Is.EqualTo(1));
        Assert.That(player.GetComponent<JangguOutputLimiter>(), Is.Not.Null);
    }

    private static int Playing(InstrumentSamplePlayer player)
    {
        int count = 0;
        foreach (AudioSource source in player.GetComponentsInChildren<AudioSource>())
            if (source.isPlaying) count++;
        return count;
    }

    [UnityTest]
    public IEnumerator ProjectTuning_ActualListenerOutputStaysWithinCeiling()
    {
        yield return Load();
        var player = Find<InstrumentSamplePlayer>("Buk Audio");
        Assert.That(player.GetComponent<JangguOutputLimiter>().enabled, Is.True);
        for (int sample = 0; sample < 3; sample++)
        {
            Assert.That(player.GetGain(sample), Is.EqualTo(3.9f));
            player.Stop();
            player.PlaySample(sample);
            yield return AssertOutputBounded("single " + sample, 0.4f);
        }
        for (int pair = 0; pair < 9; pair++)
        {
            player.Stop();
            player.PlayPair(pair);
            yield return AssertOutputBounded("pair " + pair, 0.4f);
        }
        player.Stop();
        for (int hit = 0; hit < 10; hit++)
        {
            player.PlayPair(0);
            yield return AssertOutputBounded("rapid pair " + hit, 0.1f);
        }
        yield return AssertOutputBounded("rapid pair tail", 0.5f);
    }

    private static IEnumerator AssertOutputBounded(string label, float seconds)
    {
        var output = new float[2048];
        float peak = 0;
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        int channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        do
        {
            yield return null;
            for (int channel = 0; channel < channels; channel++)
            {
                AudioListener.GetOutputData(output, channel);
                foreach (float value in output)
                {
                    Assert.That(float.IsFinite(value), Is.True, label);
                    peak = Mathf.Max(peak, Mathf.Abs(value));
                }
            }
        } while (Time.realtimeSinceStartupAsDouble < end);
        Assert.That(peak, Is.GreaterThan(0.001f), label + " must produce measurable output.");
        Assert.That(peak, Is.LessThanOrEqualTo(JangguPeakLimiter.Ceiling + 0.0001f), label);
        TestContext.Out.WriteLine($"{label}: listener peak={peak:F6}");
    }

    [UnityTest]
    public IEnumerator JangguAndBuk_ShareLimiterUntilLastPlayerCloses()
    {
        yield return Load();
        var buk = Find<InstrumentSamplePlayer>("Buk Audio");
        var limiter = buk.GetComponent<JangguOutputLimiter>();
        Assert.That(limiter, Is.Not.Null);
        var jangguObject = new GameObject("Additional janggu player");
        try
        {
            var janggu = jangguObject.AddComponent<JangguAudioPreview>();
            buk.enabled = false;
            yield return null;
            Assert.That(buk.GetComponent<JangguOutputLimiter>(), Is.SameAs(limiter));
            janggu.enabled = false;
            yield return null;
            Assert.That(buk.GetComponent<JangguOutputLimiter>() == null, Is.True);
        }
        finally { Object.Destroy(jangguObject); }
    }
    private static T Find<T>(string name) where T : Component
    {
        foreach (var root in Scene.GetRootGameObjects())
            foreach (T item in root.GetComponentsInChildren<T>(true))
                if (item.name == name) return item;
        Assert.Fail("Missing: " + name);
        return null;
    }
    private static IEnumerator Load()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Path, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
    }
    [UnityTearDown]
    public IEnumerator Unload()
    {
        if (Scene.IsValid() && Scene.isLoaded) yield return SceneManager.UnloadSceneAsync(Scene);
        yield return null;
    }
}
#endif
