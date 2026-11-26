using NUnit.Framework;
using UnityEngine;

public sealed class KalmanFilterTests
{
    [Test]
    public void KalmanFilter3DProducesNonZeroZVelocityOnDepthMovement()
    {
        var filter = new KalmanFilter3D();
        filter.SetNoiseParameters(0.05f, 0.2f);

        filter.Update(new Vector3(0.0f, 0.0f, 0.2f), 100.0, out Vector3 p0, out Vector3 v0);
        Assert.That(p0, Is.EqualTo(new Vector3(0.0f, 0.0f, 0.2f)));
        Assert.That(v0, Is.EqualTo(Vector3.zero));

        filter.Update(new Vector3(0.0f, 0.0f, 0.8f), 150.0, out Vector3 p1, out Vector3 v1);
        Assert.That(p1.z, Is.GreaterThan(0.2f));
        Assert.That(v1.z, Is.GreaterThan(0.0f));

        filter.Update(new Vector3(0.0f, 0.0f, 0.1f), 200.0, out Vector3 p2, out Vector3 v2);
        Assert.That(p2.z, Is.LessThan(p1.z));
        Assert.That(v2.z, Is.LessThan(0.0f));
    }

    [Test]
    public void KalmanFilter2DAnd3DHandleNonFiniteInputsWithoutCorruptingState()
    {
        var filter2 = new KalmanFilter2D();
        filter2.Update(new Vector2(1.0f, 2.0f), 100.0, out _, out _);
        filter2.Update(new Vector2(float.NaN, 2.0f), 120.0, out Vector2 p2, out Vector2 v2);
        Assert.That(float.IsFinite(p2.x) && float.IsFinite(p2.y), Is.True);
        Assert.That(p2, Is.EqualTo(new Vector2(1.0f, 2.0f)));
        Assert.That(v2, Is.EqualTo(Vector2.zero));

        filter2.Update(new Vector2(2.0f, 4.0f), double.PositiveInfinity, out Vector2 p2Inf, out Vector2 v2Inf);
        Assert.That(p2Inf, Is.EqualTo(new Vector2(1.0f, 2.0f)));
        Assert.That(v2Inf, Is.EqualTo(Vector2.zero));

        var filter3 = new KalmanFilter3D();
        filter3.Update(new Vector3(1.0f, 2.0f, 0.5f), 100.0, out _, out _);
        filter3.Update(new Vector3(1.0f, 2.0f, float.NaN), 120.0, out Vector3 p3, out Vector3 v3);
        Assert.That(p3, Is.EqualTo(new Vector3(1.0f, 2.0f, 0.5f)));
        Assert.That(v3, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void KalmanFilter2DAnd3DHandleZeroOrNegativeDeltaTimeSafely()
    {
        var filter2 = new KalmanFilter2D();
        filter2.Update(new Vector2(0.0f, 0.0f), 100.0, out _, out _);
        filter2.Update(new Vector2(1.0f, 1.0f), 120.0, out Vector2 p2, out Vector2 v2);
        Assert.That(v2.x, Is.GreaterThan(0.0f));

        filter2.Update(new Vector2(9.0f, 9.0f), 120.0, out Vector2 p2Zero, out Vector2 v2Zero);
        Assert.That(p2Zero, Is.EqualTo(p2));
        Assert.That(v2Zero, Is.EqualTo(Vector2.zero));

        filter2.Update(new Vector2(9.0f, 9.0f), 110.0, out Vector2 p2Back, out Vector2 v2Back);
        Assert.That(p2Back, Is.EqualTo(p2));
        Assert.That(v2Back, Is.EqualTo(Vector2.zero));

        var filter3 = new KalmanFilter3D();
        filter3.Update(new Vector3(0.0f, 0.0f, 0.2f), 100.0, out _, out _);
        filter3.Update(new Vector3(0.0f, 0.0f, 0.6f), 120.0, out Vector3 p3, out Vector3 v3);
        Assert.That(v3.z, Is.GreaterThan(0.0f));

        filter3.Update(new Vector3(0.0f, 0.0f, 2.0f), 120.0, out Vector3 p3Zero, out Vector3 v3Zero);
        Assert.That(p3Zero, Is.EqualTo(p3));
        Assert.That(v3Zero, Is.EqualTo(Vector3.zero));

        filter3.Update(new Vector3(0.0f, 0.0f, 2.0f), 90.0, out Vector3 p3Back, out Vector3 v3Back);
        Assert.That(p3Back, Is.EqualTo(p3));
        Assert.That(v3Back, Is.EqualTo(Vector3.zero));
    }
}
