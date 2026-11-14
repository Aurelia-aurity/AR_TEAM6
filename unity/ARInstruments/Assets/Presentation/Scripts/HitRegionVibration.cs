using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation-only displacement of mapped visual regions. Distances are world metres.</summary>
[DisallowMultipleComponent]
public sealed class HitRegionVibration : MonoBehaviour
{
    [Serializable]
    public sealed class RegionBinding
    {
        public string RegionId;
        public Transform Target;
        [Tooltip("Outward surface normal in the target mesh's local space.")]
        public Vector3 LocalNormal = Vector3.right;
    }

    [SerializeField] private RegionBinding[] regions = Array.Empty<RegionBinding>();
    [SerializeField, Range(0.1f, 0.2f)] private float durationSeconds = 0.15f;
    [SerializeField, Range(1, 4)] private int oscillations = 3;

    private sealed class RegionState
    {
        public RegionBinding Binding;
        public Vector3 RestPosition;
        public double StartedAt;
        public float Amplitude;
        public float Duration;
        public int Oscillations;
        public bool Playing;
    }

    private readonly Dictionary<string, RegionState> states = new Dictionary<string, RegionState>(StringComparer.Ordinal);

    private void OnEnable()
    {
        states.Clear();
        var targets = new HashSet<Transform>();
        foreach (RegionBinding binding in regions)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.RegionId) || binding.Target == null
                || binding.Target == transform || !binding.Target.IsChildOf(transform)
                || !IsFinite(binding.LocalNormal) || binding.LocalNormal.sqrMagnitude < 0.000001f
                || states.ContainsKey(binding.RegionId) || !targets.Add(binding.Target))
            {
                Debug.LogError("HitRegionVibration requires unique IDs, unique visual children and valid local normals.", this);
                enabled = false;
                return;
            }
            states.Add(binding.RegionId, new RegionState { Binding = binding, RestPosition = binding.Target.localPosition });
        }
        GameEventBus.OnValidHit += OnValidHit;
    }

    private void OnValidHit(HitEvent hit) => PlayHit(hit.InstrumentId, hit.HitForce);

    /// <summary>Local visual preview. Module C should publish GameEventBus.OnValidHit instead.</summary>
    public bool PlayHit(string regionId, float force)
    {
        if (!isActiveAndEnabled || string.IsNullOrEmpty(regionId) || !IsFinite(force)
            || !states.TryGetValue(regionId, out RegionState state)
            || state.Binding.Target == null || !state.Binding.Target.gameObject.activeInHierarchy)
            return false;

        // Restart from the saved rest pose, never from the previous displaced position.
        state.StartedAt = Time.unscaledTimeAsDouble;
        state.Amplitude = Mathf.Lerp(0.001f, 0.003f, Mathf.Clamp01(force));
        state.Duration = IsFinite(durationSeconds) ? Mathf.Clamp(durationSeconds, 0.1f, 0.2f) : 0.15f;
        state.Oscillations = Mathf.Clamp(oscillations, 1, 4);
        state.Playing = true;
        ApplyOffset(state, -state.Amplitude);
        return true;
    }

    // Runs before the preview camera's LateUpdate render, including while timeScale is zero.
    private void Update()
    {
        double now = Time.unscaledTimeAsDouble;
        foreach (RegionState state in states.Values)
        {
            if (!state.Playing) continue;
            Transform target = state.Binding.Target;
            float progress = (float)((now - state.StartedAt) / state.Duration);
            if (target == null || !target.gameObject.activeInHierarchy || progress >= 1)
            {
                Restore(state);
                continue;
            }
            float envelope = (1 - progress) * (1 - progress);
            float offset = -state.Amplitude * envelope * Mathf.Cos(progress * state.Oscillations * 2 * Mathf.PI);
            ApplyOffset(state, offset);
        }
    }

    private static void ApplyOffset(RegionState state, float metres)
    {
        Transform target = state.Binding.Target;
        // Inverse transpose preserves perpendicularity even with rotated, non-uniformly scaled parents.
        Vector3 normal = target.worldToLocalMatrix.transpose.MultiplyVector(state.Binding.LocalNormal).normalized;
        Vector3 offset = normal * metres;
        if (target.parent != null) offset = target.parent.InverseTransformVector(offset);
        if (!IsFinite(offset) || normal.sqrMagnitude < 0.000001f)
        {
            Restore(state);
            return;
        }
        target.localPosition = state.RestPosition + offset;
    }

    private void OnDisable()
    {
        GameEventBus.OnValidHit -= OnValidHit;
        foreach (RegionState state in states.Values) Restore(state);
        states.Clear();
    }

    private static void Restore(RegionState state)
    {
        if (state.Binding.Target != null) state.Binding.Target.localPosition = state.RestPosition;
        state.Playing = false;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
}
