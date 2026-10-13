using GlobalDeformationLab.IO;

namespace GlobalDeformationLab.Tests;

public sealed class ObjMeshServiceTests
{
    [Fact]
    public void LoadAndSave_RoundTripsGeometry()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "F-16.obj");
        var exported = Path.Combine(Path.GetTempPath(), $"deformation-lab-{Guid.NewGuid():N}.obj");

        try
        {
            var mesh = ObjMeshService.Load(source);
            Assert.NotEmpty(mesh.Positions);
            Assert.NotEmpty(mesh.TriangleIndices);

            ObjMeshService.Save(exported, mesh, mesh.Positions);
            var roundTrip = ObjMeshService.Load(exported);

            Assert.Equal(mesh.Positions.Length, roundTrip.Positions.Length);
            Assert.Equal(mesh.TriangleIndices.Length, roundTrip.TriangleIndices.Length);
        }
        finally
        {
            if (File.Exists(exported))
            {
                File.Delete(exported);
            }
        }
    }
}
