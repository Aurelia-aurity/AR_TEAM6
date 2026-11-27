#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class InstrumentHitRuntimeTests
{
    [UnityTest]
    public IEnumerator PlacedJangguAndBuk_PlayThroughTrackingBusWithoutPreviewComponents()
    {
        GameObject janggu = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Presentation/Prefabs/Janggu.prefab"));
        GameObject buk = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Presentation/Prefabs/Buk.prefab"));
        janggu.transform.SetPositionAndRotation(Vector3.left, Quaternion.Euler(0f, 25f, 0f));
        buk.transform.SetPositionAndRotation(Vector3.right, Quaternion.Euler(0f, 180f, 0f));
        DrumStrikeSurfaces jangguSurfaces = null;
        DrumStrikeSurfaces bukSurfaces = null;
        var played = new List<int>();
        try
        {
            Assert.That(DrumStrikeSurfaces.TryCreate(janggu.transform,
                new DrumHeadDefinition("Head_A_Inner", "janggu_a"),
                new DrumHeadDefinition("Head_B_Inner", "janggu_b"), out jangguSurfaces), Is.True);
            Assert.That(DrumStrikeSurfaces.TryCreate(buk.transform,
                new DrumHeadDefinition("Head_A_Inner", "buk_a"),
                new DrumHeadDefinition("Head_B_Inner", "buk_b"), out bukSurfaces), Is.True);
            var pads = new List<InstrumentPad>(jangguSurfaces.Pads);
            pads.AddRange(bukSurfaces.Pads);
            using (var runtime = new InstrumentHitRuntime(pads))
            using (var audio = new InstrumentHitAudio(played.Add,
                new InstrumentSoundBinding("janggu_a", "Deok", 0, 1, 2),
                new InstrumentSoundBinding("janggu_b", "Kung", 3, 4, 5),
                new InstrumentSoundBinding("buk_a", "Buk A", 6, 7, 8),
                new InstrumentSoundBinding("buk_b", "Buk B", 9, 10, 11)))
            {
                audio.Subscribe();
                runtime.Subscribe();
                runtime.Subscribe();
                for (int instrument = 0; instrument < 2; instrument++)
                {
                    Transform model = instrument == 0 ? janggu.transform : buk.transform;
                    MeshFilter[] meshes = model.GetComponentsInChildren<MeshFilter>();
                    MeshFilter headA = System.Array.Find(meshes, mesh => mesh.name == "Head_A_Inner");
                    MeshFilter headB = System.Array.Find(meshes, mesh => mesh.name == "Head_B_Inner");
                    Vector3 centerA = headA.transform.TransformPoint(headA.sharedMesh.bounds.center);
                    Vector3 centerB = headB.transform.TransformPoint(headB.sharedMesh.bounds.center);
                    Vector3 center = (centerA + centerB) * 0.5f;
                    for (int face = 0; face < 2; face++)
                    {
                        runtime.ResetTracking();
                        int before = played.Count;
                        Vector3 faceCenter = face == 0 ? centerA : centerB;
                        Vector3 outward = (faceCenter - center).normalized;
                        Publish(faceCenter - outward * 0.05f, 0);
                        Publish(faceCenter + outward * 0.4f, 33);
                        Assert.That(played.Count, Is.EqualTo(before), "Withdrawal must not play.");
                        Publish(faceCenter - outward * 0.05f, 66);
                        Assert.That(played.Count, Is.EqualTo(before + 1));
                        Assert.That(played[before], Is.EqualTo(instrument * 6 + face * 3));
                        Publish(faceCenter + outward * 0.05f, 99);
                        Publish(faceCenter - outward * 0.05f, 132);
                        Assert.That(played.Count, Is.EqualTo(before + 1), "Jitter must not replay.");
                        Publish(faceCenter + outward * 0.4f, 165);
                        Assert.That(played.Count, Is.EqualTo(before + 1));
                        Publish(faceCenter - outward * 0.05f, 198);
                        Assert.That(played.Count, Is.EqualTo(before + 2));
                    }
                }
            }
            int afterDispose = played.Count;
            Publish(Vector3.zero, 231);
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                ContextVersion = GameEventBus.ContextVersion, InstrumentId = "buk_a", HitForce = 1f
            });
            Assert.That(played.Count, Is.EqualTo(afterDispose), "Disposed audio must unsubscribe.");
        }
        finally
        {
            jangguSurfaces?.Dispose();
            bukSurfaces?.Dispose();
            Object.Destroy(janggu);
            Object.Destroy(buk);
        }
        yield return null;
    }

    private static void Publish(Vector3 position, double time)
    {
        GameEventBus.OnMalletsTracked?.Invoke(new MalletState
        {
            ContextVersion = GameEventBus.ContextVersion,
            MalletId = 0, Position = position, Confidence = 1f, TimestampMs = time
        }, new MalletState
        {
            ContextVersion = GameEventBus.ContextVersion, MalletId = 1, TimestampMs = time
        });
    }
}
#endif
