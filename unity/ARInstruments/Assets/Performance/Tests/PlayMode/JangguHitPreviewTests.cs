#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class JangguHitPreviewTests
{
    private const string ScenePath = "Assets/Performance/Scenes/JangguHitPreview.unity";

    [UnityTest]
    public IEnumerator ScenarioButtons_MoveSelectedMalletAndReportExpectedHits()
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);

        yield return VerifyScenario(scene, "Hit and stay behind", 0, 1);
        yield return VerifyScenario(scene, "Lift and hit again", 0, 2);
        yield return VerifyScenario(scene, "Cross outside pad", 0, 0);
        yield return VerifyScenario(scene, "Stop before face", 0, 0);
        yield return VerifyScenario(scene, "Hit and stay behind", 1, 1);
        yield return VerifyScenario(scene, "Lift and hit again", 1, 2);
        yield return VerifyScenario(scene, "Cross outside pad", 1, 0);
        yield return VerifyScenario(scene, "Stop before face", 1, 0);
    }

    [UnityTest]
    public IEnumerator RotatingJangguHalfTurn_SwitchesStrikeToFaceB()
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        Slider rotation = FindComponent<Slider>(scene, "Rotation Y Slider");
        rotation.value = 180f;
        yield return null;

        yield return VerifyScenario(scene, "Hit and stay behind", 1, 1, "B");
        Text face = FindComponent<Text>(scene, "Face B Indicator");
        Assert.That(face.text, Is.EqualTo("B - R"));
    }

    private static IEnumerator LoadPreview()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
    }

    private static IEnumerator VerifyScenario(Scene scene, string scenario, int malletId, int expectedHits, string face = "A")
    {
        string selectedSide = malletId == 0 ? "Left" : "Right";
        string otherSide = malletId == 0 ? "Right" : "Left";
        RectTransform selected = FindComponent<RectTransform>(scene, selectedSide + " Mallet");
        RectTransform other = FindComponent<RectTransform>(scene, otherSide + " Mallet");
        Button button = FindComponent<Button>(scene, scenario + " - " + selectedSide);
        Vector2 selectedStart = selected.anchoredPosition;
        Vector2 otherStart = other.anchoredPosition;
        var hits = new List<HitEvent>();

        GameEventBus.OnValidHit += hits.Add;
        try
        {
            button.onClick.Invoke();
            Assert.That(Vector2.Distance(selected.anchoredPosition, selectedStart), Is.GreaterThan(1f), button.name);

            double deadline = Time.realtimeSinceStartupAsDouble + 5.0;
            while (Vector2.Distance(selected.anchoredPosition, selectedStart) > 0.1f &&
                Time.realtimeSinceStartupAsDouble < deadline)
            {
                Assert.That(Vector2.Distance(other.anchoredPosition, otherStart), Is.LessThan(0.1f), button.name);
                yield return null;
            }

            Assert.That(Vector2.Distance(selected.anchoredPosition, selectedStart), Is.LessThanOrEqualTo(0.1f),
                $"The selected mallet did not return after '{button.name}'.");
            Assert.That(hits, Has.Count.EqualTo(expectedHits), button.name);
            foreach (HitEvent hit in hits)
            {
                Assert.That(hit.MalletId, Is.EqualTo(malletId), button.name);
                Assert.That(hit.InstrumentId, Is.EqualTo(face == "A" ? "janggu_head_a_inner" : "janggu_head_b_inner"), button.name);
            }
        }
        finally
        {
            GameEventBus.OnValidHit -= hits.Add;
        }
    }

    [UnityTearDown]
    public IEnumerator UnloadPreview()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static T FindComponent<T>(Scene scene, string objectName) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (T component in root.GetComponentsInChildren<T>())
            {
                if (component.name == objectName)
                {
                    return component;
                }
            }
        }

        Assert.Fail($"Missing {typeof(T).Name} on '{objectName}' in {scene.name}.");
        return null;
    }
}
#endif
