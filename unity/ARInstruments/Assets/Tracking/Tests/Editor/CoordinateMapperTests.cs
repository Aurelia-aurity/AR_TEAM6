using NUnit.Framework;
using UnityEngine;

public sealed class CoordinateMapperTests
{
    private static readonly Vector3 WorldMin = new Vector3(-5.0f, -3.0f, 0.0f);
    private static readonly Vector3 WorldMax = new Vector3(5.0f, 3.0f, 2.0f);

    [TestCase(0, false, 0f, 0f, -5f, 3f)]
    [TestCase(0, false, 160f, 90f, 5f, -3f)]
    [TestCase(90, false, 0f, 90f, -5f, 3f)]
    [TestCase(90, false, 0f, 0f, 5f, 3f)]
    [TestCase(180, false, 160f, 90f, -5f, 3f)]
    [TestCase(180, false, 0f, 0f, 5f, -3f)]
    [TestCase(270, false, 160f, 0f, -5f, 3f)]
    [TestCase(270, false, 160f, 90f, 5f, 3f)]
    // VerticallyMirrored MUST apply BEFORE ClockwiseRotation:
    // Top-left (0,0) -> flipY (0,90) -> CW 90 -> (0,0) -> (-5, 3).
    [TestCase(90, true, 0f, 0f, -5f, 3f)]
    // Bottom-left (0,90) -> flipY (0,0) -> CW 90 -> (90,0) -> (5, 3).
    [TestCase(90, true, 0f, 90f, 5f, 3f)]
    // Top-left (0,0) -> flipY (0,90) -> CW 180 -> (160,0) -> (5, 3).
    [TestCase(180, true, 0f, 0f, 5f, 3f)]
    // Bottom-right (160,90) -> flipY (160,0) -> CW 270 -> (0,0) -> (-5, 3).
    [TestCase(270, true, 160f, 90f, -5f, 3f)]
    // Negative and >= 360 angles normalize cleanly.
    [TestCase(-90, true, 160f, 90f, -5f, 3f)]
    [TestCase(450, true, 0f, 0f, -5f, 3f)]
    public void OrientationNormalizationAppliesVerticalMirrorBeforeClockwiseRotation(
        int rotation,
        bool verticallyMirrored,
        float rawX,
        float rawY,
        float expectedWorldX,
        float expectedWorldY)
    {
        var mapper = new CoordinateMapper();
        bool swapsAxes = ((rotation % 360 + 360) % 360) == 90 || ((rotation % 360 + 360) % 360) == 270;
        Vector2 orientedSize = swapsAxes ? new Vector2(90f, 160f) : new Vector2(160f, 90f);

        mapper.SetupCamera(160f, 90f, frontFacing: false, rotation, verticallyMirrored);
        mapper.UpdateCalibration(Vector2.zero, orientedSize, 100.0, 1000.0, WorldMin, WorldMax);

        Assert.That(mapper.TryMapToWorld(new Vector2(rawX, rawY), 550.0, out Vector3 world), Is.True);
        Assert.That(world.x, Is.EqualTo(expectedWorldX).Within(0.001f));
        Assert.That(world.y, Is.EqualTo(expectedWorldY).Within(0.001f));
        Assert.That(world.z, Is.EqualTo(1.0f).Within(0.001f));
    }

    [Test]
    public void FrontFacingCameraNeverFlipsWorldX()
    {
        var backMapper = new CoordinateMapper();
        var frontMapper = new CoordinateMapper();

        backMapper.SetupCamera(1280f, 720f, frontFacing: false);
        frontMapper.SetupCamera(1280f, 720f, frontFacing: true);
        backMapper.UpdateCalibration(Vector2.zero, new Vector2(1280f, 720f), 100.0, 1000.0, WorldMin, WorldMax);
        frontMapper.UpdateCalibration(Vector2.zero, new Vector2(1280f, 720f), 100.0, 1000.0, WorldMin, WorldMax);

        var leftSidePixel = new Vector2(320f, 180f);
        Vector3 backWorld = backMapper.MapToWorld(leftSidePixel, 550.0);
        Vector3 frontWorld = frontMapper.MapToWorld(leftSidePixel, 550.0);

        Assert.That(frontWorld.x, Is.EqualTo(backWorld.x).Within(0.0001f));
        Assert.That(frontWorld.x, Is.LessThan(0.0f));
        Assert.That(frontWorld.y, Is.EqualTo(backWorld.y).Within(0.0001f));
        Assert.That(frontWorld.z, Is.EqualTo(backWorld.z).Within(0.0001f));
    }

    [TestCase(0.0)]
    [TestCase(-10.0)]
    [TestCase(99.9)]
    [TestCase(1000.1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidOrOutOfRangeAreaIsRejectedWithoutClamping(double invalidArea)
    {
        var mapper = new CoordinateMapper();
        mapper.SetupCamera(1280f, 720f, frontFacing: false);
        mapper.UpdateCalibration(Vector2.zero, new Vector2(1280f, 720f), 100.0, 1000.0, WorldMin, WorldMax);

        Assert.That(mapper.IsValidArea(invalidArea), Is.False);
        Assert.That(mapper.TryMapToWorld(new Vector2(640f, 360f), invalidArea, out Vector3 world), Is.False);
        Assert.That(world, Is.EqualTo(Vector3.zero));
        Assert.That(mapper.ComputeConfidence(invalidArea, 50), Is.EqualTo(0.0f));
    }

    [Test]
    public void ValidAreaRangeComputesUnclampedDepthAndContinuousConfidence()
    {
        var mapper = new CoordinateMapper();
        mapper.SetupCamera(1280f, 720f, frontFacing: false);
        mapper.UpdateCalibration(Vector2.zero, new Vector2(1280f, 720f), 100.0, 1000.0, WorldMin, WorldMax);

        Assert.That(mapper.TryMapToWorld(new Vector2(640f, 360f), 100.0, out Vector3 nearMin), Is.True);
        Assert.That(mapper.TryMapToWorld(new Vector2(640f, 360f), 550.0, out Vector3 mid), Is.True);
        Assert.That(mapper.TryMapToWorld(new Vector2(640f, 360f), 1000.0, out Vector3 nearMax), Is.True);

        Assert.That(nearMin.z, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(mid.z, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(nearMax.z, Is.EqualTo(2.0f).Within(0.0001f));

        float confMin = mapper.ComputeConfidence(100.0, 50);
        float confMid = mapper.ComputeConfidence(550.0, 50);
        float confMax = mapper.ComputeConfidence(1000.0, 50);
        Assert.That(confMin, Is.GreaterThan(0.0f).And.LessThan(confMid));
        Assert.That(confMid, Is.LessThan(confMax));
        Assert.That(confMax, Is.EqualTo(1.0f).Within(0.0001f));
    }

    [Test]
    public void InvalidInputsAndNonRightAngleRotationsAreRejected()
    {
        var mapper = new CoordinateMapper();
        mapper.SetupCamera(1280f, 720f, frontFacing: false, 45, false);
        mapper.UpdateCalibration(Vector2.zero, new Vector2(1280f, 720f), 100.0, 1000.0, WorldMin, WorldMax);

        Assert.That(mapper.TryMapToWorld(new Vector2(640f, 360f), 500.0, out _), Is.False);

        mapper.SetupCamera(1280f, 720f, frontFacing: false, 0, false);
        Assert.That(mapper.TryMapToWorld(new Vector2(float.NaN, 360f), 500.0, out _), Is.False);
        Assert.That(mapper.MapVelocity(new Vector3(float.NaN, 1f, 1f)), Is.EqualTo(Vector3.zero));
    }
}
