using System.Numerics;
using GlobalDeformationLab.Core;

namespace GlobalDeformationLab.Tests;

public sealed class DeformationMathTests
{
    [Fact]
    public void Taper_KeepsBottomAndScalesTop()
    {
        var points = new[]
        {
            new Vector3(-1, -1, 0),
            new Vector3(1, -1, 0),
            new Vector3(-1, 1, 0),
            new Vector3(1, 1, 0)
        };

        var result = DeformationMath.Taper(points, 0.5f);

        Assert.Equal(points[0], result[0]);
        Assert.Equal(points[1], result[1]);
        Assert.Equal(-0.5f, result[2].X, 4);
        Assert.Equal(0.5f, result[3].X, 4);
    }

    [Fact]
    public void Twist_RotatesTopAroundBoundingBoxCenter()
    {
        var points = new[]
        {
            new Vector3(1, 0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(1, 2, 0),
            new Vector3(-1, 2, 0)
        };

        var result = DeformationMath.Twist(points, 90);

        Assert.Equal(points[0], result[0]);
        Assert.Equal(0f, result[2].X, 4);
        Assert.Equal(1f, result[2].Z, 4);
    }

    [Fact]
    public void Squeeze_PreservesApproximateScaleVolume()
    {
        var points = new[]
        {
            new Vector3(-1, -1, -1),
            new Vector3(1, 1, 1)
        };

        var result = DeformationMath.Squeeze(points, 1f);
        var bounds = Bounds3.FromPoints(result);

        var volume = bounds.Size.X * bounds.Size.Y * bounds.Size.Z;
        Assert.Equal(8f, volume, 3);
    }

    [Fact]
    public void Bend_WithZeroAngleReturnsOriginalPoints()
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(2, 3, 4) };
        Assert.Equal(points, DeformationMath.Bend(points, 0));
    }
}
