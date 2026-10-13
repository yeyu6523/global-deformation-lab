using System.Numerics;

namespace GlobalDeformationLab.Core;

public static class DeformationMath
{
    private const float Epsilon = 1e-6f;

    public static Vector3[] Taper(IReadOnlyList<Vector3> source, float amount)
    {
        var bounds = Bounds3.FromPoints(source);
        var height = MathF.Max(bounds.Size.Y, Epsilon);
        var center = bounds.Center;
        var output = new Vector3[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var p = source[i];
            var t = Math.Clamp((p.Y - bounds.Min.Y) / height, 0f, 1f);
            var scale = MathF.Max(0.05f, 1f - amount * t);
            output[i] = new Vector3(
                center.X + (p.X - center.X) * scale,
                p.Y,
                center.Z + (p.Z - center.Z) * scale);
        }

        return output;
    }

    public static Vector3[] Twist(IReadOnlyList<Vector3> source, float angleDegrees)
    {
        var bounds = Bounds3.FromPoints(source);
        var height = MathF.Max(bounds.Size.Y, Epsilon);
        var center = bounds.Center;
        var totalAngle = DegreesToRadians(angleDegrees);
        var output = new Vector3[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var p = source[i];
            var t = Math.Clamp((p.Y - bounds.Min.Y) / height, 0f, 1f);
            var angle = totalAngle * t;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            var x = p.X - center.X;
            var z = p.Z - center.Z;
            output[i] = new Vector3(
                center.X + x * cos - z * sin,
                p.Y,
                center.Z + x * sin + z * cos);
        }

        return output;
    }

    public static Vector3[] Bend(IReadOnlyList<Vector3> source, float angleDegrees)
    {
        var angle = DegreesToRadians(angleDegrees);
        if (MathF.Abs(angle) < Epsilon)
        {
            return source.ToArray();
        }

        var bounds = Bounds3.FromPoints(source);
        var height = MathF.Max(bounds.Size.Y, Epsilon);
        var center = bounds.Center;
        var radius = height / angle;
        var output = new Vector3[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var p = source[i];
            var t = (p.Y - bounds.Min.Y) / height;
            var theta = angle * t;
            var cos = MathF.Cos(theta);
            var sin = MathF.Sin(theta);
            var offsetX = p.X - center.X;
            var centerX = center.X + radius * (1f - cos);
            var centerY = bounds.Min.Y + radius * sin;

            output[i] = new Vector3(
                centerX + offsetX * cos,
                centerY - offsetX * sin,
                p.Z);
        }

        return output;
    }

    public static Vector3[] Squeeze(IReadOnlyList<Vector3> source, float yScaleDelta)
    {
        var bounds = Bounds3.FromPoints(source);
        var center = bounds.Center;
        var scaleY = MathF.Max(0.2f, 1f + yScaleDelta);
        var scaleXZ = 1f / MathF.Sqrt(scaleY);
        var output = new Vector3[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var p = source[i] - center;
            output[i] = center + new Vector3(p.X * scaleXZ, p.Y * scaleY, p.Z * scaleXZ);
        }

        return output;
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
}
