using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Identifies a model mesh and the hit event region it represents.</summary>
public readonly struct DrumHeadDefinition
{
    public readonly string MeshName;
    public readonly string RegionId;

    public DrumHeadDefinition(string meshName, string regionId)
    {
        MeshName = meshName;
        RegionId = regionId;
    }
}

/// <summary>Owns two generated strike surfaces, independently of cameras, input and preview UI.</summary>
public sealed class DrumStrikeSurfaces : IDisposable
{
    public InstrumentPad PadA { get; }
    public InstrumentPad PadB { get; }
    public IReadOnlyList<InstrumentPad> Pads { get; }
    private bool disposed;

    private DrumStrikeSurfaces(InstrumentPad padA, InstrumentPad padB)
    {
        PadA = padA;
        PadB = padB;
        Pads = Array.AsReadOnly(new[] { padA, padB });
    }

    public static bool TryCreate(Transform model, DrumHeadDefinition definitionA,
        DrumHeadDefinition definitionB, out DrumStrikeSurfaces surfaces)
    {
        surfaces = null;
        if (!model || string.IsNullOrWhiteSpace(definitionA.RegionId)
            || string.IsNullOrWhiteSpace(definitionB.RegionId) || definitionA.RegionId == definitionB.RegionId)
            return false;

        MeshFilter[] meshes = model.GetComponentsInChildren<MeshFilter>();
        MeshFilter headA = Array.Find(meshes, mesh => mesh.name == definitionA.MeshName);
        MeshFilter headB = Array.Find(meshes, mesh => mesh.name == definitionB.MeshName);
        if (!headA || !headA.sharedMesh || !headB || !headB.sharedMesh || headA == headB)
            return false;

        surfaces = new DrumStrikeSurfaces(CreatePad(model, headA, headB, definitionA.RegionId),
            CreatePad(model, headB, headA, definitionB.RegionId));
        return true;
    }

    private static InstrumentPad CreatePad(Transform model, MeshFilter head, MeshFilter opposite, string regionId)
    {
        Bounds bounds = head.sharedMesh.bounds;
        Transform meshTransform = head.transform;
        Vector3 headCenter = meshTransform.TransformPoint(bounds.center);
        Vector3 oppositeCenter = opposite.transform.TransformPoint(opposite.sharedMesh.bounds.center);
        // A/B labels do not imply an axis sign: Buk and Janggu use opposite layouts.
        Vector3 headAxis = meshTransform.TransformDirection(Vector3.right);
        float outwardSign = Vector3.Dot(headAxis, headCenter - oppositeCenter) >= 0f ? 1f : -1f;
        Transform strikeSurface = new GameObject(head.name + " Strike Surface").transform;
        Vector3 center = bounds.center;
        center.x = outwardSign > 0f ? bounds.max.x : bounds.min.x;
        Vector3 outward = meshTransform.TransformDirection(Vector3.right * outwardSign);
        Vector3 up = meshTransform.TransformDirection(Vector3.up);
        strikeSurface.SetPositionAndRotation(meshTransform.TransformPoint(center), Quaternion.LookRotation(outward, up));
        strikeSurface.SetParent(model, true);

        float width = meshTransform.TransformVector(Vector3.forward * bounds.size.z).magnitude;
        float height = meshTransform.TransformVector(Vector3.up * bounds.size.y).magnitude;
        return new InstrumentPad
        {
            PadId = regionId,
            HitSurface = strikeSurface,
            HitBox2D = new Rect(-width * 0.5f, -height * 0.5f, width, height)
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DestroySurface(PadA.HitSurface);
        DestroySurface(PadB.HitSurface);
    }

    private static void DestroySurface(Transform surface)
    {
        if (!surface) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(surface.gameObject);
        else UnityEngine.Object.DestroyImmediate(surface.gameObject);
    }
}
