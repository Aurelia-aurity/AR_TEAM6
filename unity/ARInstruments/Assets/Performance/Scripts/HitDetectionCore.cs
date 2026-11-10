using UnityEngine;

public struct DrumHitbox
{
    public string InstrumentId;
    public Vector3 Center;
    public float Radius;
    public float HitZThreshold;
    public float LiftZThreshold;
}

public sealed class HitDetectionCore : MonoBehaviour
{
    [SerializeField] private float globalHitForceMultiplier = 1.0f;

    private DrumHitbox[] drumNodes;
    private bool[] isLeftMalletLocked;
    private bool[] isRightMalletLocked;

    private void OnEnable()
    {
        InitializeDrumKit();
        GameEventBus.OnMalletsTracked += ProcessHitDetection;
    }

    private void OnDisable()
    {
        GameEventBus.OnMalletsTracked -= ProcessHitDetection;
    }

    private void InitializeDrumKit()
    {
        drumNodes = new DrumHitbox[2];
        drumNodes[0] = new DrumHitbox
        {
            InstrumentId = "Snare_1",
            Center = new Vector3(-0.5f, 0.0f, 0.5f),
            Radius = 0.2f,
            HitZThreshold = 0.1f,
            LiftZThreshold = 0.15f
        };
        drumNodes[1] = new DrumHitbox
        {
            InstrumentId = "Cymbal_R",
            Center = new Vector3(0.5f, 0.0f, 0.6f),
            Radius = 0.25f,
            HitZThreshold = 0.12f,
            LiftZThreshold = 0.18f
        };

        isLeftMalletLocked = new bool[drumNodes.Length];
        isRightMalletLocked = new bool[drumNodes.Length];
    }

    private void ProcessHitDetection(MalletState left, MalletState right)
    {
        for (int i = 0; i < drumNodes.Length; i++)
        {
            CheckSingleMallet(ref left, i, ref isLeftMalletLocked[i]);
            CheckSingleMallet(ref right, i, ref isRightMalletLocked[i]);
        }
    }

    private void CheckSingleMallet(
        ref MalletState mallet,
        int drumIndex,
        ref bool isLocked)
    {
        if (mallet.Confidence < 0.6f)
        {
            return;
        }

        ref DrumHitbox node = ref drumNodes[drumIndex];
        float dx = mallet.Position.x - node.Center.x;
        float dy = mallet.Position.y - node.Center.y;
        float squaredDistance = dx * dx + dy * dy;
        float squaredRadius = node.Radius * node.Radius;

        if (squaredDistance > squaredRadius)
        {
            isLocked = false;
            return;
        }

        if (!isLocked && mallet.Position.z >= node.HitZThreshold)
        {
            isLocked = true;
            GameEventBus.OnValidHit?.Invoke(new HitEvent
            {
                InstrumentId = node.InstrumentId,
                HitPosition = mallet.Position,
                HitForce = Mathf.Clamp01(
                    mallet.Velocity.magnitude * globalHitForceMultiplier),
                MalletId = mallet.MalletId
            });
        }
        else if (isLocked && mallet.Position.z < node.LiftZThreshold)
        {
            isLocked = false;
        }
    }
}
