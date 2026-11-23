using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class JangguPreviewLimiterTests
{
    private GameObject listenerObject;
    private Scene previewScene;

#if UNITY_EDITOR
    [UnityTest]
    public IEnumerator DisabledPreview_PairButtonStaysSilentUntilReenabled()
    {
        const string scenePath = "Assets/Performance/Scenes/JangguAudioPreview.unity";
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        previewScene = SceneManager.GetSceneByPath(scenePath);
        yield return null;

        JangguAudioPreview preview = null;
        UnityEngine.UI.Button pairButton = null;
        foreach (var root in previewScene.GetRootGameObjects())
        {
            if (preview == null)
            {
                preview = root.GetComponentInChildren<JangguAudioPreview>();
            }

            foreach (var button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                for (int index = 0; index < button.onClick.GetPersistentEventCount(); index++)
                {
                    if (button.onClick.GetPersistentMethodName(index) == nameof(JangguAudioPreview.PlayPair))
                    {
                        pairButton = button;
                        break;
                    }
                }
            }
        }

        Assert.That(preview, Is.Not.Null);
        Assert.That(pairButton, Is.Not.Null);
        var sources = preview.GetComponentsInChildren<AudioSource>();
        Assert.That(sources.Length, Is.GreaterThan(0));
        pairButton.onClick.Invoke();
        Assert.That(System.Array.Exists(sources, source => source.isPlaying), Is.True,
            "The scene button must play audio before disabling the preview.");

        preview.enabled = false;
        yield return null;
        Assert.That(preview.gameObject.activeInHierarchy, Is.True);
        Assert.That(Object.FindObjectsByType<JangguOutputLimiter>(FindObjectsSortMode.None), Is.Empty);
        pairButton.onClick.Invoke();
        preview.PlaySample(0);
        preview.PlayDeong(0);
        Assert.That(System.Array.Exists(preview.GetComponentsInChildren<AudioSource>(), source => source.isPlaying),
            Is.False, "Disabled previews must reject pair, sample, and Deong playback.");
        yield return null;
        Assert.That(System.Array.Exists(preview.GetComponentsInChildren<AudioSource>(), source => source.isPlaying),
            Is.False);

        preview.enabled = true;
        yield return null;
        Assert.That(Object.FindObjectsByType<JangguOutputLimiter>(FindObjectsSortMode.None), Is.Not.Empty);
        pairButton.onClick.Invoke();
        Assert.That(System.Array.Exists(preview.GetComponentsInChildren<AudioSource>(), source => source.isPlaying),
            Is.True, "Reenabling the preview must restore pair playback.");
    }
#endif

    [UnityTest]
    public IEnumerator UnloadingPreview_RemovesItsLimiterFromPersistentListener()
    {
        listenerObject = new GameObject("Main listener", typeof(AudioListener));
        previewScene = SceneManager.CreateScene("Additive preview");
        var first = CreatePreview();
        CreatePreview();
        var limiter = listenerObject.GetComponent<JangguOutputLimiter>();
        Assert.That(limiter != null, Is.True);

        first.enabled = false;
        yield return null;
        Assert.That(listenerObject.GetComponent<JangguOutputLimiter>(), Is.SameAs(limiter));

        yield return SceneManager.UnloadSceneAsync(previewScene);
        yield return null;
        Assert.That(listenerObject != null, Is.True);
        Assert.That(listenerObject.GetComponent<JangguOutputLimiter>() == null, Is.True);
    }

    [UnityTest]
    public IEnumerator ClosingPreview_PreservesAnExistingLimiter()
    {
        listenerObject = new GameObject("Main listener", typeof(AudioListener), typeof(JangguOutputLimiter));
        var existing = listenerObject.GetComponent<JangguOutputLimiter>();
        previewScene = SceneManager.CreateScene("Additive preview");
        CreatePreview();

        yield return SceneManager.UnloadSceneAsync(previewScene);
        yield return null;
        Assert.That(listenerObject.GetComponent<JangguOutputLimiter>(), Is.SameAs(existing));
    }

    private JangguAudioPreview CreatePreview()
    {
        var preview = new GameObject("Janggu preview");
        SceneManager.MoveGameObjectToScene(preview, previewScene);
        return preview.AddComponent<JangguAudioPreview>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (previewScene.IsValid() && previewScene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(previewScene);
        }

        Object.Destroy(listenerObject);
        yield return null;
    }
}
