using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GlobalDeformationLab.Core;

namespace GlobalDeformationLab.Rendering;

public static class MeshRenderFactory
{
    public static GeometryModel3D CreateModel(MeshData mesh, IReadOnlyList<Vector3> positions)
    {
        var geometry = CreateGeometry(mesh.TriangleIndices, positions);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(203, 222, 213))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(238, 247, 242)), 42));

        return new GeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            BackMaterial = material
        };
    }

    public static MeshGeometry3D CreateSphere(Vector3 center, double radius, int longitude = 10, int latitude = 7)
    {
        var positions = new Point3DCollection();
        var normals = new Vector3DCollection();
        var indices = new Int32Collection();

        for (var lat = 0; lat <= latitude; lat++)
        {
            var theta = Math.PI * lat / latitude;
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);

            for (var lon = 0; lon <= longitude; lon++)
            {
                var phi = 2 * Math.PI * lon / longitude;
                var normal = new Vector3D(
                    sinTheta * Math.Cos(phi),
                    cosTheta,
                    sinTheta * Math.Sin(phi));
                normals.Add(normal);
                positions.Add(new Point3D(
                    center.X + radius * normal.X,
                    center.Y + radius * normal.Y,
                    center.Z + radius * normal.Z));
            }
        }

        var rowLength = longitude + 1;
        for (var lat = 0; lat < latitude; lat++)
        {
            for (var lon = 0; lon < longitude; lon++)
            {
                var current = lat * rowLength + lon;
                var next = current + rowLength;
                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            Normals = normals,
            TriangleIndices = indices
        };
    }

    private static MeshGeometry3D CreateGeometry(int[] triangleIndices, IReadOnlyList<Vector3> positions)
    {
        var pointCollection = new Point3DCollection(positions.Count);
        foreach (var point in positions)
        {
            pointCollection.Add(new Point3D(point.X, point.Y, point.Z));
        }

        var indexCollection = new Int32Collection(triangleIndices);
        var normalCollection = CalculateNormals(triangleIndices, positions);

        return new MeshGeometry3D
        {
            Positions = pointCollection,
            TriangleIndices = indexCollection,
            Normals = normalCollection
        };
    }

    private static Vector3DCollection CalculateNormals(int[] indices, IReadOnlyList<Vector3> positions)
    {
        var normals = new Vector3[positions.Count];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = indices[i];
            var b = indices[i + 1];
            var c = indices[i + 2];
            if ((uint)a >= positions.Count || (uint)b >= positions.Count || (uint)c >= positions.Count)
            {
                continue;
            }

            var edge1 = positions[b] - positions[a];
            var edge2 = positions[c] - positions[a];
            var faceNormal = Vector3.Cross(edge1, edge2);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        var result = new Vector3DCollection(positions.Count);
        foreach (var normal in normals)
        {
            var value = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
            result.Add(new Vector3D(value.X, value.Y, value.Z));
        }

        return result;
    }
}
