#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class JangguHitPreviewTests
{
    private const string ScenePath = "Assets/Performance/Scenes/JangguHitPreview.unity";

    [UnityTest]
    public IEnumerator DraggingMallet_UsesSharedHandlersAndLeavesOtherMalletStill()
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        var preview = FindComponent<JangguHitPreview>(scene, "Preview Mallets");
        RectTransform left = FindComponent<RectTransform>(scene, "Left Mallet");
        RectTransform right = FindComponent<RectTransform>(scene, "Right Mallet");
        Vector2 leftStart = left.anchoredPosition;
        Vector2 rightStart = right.anchoredPosition;
        Vector2 screenStart = RectTransformUtility.WorldToScreenPoint(null, left.position);
        var pointer = new PointerEventData(EventSystem.current)
        {
            pointerId = 7,
            button = PointerEventData.InputButton.Left,
            pressPosition = screenStart,
            position = screenStart,
            pointerPressRaycast = new RaycastResult { gameObject = left.gameObject }
        };

        ((IBeginDragHandler)preview).OnBeginDrag(pointer);
        pointer.position += new Vector2(80f, 40f);
        ((IDragHandler)preview).OnDrag(pointer);
        yield return null;
        Assert.That(Vector2.Distance(left.anchoredPosition, leftStart), Is.GreaterThan(1f));
        Assert.That(Vector2.Distance(right.anchoredPosition, rightStart), Is.LessThan(0.1f));

        ((IEndDragHandler)preview).OnEndDrag(pointer);
        Vector2 releasedPosition = left.anchoredPosition;
        pointer.position += new Vector2(80f, 40f);
        ((IDragHandler)preview).OnDrag(pointer);
        yield return null;
        Assert.That(Vector2.Distance(left.anchoredPosition, releasedPosition), Is.LessThan(0.1f));
    }

    [UnityTest]
    public IEnumerator DisableDuringScenario_ReenableRestoresSingleHitAndSound()
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        var preview = FindComponent<JangguHitPreview>(scene, "Preview Mallets");
        FindComponent<Button>(scene, "Lift and hit again - Left").onClick.Invoke();
        yield return null;
        preview.enabled = false;
        yield return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>())
            {
                Assert.That(child.name.EndsWith(" Strike Surface"), Is.False,
                    "Disabling the preview must remove its generated strike surfaces.");
            }
        }

        preview.enabled = true;
        yield return null;
        var audio = FindComponent<JangguAudioPreview>(scene, "Janggu Audio Preview");
        audio.Stop();
        for (int sample = 0; sample < JangguSoundProfile.SampleCount; sample++)
        {
            audio.SetGain(sample, 1f);
            audio.SetPitch(sample, 1f);
        }
        yield return VerifyScenario(scene, "Hit and stay behind", 0, 1);
        Assert.That(CountPlayingSources(scene, 1f), Is.EqualTo(1),
            "Re-enabling must not leave duplicate hit subscriptions or a running scenario.");
    }

    [UnityTest]
    public IEnumerator ActualHeadPositions_InwardStrikesHitOnce_WithdrawalStaysSilent()
    {
        yield return VerifyActualHeadPositions(false);
    }

    [UnityTest]
    public IEnumerator BukHeadPositions_InwardStrikesHitOnce_WithdrawalStaysSilent()
    {
        yield return VerifyActualHeadPositions(true);
    }

    private static IEnumerator VerifyActualHeadPositions(bool useBuk)
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        var audio = FindComponent<JangguAudioPreview>(scene, "Janggu Audio Preview");
        for (int sample = 0; sample < JangguSoundProfile.SampleCount; sample++)
        {
            audio.SetGain(sample, 1f);
            audio.SetPitch(sample, 1f);
        }
        Transform headRoot = null;
        if (useBuk)
        {
            JangguHitPreview preview = FindComponent<JangguHitPreview>(scene, "Preview Mallets");
            preview.enabled = false;
            // Allow the old strike surfaces to be destroyed before looking up the new ones.
            yield return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Presentation/Prefabs/Buk.prefab");
            GameObject buk = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(buk, scene);
            typeof(JangguHitPreview).GetField("janggu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(preview, buk.transform);
            preview.enabled = true;
            headRoot = buk.transform;
        }
        foreach (float rotation in new[] { 0f, 180f })
        {
            // Measure the rest geometry after the preceding hits' visual vibration finishes.
            yield return new WaitForSecondsRealtime(0.2f);
            FindComponent<Slider>(scene, "Rotation Y Slider").value = rotation;
            yield return null;
            MeshFilter headA = headRoot == null ? FindComponent<MeshFilter>(scene, "Head_A_Inner")
                : System.Array.Find(headRoot.GetComponentsInChildren<MeshFilter>(), mesh => mesh.name == "Head_A_Inner");
            MeshFilter headB = headRoot == null ? FindComponent<MeshFilter>(scene, "Head_B_Inner")
                : System.Array.Find(headRoot.GetComponentsInChildren<MeshFilter>(), mesh => mesh.name == "Head_B_Inner");
            Vector3 centerA = headA.transform.TransformPoint(headA.sharedMesh.bounds.center);
            Vector3 centerB = headB.transform.TransformPoint(headB.sharedMesh.bounds.center);
            Vector3 drumCenter = (centerA + centerB) * 0.5f;
            foreach (MeshFilter head in new[] { headA, headB })
            {
                Vector3 faceCenter = head.transform.TransformPoint(head.sharedMesh.bounds.center);
                Vector3 outward = (faceCenter - drumCenter).normalized;
                Transform strike = FindComponent<Transform>(scene, head.name + " Strike Surface");
                Assert.That(Vector3.Dot(strike.forward, outward), Is.GreaterThan(0.99f), head.name);
                Assert.That(Vector3.Dot(strike.position - faceCenter, outward), Is.GreaterThanOrEqualTo(-0.00001f),
                    "The strike plane must lie on the outer side of the mesh.");
                for (int malletId = 0; malletId < 2; malletId++)
                {
                    audio.Stop();
                    var hits = new List<HitEvent>();
                    using (var detector = new HitDetectionCore())
                    {
                        detector.RegisterPad(new InstrumentPad
                        {
                            PadId = head == headA ? "janggu_head_a_inner" : "janggu_head_b_inner",
                            HitSurface = strike,
                            HitBox2D = new Rect(-0.1f, -0.1f, 0.2f, 0.2f)
                        });
                        GameEventBus.OnValidHit += hits.Add;
                        try
                        {
                            // Derive the trajectory from model geometry, never the generated surface normal.
                            Submit(detector, faceCenter - outward * 0.05f, 0, malletId);
                            Submit(detector, faceCenter + outward * 0.04f, 33, malletId);
                            Assert.That(hits, Is.Empty, "An initial withdrawal must stay silent.");
                            Assert.That(CountPlayingSources(scene, 1f), Is.Zero);
                            Submit(detector, faceCenter + outward * 0.08f, 66, malletId);
                            Submit(detector, faceCenter - outward * 0.05f, 99, malletId);
                            Assert.That(hits, Has.Count.EqualTo(1), head.name);
                            Assert.That(hits[0].MalletId, Is.EqualTo(malletId));
                            Assert.That(hits[0].InstrumentId,
                                Is.EqualTo(head == headA ? "janggu_head_a_inner" : "janggu_head_b_inner"));
                            Assert.That(CountPlayingSources(scene, 1f), Is.EqualTo(1), "A strike must play once.");
                            Submit(detector, faceCenter - outward * 0.08f, 132, malletId);
                            Submit(detector, faceCenter + outward * 0.04f, 165, malletId);
                            Submit(detector, faceCenter - outward * 0.05f, 198, malletId);
                            Assert.That(hits, Has.Count.EqualTo(1), "Contact jitter must not replay audio.");
                            Submit(detector, faceCenter + outward * 0.34f, 231, malletId);
                            Assert.That(hits, Has.Count.EqualTo(1), "Lifting must stay silent.");
                            Assert.That(CountPlayingSources(scene, 1f), Is.EqualTo(1), "Withdrawal must not start another sound.");
                            Submit(detector, faceCenter - outward * 0.05f, 264, malletId);
                            Assert.That(hits, Has.Count.EqualTo(2), "A new strike after lifting must hit once.");
                            Assert.That(CountPlayingSources(scene, 1f), Is.EqualTo(2));
                        }
                        finally { GameEventBus.OnValidHit -= hits.Add; }
                    }
                }
            }
        }
    }

    private static void Submit(HitDetectionCore detector, Vector3 position, double time, int malletId)
    {
        detector.ProcessMalletState(new MalletState
        {
            Position = position, TimestampMs = time, MalletId = malletId, Confidence = 1f
        });
    }

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

    [UnityTest]
    public IEnumerator ValidHits_PlayBothFacesAndKeepRehitsRinging()
    {
        yield return LoadPreview();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);

        var audio = FindComponent<JangguAudioPreview>(scene, "Janggu Audio Preview");
        for (int sample = 0; sample < JangguSoundProfile.SampleCount; sample++)
        {
            audio.SetGain(sample, 1f);
            audio.SetPitch(sample, sample < JangguSoundProfile.StrengthCount ? 1f : 0.9f);
        }

        yield return VerifyScenario(scene, "Cross outside pad", 0, 0);
        Assert.That(CountPlayingSources(scene, 1f), Is.Zero, "An outside crossing must stay silent.");

        yield return VerifyScenario(scene, "Hit and stay behind", 0, 1);
        Assert.That(CountPlayingSources(scene, 1f), Is.EqualTo(1), "Face A must play Deok.");

        FindComponent<Slider>(scene, "Rotation Y Slider").value = 180f;
        yield return null;
        yield return VerifyScenario(scene, "Lift and hit again", 1, 2, "B");
        Assert.That(CountPlayingSources(scene, 0.9f), Is.EqualTo(2),
            "Both face B hits must use the shared Kung pitch and remain audible together.");
    }

    private static int CountPlayingSources(Scene scene, float pitch)
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var source in root.GetComponentsInChildren<AudioSource>())
            {
                if (source.isPlaying && Mathf.Approximately(source.pitch, pitch))
                {
                    count++;
                }
            }
        }

        return count;
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
