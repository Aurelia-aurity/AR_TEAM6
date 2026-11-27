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

    [Test]
    public void KalmanFilter3DDoesNotOvershootPlaneWhenMalletStopsInFront()
    {
        var filter = new KalmanFilter3D();
        filter.SetNoiseParameters(0.05f, 0.5f);

        filter.Update(new Vector3(0.0f, 0.0f, 0.40f), 0.0, out Vector3 p0, out _);
        filter.Update(new Vector3(0.0f, 0.0f, 0.06f), 33.0, out Vector3 p1, out _);
        filter.Update(new Vector3(0.0f, 0.0f, 0.06f), 66.0, out Vector3 p2, out _);

        Assert.That(p0.z, Is.EqualTo(0.40f).Within(0.0001f));
        Assert.That(p1.z, Is.GreaterThanOrEqualTo(0.06f));
        Assert.That(p2.z, Is.GreaterThanOrEqualTo(0.06f),
            "Stopping at 0.06m in front of Z=0 must not overshoot into negative Z.");

        // Actual forward crossing past Z=0 still crosses into negative Z.
        filter.Update(new Vector3(0.0f, 0.0f, -0.02f), 99.0, out Vector3 p3, out Vector3 v3);
        Assert.That(p3.z, Is.LessThan(0.0f));
        Assert.That(v3.z, Is.LessThan(0.0f));
    }

    [Test]
    public void KalmanFilter3DStaysInFrontOf45DegreeTiltedPlaneWhenObservationsStayInFront()
    {
        var filter = new KalmanFilter3D();
        filter.SetNoiseParameters(0.05f, 0.5f);

        Vector3 planePoint = new Vector3(0.0f, 0.0f, 0.5f);
        Vector3 planeNormal = new Vector3(1.0f, 0.0f, 1.0f).normalized;

        // Observations at 33ms intervals: m1 and m2 are both +3.5355cm in front of the 45-deg plane.
        filter.Update(new Vector3(0.20f, 0.0f, 0.80f), 0.0, out Vector3 p0, out _);
        filter.Update(new Vector3(-0.25f, 0.0f, 0.80f), 33.0, out Vector3 p1, out _);
        filter.Update(new Vector3(0.35f, 0.0f, 0.20f), 66.0, out Vector3 p2, out _);

        Assert.That(Vector3.Dot(p0 - planePoint, planeNormal), Is.GreaterThanOrEqualTo(0.035f));
        Assert.That(Vector3.Dot(p1 - planePoint, planeNormal), Is.GreaterThanOrEqualTo(0.035f));
        Assert.That(Vector3.Dot(p2 - planePoint, planeNormal), Is.GreaterThanOrEqualTo(0.035f),
            "Line-segment projection must keep filtered output in front of arbitrary tilted planes.");

        filter.Update(new Vector3(-0.15f, 0.0f, 0.25f), 99.0, out Vector3 p3, out _);
        Assert.That(Vector3.Dot(p3 - planePoint, planeNormal), Is.LessThan(0.0f));
    }
}
