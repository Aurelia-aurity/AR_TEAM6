using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Previews synthetic mallet strikes against both inner janggu regions.</summary>
public sealed class JangguHitPreview : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const string RegionA = "janggu_head_a_inner";
    private const string RegionB = "janggu_head_b_inner";

    [SerializeField] private Transform janggu;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private JangguAudioPreview hitAudio;

    [SerializeField] private RectTransform leftMarker;
    [SerializeField] private RectTransform rightMarker;
    [SerializeField] private Text status;
    [SerializeField] private Text faceALabel;
    [SerializeField] private Text faceBLabel;
    [SerializeField] private Slider positionX;
    [SerializeField] private Slider positionY;
    [SerializeField] private Slider positionZ;
    [SerializeField] private Slider rotationY;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Plane dragPlane;
    private readonly PreviewMallet leftMallet = new PreviewMallet();
    private readonly PreviewMallet rightMallet = new PreviewMallet();
    private HitDetectionCore core;
    private Transform surface;
    private InstrumentPad padA;
    private InstrumentPad padB;
    private InstrumentPad activePad;
    private int faceAHits;
    private int faceBHits;
    private double timestampMs;
    private int hits;
    private float? lastHitStrength;
    private string lastHitSound;
    private Coroutine scenarioPlayback;
    private Vector3 positionBeforeScenario;
    private PreviewMallet scenarioMallet;


    private void OnEnable()
    {
        padA = CreatePad("Head_A_Inner", RegionA, 1f);
        padB = CreatePad("Head_B_Inner", RegionB, -1f);
        if (padA == null || padB == null)
        {
            Debug.LogError("The preview requires both inner drumhead meshes.", this);
            enabled = false;
            return;
        }

        surface = padA.HitSurface;
        Vector3 outward = surface.forward;
        Vector3 up = surface.up;
        initialPosition = janggu.localPosition;
        initialRotation = janggu.localRotation;
        core = new HitDetectionCore();
        core.Subscribe();
        GameEventBus.OnValidHit += CountHit;

        Vector3 cameraOffset = outward * 0.95f + up * 0.22f + surface.right * 0.8f;
        previewCamera.transform.SetPositionAndRotation(surface.position + cameraOffset,
            Quaternion.LookRotation(-cameraOffset, up));
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        ResetMallet();
    }

    private InstrumentPad CreatePad(string meshName, string regionId, float outwardSign)
    {
        MeshFilter head = System.Array.Find(janggu.GetComponentsInChildren<MeshFilter>(),
            mesh => mesh.name == meshName);
        if (head == null || head.sharedMesh == null)
        {
            return null;
        }

        Bounds bounds = head.sharedMesh.bounds;
        Transform meshTransform = head.transform;
        Transform strikeSurface = new GameObject(meshName + " Strike Surface").transform;
        Vector3 center = bounds.center;
        center.x = outwardSign > 0f ? bounds.max.x : bounds.min.x;
        Vector3 outward = meshTransform.TransformDirection(Vector3.right * outwardSign);
        Vector3 up = meshTransform.TransformDirection(Vector3.up);
        strikeSurface.SetPositionAndRotation(meshTransform.TransformPoint(center), Quaternion.LookRotation(outward, up));
        strikeSurface.SetParent(janggu, true);

        float width = meshTransform.TransformVector(Vector3.forward * bounds.size.z).magnitude;
        float height = meshTransform.TransformVector(Vector3.up * bounds.size.y).magnitude;
        return new InstrumentPad
        {
            PadId = regionId,
            HitSurface = strikeSurface,
            HitBox2D = new Rect(-width * 0.5f, -height * 0.5f, width, height)
        };
    }

    private void OnDisable()
    {
        StopScenario();
        leftMallet.PointerId = null;
        rightMallet.PointerId = null;
        GameEventBus.OnValidHit -= CountHit;
        core?.Dispose();
        core = null;
        if (padA != null && padA.HitSurface)
        {
            Destroy(padA.HitSurface.gameObject);
        }

        if (padB != null && padB.HitSurface)
        {
            Destroy(padB.HitSurface.gameObject);
        }
    }

    public void ApplyPose(float _)
    {
        if (core == null)
        {
            return;
        }

        janggu.localPosition = initialPosition + new Vector3(positionX.value, positionY.value, positionZ.value);
        janggu.localRotation = initialRotation * Quaternion.Euler(0f, rotationY.value, 0f);
        RepositionMallets();
    }

    public void ResetPose()
    {
        positionX.SetValueWithoutNotify(0f);
        positionY.SetValueWithoutNotify(0f);
        positionZ.SetValueWithoutNotify(0f);
        rotationY.SetValueWithoutNotify(0f);
        ApplyPose(0f);
    }

    public void ResetMallet()
    {
        leftMallet.Hits = 0;
        rightMallet.Hits = 0;
        faceAHits = 0;
        faceBHits = 0;
        lastHitStrength = null;
        lastHitSound = null;
        faceALabel.text = "A";
        faceBLabel.text = "B";
        RepositionMallets();
    }

    private void RepositionMallets()
    {
        StopScenario();
        leftMallet.PointerId = null;
        rightMallet.PointerId = null;
        ResetTracking();

        Vector3 cameraPosition = previewCamera.transform.position;
        float facingA = Vector3.Dot(padA.HitSurface.forward, (cameraPosition - padA.HitSurface.position).normalized);
        float facingB = Vector3.Dot(padB.HitSurface.forward, (cameraPosition - padB.HitSurface.position).normalized);
        activePad = facingA >= facingB ? padA : padB;
        surface = activePad.HitSurface;

        Vector3 normal = previewCamera.transform.forward;
        Vector3 approach = Vector3.ProjectOnPlane(surface.forward, normal);
        if (approach.sqrMagnitude < 0.4f)
        {
            // 정면에서도 채를 움직여 타격면을 통과할 수 있게 드래그 평면을 기울인다.
            Vector3 right = previewCamera.transform.right;
            float tilt = Mathf.Sign(Vector3.Dot(surface.forward, normal) * Vector3.Dot(surface.forward, right));
            normal = (normal - tilt * right).normalized;
            approach = Vector3.ProjectOnPlane(surface.forward, normal);
        }

        dragPlane = new Plane(normal, surface.position);
        Vector3 start = surface.position + approach * (0.34f / approach.sqrMagnitude);
        Vector3 across = Vector3.Cross(normal, approach).normalized;
        leftMallet.Position = start + across * 0.09f;
        rightMallet.Position = start - across * 0.09f;
        PlaceMarkers();
        ShowManualHits();
    }

    public void OnBeginDrag(PointerEventData input)
    {
        if (scenarioPlayback != null || input.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        Transform target = input.pointerPressRaycast.gameObject?.transform;
        PreviewMallet mallet = null;
        if (target != null && target.IsChildOf(leftMarker))
        {
            mallet = leftMallet;
        }
        else if (target != null && target.IsChildOf(rightMarker))
        {
            mallet = rightMallet;
        }

        if (mallet == null || mallet.PointerId.HasValue)
        {
            return;
        }

        mallet.PointerId = input.pointerId;
        mallet.PointerOffset = (Vector2)previewCamera.WorldToScreenPoint(mallet.Position) - input.pressPosition;
        PublishManualSamples();
        OnDrag(input);
    }

    public void OnDrag(PointerEventData input)
    {
        PreviewMallet mallet = FindDraggedMallet(input.pointerId);
        if (mallet == null)
        {
            return;
        }

        Ray ray = previewCamera.ScreenPointToRay(input.position + mallet.PointerOffset);
        if (dragPlane.Raycast(ray, out float distance))
        {
            mallet.Position = ray.GetPoint(distance);
            PlaceMarkers();
        }
    }

    public void OnEndDrag(PointerEventData input)
    {
        PreviewMallet mallet = FindDraggedMallet(input.pointerId);
        if (mallet != null)
        {
            PublishManualSamples();
            mallet.PointerId = null;
        }
    }

    private PreviewMallet FindDraggedMallet(int pointerId)
    {
        if (leftMallet.PointerId == pointerId)
        {
            return leftMallet;
        }

        return rightMallet.PointerId == pointerId ? rightMallet : null;
    }

    private void Update()
    {
        if (leftMallet.PointerId.HasValue || rightMallet.PointerId.HasValue)
        {
            PublishManualSamples();
        }

        PlaceMarkers();
    }

    private void PlaceMarkers()
    {
        PlaceMarker(leftMarker, leftMallet.Position);
        PlaceMarker(rightMarker, rightMallet.Position);
        PlaceFaceLabel(faceALabel, padA);
        PlaceFaceLabel(faceBLabel, padB);
    }

    private void PlaceFaceLabel(Text label, InstrumentPad pad)
    {
        Transform face = pad.HitSurface;
        PlaceMarker(label.rectTransform, face.position + face.up * 0.25f);
    }

    private void PlaceMarker(RectTransform marker, Vector3 position)
    {
        Vector3 screenPoint = previewCamera.WorldToScreenPoint(position);
        RectTransform parent = (RectTransform)marker.parent;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, null, out Vector2 point))
        {
            marker.anchoredPosition = point;
        }
    }

    private void PublishManualSamples()
    {
        double now = Time.realtimeSinceStartupAsDouble * 1000.0;
        GameEventBus.OnMalletsTracked?.Invoke(
            leftMallet.CreateSample(0, now),
            rightMallet.CreateSample(1, now));
        ShowManualHits();
    }

    private void ShowManualHits()
    {
        status.text = $"Left hits: {leftMallet.Hits}    Right hits: {rightMallet.Hits}\nA face: {faceAHits}    B face: {faceBHits}\n{FormatHitStrength()}";
    }

    public void TestStayBehind(int malletId) => RunSequence(malletId, 0f, 1, 0.4f, -0.05f, -0.1f);
    public void TestRehit(int malletId) => RunSequence(malletId, 0f, 2, 0.4f, -0.05f, 0.4f, -0.05f);
    public void TestOutsidePad(int malletId) => RunSequence(malletId, activePad.HitBox2D.width, 0, 0.4f, -0.05f);
    public void TestStayInFront(int malletId) => RunSequence(malletId, 0f, 0, 0.4f, 0.05f);

    private void ResetTracking()
    {
        core.ClearPads();
        core.RegisterPad(padA);
        core.RegisterPad(padB);
    }

    private void RunSequence(int malletId, float horizontalOffset, int expectedHits, params float[] depths)
    {
        if (core == null)
        {
            return;
        }

        StopScenario();
        leftMallet.PointerId = null;
        rightMallet.PointerId = null;
        scenarioMallet = malletId == 0 ? leftMallet : rightMallet;
        positionBeforeScenario = scenarioMallet.Position;
        scenarioPlayback = StartCoroutine(PlaySequence(malletId, horizontalOffset, expectedHits, depths));
    }

    private IEnumerator PlaySequence(int malletId, float horizontalOffset, int expectedHits, float[] depths)
    {
        hits = 0;
        lastHitStrength = null;
        lastHitSound = null;
        ResetTracking();
        Vector3 start = surface.TransformPoint(new Vector3(horizontalOffset, 0f, depths[0]));
        scenarioMallet.Position = start;
        PlaceMarkers();
        PublishScenarioSample(malletId, start);
        ShowScenarioHits(malletId, expectedHits);

        for (int i = 1; i < depths.Length; i++)
        {
            Vector3 end = surface.TransformPoint(new Vector3(horizontalOffset, 0f, depths[i]));
            double startedAt = Time.realtimeSinceStartupAsDouble;
            const double segmentSeconds = 0.45;
            while (Time.realtimeSinceStartupAsDouble - startedAt < segmentSeconds)
            {
                float progress = (float)((Time.realtimeSinceStartupAsDouble - startedAt) / segmentSeconds);
                scenarioMallet.Position = Vector3.Lerp(start, end, progress);
                PlaceMarkers();
                yield return null;
            }

            scenarioMallet.Position = end;
            PlaceMarkers();
            // 화면 재생 속도와 별개로 원래 시나리오의 추적 샘플 간격을 유지한다.
            PublishScenarioSample(malletId, end);
            ShowScenarioHits(malletId, expectedHits);
            start = end;
        }

        yield return new WaitForSecondsRealtime(0.55f);
        scenarioMallet.Position = positionBeforeScenario;
        PlaceMarkers();
        ResetTracking();
        scenarioPlayback = null;
    }

    private void StopScenario()
    {
        if (scenarioPlayback != null)
        {
            StopCoroutine(scenarioPlayback);
            scenarioPlayback = null;
            scenarioMallet.Position = positionBeforeScenario;
        }
    }

    private void ShowScenarioHits(int malletId, int expectedHits)
    {
        status.text = $"Hits: {hits} / {expectedHits} expected\nFace: {(activePad == padA ? "A" : "B")}    Mallet: {(malletId == 0 ? "L" : "R")}\n{FormatHitStrength()}";
    }

    private string FormatHitStrength()
    {
        return lastHitStrength.HasValue
            ? $"Last strength: {lastHitStrength.Value * 100f:0}% ({lastHitSound})"
            : "Last strength: --";
    }

    private void PublishScenarioSample(int malletId, Vector3 position)
    {
        timestampMs += 33.0;
        var sample = new MalletState { MalletId = malletId, Position = position, Confidence = 1f, TimestampMs = timestampMs };
        var missing = new MalletState { MalletId = 1 - malletId, TimestampMs = timestampMs };
        if (malletId == 0)
        {
            GameEventBus.OnMalletsTracked?.Invoke(sample, missing);
        }
        else
        {
            GameEventBus.OnMalletsTracked?.Invoke(missing, sample);
        }
    }

    private void CountHit(HitEvent hit)
    {
        if (hit.InstrumentId == RegionA || hit.InstrumentId == RegionB)
        {
            hits++;
            lastHitStrength = hit.HitForce;
            bool isDeok = hit.InstrumentId == RegionA;
            int strength = hit.HitForce >= 0.66f ? 0 : hit.HitForce >= 0.33f ? 1 : 2;
            string strengthName = strength == 0 ? "strong" : strength == 1 ? "medium" : "weak";
            lastHitSound = $"{(isDeok ? "Deok" : "Kung")} {strengthName}";
            if (hitAudio != null)
            {
                int sample = isDeok ? strength : JangguSoundProfile.StrengthCount + strength;
                hitAudio.PlaySample(sample);
            }
            string malletLabel = hit.MalletId == 0 ? "L" : "R";
            if (hit.InstrumentId == RegionA)
            {
                faceALabel.text = $"A - {malletLabel}";
            }
            else
            {
                faceBLabel.text = $"B - {malletLabel}";
            }
            PreviewMallet mallet = hit.MalletId == 0 ? leftMallet : rightMallet;
            if (mallet.PointerId.HasValue)
            {
                mallet.Hits++;
                if (hit.InstrumentId == RegionA)
                {
                    faceAHits++;
                }
                else
                {
                    faceBHits++;
                }
            }
        }
    }

    /// <summary>Stores the pointer, position and hit count for one preview mallet.</summary>
    private sealed class PreviewMallet
    {
        public Vector3 Position;
        public Vector2 PointerOffset;
        public int? PointerId;
        public int Hits;

        public MalletState CreateSample(int id, double timestampMs)
        {
            return new MalletState
            {
                MalletId = id,
                Position = Position,
                Confidence = PointerId.HasValue ? 1f : 0f,
                TimestampMs = timestampMs
            };
        }
    }
}
