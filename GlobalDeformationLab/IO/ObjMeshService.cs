using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media.Media3D;
using GlobalDeformationLab.Core;
using HelixToolkit.Wpf;

namespace GlobalDeformationLab.IO;

public static class ObjMeshService
{
    public static MeshData Load(string filePath)
    {
        var importer = new ModelImporter();
        var model = importer.Load(filePath);
        if (model is null)
        {
            throw new InvalidDataException("无法从 OBJ 文件创建三维模型。 ");
        }

        var positions = new List<Vector3>();
        var indices = new List<int>();
        CollectGeometry(model, Matrix3D.Identity, positions, indices);

        if (positions.Count == 0 || indices.Count < 3)
        {
            throw new InvalidDataException("OBJ 文件不包含可显示的三角网格。 ");
        }

        Normalize(positions);
        return new MeshData(Path.GetFileName(filePath), positions.ToArray(), indices.ToArray());
    }

    public static void Save(string filePath, MeshData mesh, IReadOnlyList<Vector3> positions)
    {
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));
        writer.WriteLine("# Global Deformation Lab export");
        writer.WriteLine($"# vertices {positions.Count}");
        writer.WriteLine($"# triangles {mesh.TriangleIndices.Length / 3}");

        foreach (var point in positions)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"v {point.X:R} {point.Y:R} {point.Z:R}"));
        }

        for (var i = 0; i < mesh.TriangleIndices.Length; i += 3)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"f {mesh.TriangleIndices[i] + 1} {mesh.TriangleIndices[i + 1] + 1} {mesh.TriangleIndices[i + 2] + 1}"));
        }
    }

    private static void CollectGeometry(
        Model3D model,
        Matrix3D parentTransform,
        ICollection<Vector3> positions,
        ICollection<int> indices)
    {
        var combined = model.Transform?.Value ?? Matrix3D.Identity;
        combined.Append(parentTransform);

        if (model is GeometryModel3D { Geometry: MeshGeometry3D mesh })
        {
            var offset = positions.Count;
            foreach (var point in mesh.Positions)
            {
                var transformed = combined.Transform(point);
                positions.Add(new Vector3((float)transformed.X, (float)transformed.Y, (float)transformed.Z));
            }

            if (mesh.TriangleIndices.Count > 0)
            {
                foreach (var index in mesh.TriangleIndices)
                {
                    indices.Add(offset + index);
                }
            }
            else
            {
                for (var i = 0; i + 2 < mesh.Positions.Count; i += 3)
                {
                    indices.Add(offset + i);
                    indices.Add(offset + i + 1);
                    indices.Add(offset + i + 2);
                }
            }
        }

        if (model is not Model3DGroup group)
        {
            return;
        }

        foreach (var child in group.Children)
        {
            CollectGeometry(child, combined, positions, indices);
        }
    }

    private static void Normalize(IList<Vector3> positions)
    {
        var bounds = Bounds3.FromPoints(positions.ToArray());
        var maxExtent = MathF.Max(bounds.Size.X, MathF.Max(bounds.Size.Y, bounds.Size.Z));
        if (maxExtent < 1e-6f)
        {
            throw new InvalidDataException("OBJ 模型的包围框尺寸为零。 ");
        }

        var center = bounds.Center;
        var scale = 4f / maxExtent;
        for (var i = 0; i < positions.Count; i++)
        {
            positions[i] = (positions[i] - center) * scale;
        }
    }
}
