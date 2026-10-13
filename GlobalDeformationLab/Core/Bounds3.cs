using System.Numerics;

namespace GlobalDeformationLab.Core;

public readonly record struct Bounds3(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 Center => (Min + Max) * 0.5f;

    public static Bounds3 FromPoints(IReadOnlyList<Vector3> points)
    {
        if (points.Count == 0)
        {
            throw new ArgumentException("至少需要一个顶点。", nameof(points));
        }

        var min = points[0];
        var max = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        return new Bounds3(min, max);
    }
}
