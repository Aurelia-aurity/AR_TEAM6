using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class HitRegionVibrationTests
{
    private const string PrefabPath = "Assets/Presentation/Prefabs/Janggu.prefab";
    private const string ScenePath = "Assets/Presentation/Scenes/JangguPlacementPreview.unity";

    [Serializable] private sealed class Metadata { public Node[] nodes; }
    [Serializable] private sealed class Node { public string name; public Extras extras; }
    [Serializable] private sealed class Extras { public string zone_id; }

    [Test]
    public void PrefabBindingsUseActualGlbZoneIdsAndOutwardNormals()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var effect = prefab.GetComponent<HitRegionVibration>();
        Assert.That(effect, Is.Not.Null);
        byte[] glb = File.ReadAllBytes("Assets/Models/Janggu/janggu.glb");
        var metadata = JsonUtility.FromJson<Metadata>(Encoding.UTF8.GetString(glb, 20, BitConverter.ToInt32(glb, 12)));
        var zones = metadata.nodes.Where(n => !string.IsNullOrEmpty(n.extras?.zone_id)).ToArray();
        var bindings = Bindings(effect);
        Assert.That(bindings.Count, Is.EqualTo(4));
        Assert.That(bindings.Keys, Is.EquivalentTo(zones.Select(n => n.extras.zone_id)));
        var settings = new SerializedObject(effect).FindProperty("regions");
        foreach (Node zone in zones)
        {
            Transform target = bindings[zone.extras.zone_id];
            Assert.That(target.name, Is.EqualTo(zone.name));
            Assert.That(target.IsChildOf(prefab.transform), Is.True);
        }
        for (int i = 0; i < settings.arraySize; i++)
        {
            var binding = settings.GetArrayElementAtIndex(i);
            var target = (Transform)binding.FindPropertyRelative("Target").objectReferenceValue;
            Vector3 normal = binding.FindPropertyRelative("LocalNormal").vector3Value;
            // Imported GLB meshes face along X; this also catches import handedness mistakes.
            Assert.That(normal, Is.EqualTo(Vector3.right * Mathf.Sign(target.GetComponent<MeshFilter>().sharedMesh.bounds.center.x)));
        }
    }

    [UnityTest]
    public IEnumerator HitsMoveOnlyMatchingRegionByOneToThreeMillimetresAndRestoreExactly()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        var effect = Object.FindFirstObjectByType<HitRegionVibration>();
        var bindings = Bindings(effect);
        var rest = bindings.ToDictionary(pair => pair.Key, pair => pair.Value.localPosition);
        foreach (var pair in bindings)
        {
            foreach (float force in new[] { 0f, 0.5f, 1f })
            {
                Vector3 before = pair.Value.position;
                Publish(pair.Key, force);
                Assert.That(Vector3.Distance(pair.Value.position, before),
                    Is.EqualTo(Mathf.Lerp(0.001f, 0.003f, force)).Within(0.000001f));
                foreach (var other in bindings.Where(p => p.Key != pair.Key))
                    Assert.That(other.Value.localPosition, Is.EqualTo(rest[other.Key]));
                yield return new WaitForSecondsRealtime(0.22f);
                Assert.That(pair.Value.localPosition, Is.EqualTo(rest[pair.Key]), "The final pose must be exact, not a lerp approximation.");
            }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DecayPreservesWorldMillimetresAndSurfaceNormalUnderScaleAndRotation()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        var effect = Object.FindFirstObjectByType<HitRegionVibration>();
        var pair = Bindings(effect).First();
        Transform target = pair.Value;
        Vector3 rest = target.localPosition;
        effect.transform.localScale = new Vector3(2, 0.7f, 1.3f);
        effect.transform.rotation = Quaternion.Euler(21, 37, 12);
        target.localRotation = Quaternion.Euler(17, 29, -11);
        Vector3 before = target.position;
        float originalTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0;
            Publish(pair.Key, 1);
            Vector3 delta = target.position - before;
            Assert.That(delta.magnitude, Is.EqualTo(0.003f).Within(0.000001f));
            Assert.That(Mathf.Abs(Vector3.Dot(delta.normalized, target.TransformVector(Vector3.up).normalized)), Is.LessThan(0.0001f));
            Assert.That(Mathf.Abs(Vector3.Dot(delta.normalized, target.TransformVector(Vector3.forward).normalized)), Is.LessThan(0.0001f));
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(Vector3.Distance(target.position, before), Is.LessThan(0.0015f), "The oscillation envelope must shrink.");
            // Moving the whole instrument while vibrating must not change its eventual local rest pose.
            effect.transform.position += Vector3.one;
            yield return new WaitForSecondsRealtime(0.14f);
            Assert.That(target.localPosition, Is.EqualTo(rest));
        }
        finally { Time.timeScale = originalTimeScale; }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator RapidAndSimultaneousHitsDoNotDriftAndDisableRestoresAllRegions()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        var effect = Object.FindFirstObjectByType<HitRegionVibration>();
        var pairs = Bindings(effect).ToArray();
        var rest = pairs.Select(p => p.Value.localPosition).ToArray();
        Vector3 firstWorld = pairs[0].Value.position;
        Publish(pairs[0].Key, 1);
        Publish(pairs[1].Key, 0.5f);
        Assert.That(pairs[0].Value.localPosition, Is.Not.EqualTo(rest[0]));
        Assert.That(pairs[1].Value.localPosition, Is.Not.EqualTo(rest[1]));
        yield return new WaitForSecondsRealtime(0.04f);
        for (int i = 0; i < 20; i++) Publish(pairs[0].Key, 1);
        Assert.That(Vector3.Distance(pairs[0].Value.position, firstWorld), Is.EqualTo(0.003f).Within(0.000001f));
        effect.enabled = false;
        for (int i = 0; i < pairs.Length; i++) Assert.That(pairs[i].Value.localPosition, Is.EqualTo(rest[i]));
        Publish(pairs[0].Key, 1);
        Assert.That(pairs[0].Value.localPosition, Is.EqualTo(rest[0]));
        effect.enabled = true;
        Publish(pairs[0].Key, 1);
        Assert.That(Vector3.Distance(pairs[0].Value.position, firstWorld), Is.EqualTo(0.003f).Within(0.000001f));
        yield return new WaitForSecondsRealtime(0.22f);
        Assert.That(pairs[0].Value.localPosition, Is.EqualTo(rest[0]));
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator RemappedIdsAndInvalidHitsDoNotMoveUnrelatedRegions()
    {
        EditorSceneManager.OpenScene(ScenePath);
        yield return new EnterPlayMode();
        yield return null;
        var effect = Object.FindFirstObjectByType<HitRegionVibration>();
        var original = Bindings(effect).First();
        Vector3 rest = original.Value.localPosition;
        effect.enabled = false;
        var settings = new SerializedObject(effect);
        settings.FindProperty("regions").GetArrayElementAtIndex(0).FindPropertyRelative("RegionId").stringValue = "future_region_id";
        settings.ApplyModifiedPropertiesWithoutUndo();
        effect.enabled = true;
        Publish(original.Key, 1);
        Publish(null, 1);
        Publish("unknown", 1);
        Publish("future_region_id", float.NaN);
        Publish("future_region_id", float.PositiveInfinity);
        Assert.That(original.Value.localPosition, Is.EqualTo(rest));
        Assert.That(effect.PlayHit("future_region_id", 5), Is.True);
        Assert.That(Vector3.Distance(original.Value.position, original.Value.parent.TransformPoint(rest)),
            Is.EqualTo(0.003f).Within(0.000001f));
        effect.gameObject.SetActive(false);
        Assert.That(original.Value.localPosition, Is.EqualTo(rest));
        effect.gameObject.SetActive(true);
        Publish("future_region_id", -1);
        Assert.That(Vector3.Distance(original.Value.position, original.Value.parent.TransformPoint(rest)),
            Is.EqualTo(0.001f).Within(0.000001f));
        Object.Destroy(effect);
        yield return null;
        Assert.That(original.Value.localPosition, Is.EqualTo(rest));
        Publish("future_region_id", 1); // Destroyed subscribers must have been removed.
        yield return new ExitPlayMode();
    }

    private static Dictionary<string, Transform> Bindings(HitRegionVibration effect)
    {
        var result = new Dictionary<string, Transform>();
        var regions = new SerializedObject(effect).FindProperty("regions");
        for (int i = 0; i < regions.arraySize; i++)
        {
            var region = regions.GetArrayElementAtIndex(i);
            result.Add(region.FindPropertyRelative("RegionId").stringValue,
                (Transform)region.FindPropertyRelative("Target").objectReferenceValue);
        }
        return result;
    }

    private static void Publish(string id, float force) =>
        GameEventBus.OnValidHit?.Invoke(new HitEvent { InstrumentId = id, HitForce = force, MalletId = 0 });
}
