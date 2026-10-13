using System.Numerics;

namespace GlobalDeformationLab.Core;

public sealed record MeshData(string Name, Vector3[] Positions, int[] TriangleIndices)
{
    public MeshData CloneWithPositions(Vector3[] positions) => new(Name, positions, TriangleIndices.ToArray());
}
