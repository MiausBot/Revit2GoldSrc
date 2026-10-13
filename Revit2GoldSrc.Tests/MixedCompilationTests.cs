using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Revit2GoldSrc.Compilation;
using Revit2GoldSrc.GoldSrc;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class MixedCompilationTests
    {
        [Fact]
        public async Task MixedFamiliesMaterialsAndNonSolidModelCompileToBsp30()
        {
            string folder = Path.Combine(Path.GetTempPath(), "r2g-mixed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures"); var map = MapValidator.Parse(File.ReadAllText(Path.Combine(fixtures, "families_demo.map")), .0001);
                map.Entities[0].Properties["wad"] = Path.Combine(fixtures, "families_demo_materiales.wad").Replace('\\', '/'); var illusion = Assert.Single(map.Entities, e => e.Properties["classname"] == "func_illusionary"); Assert.Equal("-1", illusion.Properties["skin"]);
                string path = Path.Combine(folder, "mixed.map"); File.WriteAllText(path, MapSerializer.Serialize(map, .0001)); var result = await BspCompiler.CompileAsync(path, CompilerOptions.Detect()); Assert.True(result.Published); var bsp = BspInspector.Read(result.BspPath); Assert.Equal(30, bsp.Version); Assert.Equal(2, bsp.Models); Assert.Contains("func_illusionary", bsp.Entities); Assert.Contains("info_player_start", bsp.Entities); Assert.Contains("\"skin\" \"-1\"", bsp.Entities);
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
