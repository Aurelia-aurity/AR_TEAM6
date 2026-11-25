using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime strike-surface builder and HitDetectionCore host for Main.unity.
/// Rebuilds pads and resets strike locks whenever the selected instrument changes.
/// </summary>
[DisallowMultipleComponent]
public sealed class InstrumentHitDetector : MonoBehaviour
{
    private static readonly (string meshName, string padId, float outwardSign)[] JangguPads =
    {
        ("Head_A_Inner", "janggu_head_a_inner", 1f),
        ("Head_A_Outer", "janggu_head_a_outer", 1f),
        ("Head_B_Inner", "janggu_head_b_inner", -1f),
        ("Head_B_Outer", "janggu_head_b_outer", -1f)
    };

    private static readonly (string meshName, string padId, float outwardSign)[] BukPads =
    {
        ("Head_A_Inner", "buk_head_a_inner", 1f),
        ("Head_A_Outer", "buk_head_a_outer", 1f),
        ("Head_B_Inner", "buk_head_b_inner", -1f),
        ("Head_B_Outer", "buk_head_b_outer", -1f)
    };

    [SerializeField] private Transform jangguRoot;
    [SerializeField] private Transform bukRoot;
    [SerializeField, Min(0.05f)] private float rearmDistance = 0.2f;
    [SerializeField, Min(0.1f)] private float minVelocityToTrigger = 1.2f;
    [SerializeField, Min(0.5f)] private float maxVelocityToMap = 12.0f;
    [SerializeField, Min(16.0f)] private float maxTrackingGapMs = 150.0f;

    private HitDetectionCore core;
    private readonly List<GameObject> spawnedSurfaces = new List<GameObject>();

    public int ActivePadCount { get; private set; }

    private void OnEnable()
    {
        core = new HitDetectionCore
        {
            RearmDistance = rearmDistance,
            MinVelocityToTrigger = minVelocityToTrigger,
            MaxVelocityToMap = maxVelocityToMap,
            MaxTrackingGapMs = maxTrackingGapMs
        };

        RebuildPads(GameEventBus.SelectedInstrument);
        core.Subscribe();
        GameEventBus.OnInstrumentSelected += RebuildPads;
    }

    private void OnDisable()
    {
        GameEventBus.OnInstrumentSelected -= RebuildPads;
        core?.Dispose();
        core = null;
        DestroySpawnedSurfaces();
        ActivePadCount = 0;
    }

    public void RebuildPads(InstrumentType instrument)
    {
        if (core == null)
        {
            return;
        }

        core.ClearPads();
        DestroySpawnedSurfaces();
        ActivePadCount = 0;

        Transform root;
        (string meshName, string padId, float outwardSign)[] specs;
        switch (instrument)
        {
            case InstrumentType.Janggu:
                root = jangguRoot;
                specs = JangguPads;
                break;
            case InstrumentType.Buk:
                root = bukRoot;
                specs = BukPads;
                break;
            default:
                return;
        }

        if (root == null)
        {
            return;
        }

        MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < specs.Length; i++)
        {
            (string meshName, string padId, float outwardSign) = specs[i];
            InstrumentPad pad = CreatePad(root, meshes, meshName, padId, outwardSign);
            if (pad != null)
            {
                core.RegisterPad(pad);
                ActivePadCount++;
            }
        }
    }

    private InstrumentPad CreatePad(
        Transform instrumentRoot,
        MeshFilter[] meshes,
        string meshName,
        string padId,
        float outwardSign)
    {
        MeshFilter head = Array.Find(meshes, mesh => mesh != null && mesh.name == meshName);
        if (head == null || head.sharedMesh == null)
        {
            Debug.LogWarning($"InstrumentHitDetector could not find mesh '{meshName}' under '{instrumentRoot.name}'.", this);
            return null;
        }

        Bounds bounds = head.sharedMesh.bounds;
        Transform meshTransform = head.transform;
        var surfaceObject = new GameObject($"{padId} Strike Surface");
        Transform strikeSurface = surfaceObject.transform;
        Vector3 center = bounds.center;
        center.x = outwardSign > 0f ? bounds.max.x : bounds.min.x;
        Vector3 outward = meshTransform.TransformDirection(Vector3.right * outwardSign);
        Vector3 up = meshTransform.TransformDirection(Vector3.up);
        strikeSurface.SetPositionAndRotation(meshTransform.TransformPoint(center), Quaternion.LookRotation(outward, up));
        strikeSurface.SetParent(instrumentRoot, true);
        spawnedSurfaces.Add(surfaceObject);

        float width = meshTransform.TransformVector(Vector3.forward * bounds.size.z).magnitude;
        float height = meshTransform.TransformVector(Vector3.up * bounds.size.y).magnitude;
        return new InstrumentPad
        {
            PadId = padId,
            HitSurface = strikeSurface,
            HitBox2D = new Rect(-width * 0.5f, -height * 0.5f, width, height)
        };
    }

    private void DestroySpawnedSurfaces()
    {
        for (int i = 0; i < spawnedSurfaces.Count; i++)
        {
            if (spawnedSurfaces[i] != null)
            {
                Destroy(spawnedSurfaces[i]);
            }
        }

        spawnedSurfaces.Clear();
    }
}
