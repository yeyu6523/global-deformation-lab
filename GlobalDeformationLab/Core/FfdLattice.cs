using System.Numerics;

namespace GlobalDeformationLab.Core;

public sealed class FfdLattice
{
    private const float Epsilon = 1e-6f;
    private readonly float[] _weightsX;
    private readonly float[] _weightsY;
    private readonly float[] _weightsZ;

    private FfdLattice(
        int gridSize,
        Bounds3 bounds,
        Vector3[] sourcePositions,
        Vector3[] controlPoints,
        float[] weightsX,
        float[] weightsY,
        float[] weightsZ)
    {
        GridSize = gridSize;
        Bounds = bounds;
        SourcePositions = sourcePositions;
        ControlPoints = controlPoints;
        _weightsX = weightsX;
        _weightsY = weightsY;
        _weightsZ = weightsZ;
    }

    public int GridSize { get; }

    public Bounds3 Bounds { get; }

    public Vector3[] SourcePositions { get; }

    public Vector3[] ControlPoints { get; }

    public static FfdLattice Create(IReadOnlyList<Vector3> source, int gridSize)
    {
        if (gridSize is < 2 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), "FFD 网格阶数必须为 2 到 5。 ");
        }

        var sourceCopy = source.ToArray();
        var bounds = Bounds3.FromPoints(sourceCopy);
        var size = bounds.Size;
        size.X = MathF.Max(size.X, Epsilon);
        size.Y = MathF.Max(size.Y, Epsilon);
        size.Z = MathF.Max(size.Z, Epsilon);

        var count = gridSize * gridSize * gridSize;
        var controls = new Vector3[count];
        var denominator = gridSize - 1f;

        for (var x = 0; x < gridSize; x++)
        {
            for (var y = 0; y < gridSize; y++)
            {
                for (var z = 0; z < gridSize; z++)
                {
                    controls[IndexOf(gridSize, x, y, z)] = new Vector3(
                        bounds.Min.X + size.X * x / denominator,
                        bounds.Min.Y + size.Y * y / denominator,
                        bounds.Min.Z + size.Z * z / denominator);
                }
            }
        }

        var weightsX = BuildWeights(sourceCopy, gridSize, bounds.Min.X, size.X, static p => p.X);
        var weightsY = BuildWeights(sourceCopy, gridSize, bounds.Min.Y, size.Y, static p => p.Y);
        var weightsZ = BuildWeights(sourceCopy, gridSize, bounds.Min.Z, size.Z, static p => p.Z);

        return new FfdLattice(gridSize, bounds, sourceCopy, controls, weightsX, weightsY, weightsZ);
    }

    public FfdLattice Clone()
    {
        return new FfdLattice(
            GridSize,
            Bounds,
            SourcePositions.ToArray(),
            ControlPoints.ToArray(),
            _weightsX.ToArray(),
            _weightsY.ToArray(),
            _weightsZ.ToArray());
    }

    public Vector3[] Evaluate()
    {
        var output = new Vector3[SourcePositions.Length];
        var n = GridSize;

        for (var vertex = 0; vertex < SourcePositions.Length; vertex++)
        {
            var sum = Vector3.Zero;
            var weightOffset = vertex * n;

            for (var x = 0; x < n; x++)
            {
                var wx = _weightsX[weightOffset + x];
                for (var y = 0; y < n; y++)
                {
                    var wxy = wx * _weightsY[weightOffset + y];
                    for (var z = 0; z < n; z++)
                    {
                        var weight = wxy * _weightsZ[weightOffset + z];
                        sum += ControlPoints[IndexOf(n, x, y, z)] * weight;
                    }
                }
            }

            output[vertex] = sum;
        }

        return output;
    }

    public (int X, int Y, int Z) CoordinatesOf(int index)
    {
        var x = index / (GridSize * GridSize);
        var remainder = index % (GridSize * GridSize);
        var y = remainder / GridSize;
        var z = remainder % GridSize;
        return (x, y, z);
    }

    public static int IndexOf(int gridSize, int x, int y, int z)
    {
        return x * gridSize * gridSize + y * gridSize + z;
    }

    private static float[] BuildWeights(
        IReadOnlyList<Vector3> points,
        int gridSize,
        float min,
        float length,
        Func<Vector3, float> coordinate)
    {
        var weights = new float[points.Count * gridSize];
        var degree = gridSize - 1;

        for (var vertex = 0; vertex < points.Count; vertex++)
        {
            var t = Math.Clamp((coordinate(points[vertex]) - min) / length, 0f, 1f);
            for (var i = 0; i < gridSize; i++)
            {
                weights[vertex * gridSize + i] = Bernstein(degree, i, t);
            }
        }

        return weights;
    }

    private static float Bernstein(int degree, int i, float t)
    {
        return Binomial(degree, i) * MathF.Pow(t, i) * MathF.Pow(1f - t, degree - i);
    }

    private static int Binomial(int n, int k)
    {
        var value = 1;
        for (var i = 1; i <= k; i++)
        {
            value = value * (n - (k - i)) / i;
        }

        return value;
    }
}
