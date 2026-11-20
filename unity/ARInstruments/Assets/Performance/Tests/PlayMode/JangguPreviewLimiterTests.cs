using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class JangguPreviewLimiterTests
{
    private GameObject listenerObject;
    private Scene previewScene;

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
