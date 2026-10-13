using System.Numerics;
using GlobalDeformationLab.Core;

namespace GlobalDeformationLab.Tests;

public sealed class FfdLatticeTests
{
    [Fact]
    public void InitialLattice_ReproducesSourcePositions()
    {
        var points = CreateBoxPoints();
        var lattice = FfdLattice.Create(points, 4);

        var result = lattice.Evaluate();

        for (var i = 0; i < points.Length; i++)
        {
            Assert.True(Vector3.Distance(points[i], result[i]) < 1e-5f);
        }
    }

    [Fact]
    public void MovingCornerControlPoint_MovesMatchingCorner()
    {
        var points = CreateBoxPoints();
        var lattice = FfdLattice.Create(points, 3);
        var corner = FfdLattice.IndexOf(3, 2, 2, 2);
        lattice.ControlPoints[corner] += new Vector3(0.5f, 0.25f, -0.75f);

        var result = lattice.Evaluate();

        Assert.True(Vector3.Distance(points[^1] + new Vector3(0.5f, 0.25f, -0.75f), result[^1]) < 1e-5f);
        Assert.True(Vector3.Distance(points[0], result[0]) < 1e-5f);
    }

    [Fact]
    public void GridCoordinates_RoundTripToIndex()
    {
        var lattice = FfdLattice.Create(CreateBoxPoints(), 5);
        var index = FfdLattice.IndexOf(5, 3, 2, 4);
        Assert.Equal((3, 2, 4), lattice.CoordinatesOf(index));
    }

    private static Vector3[] CreateBoxPoints()
    {
        return
        [
            new Vector3(-1, -1, -1),
            new Vector3(1, -1, -1),
            new Vector3(-1, 1, -1),
            new Vector3(1, 1, -1),
            new Vector3(-1, -1, 1),
            new Vector3(1, -1, 1),
            new Vector3(-1, 1, 1),
            new Vector3(1, 1, 1)
        ];
    }
}
