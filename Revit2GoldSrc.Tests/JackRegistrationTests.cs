using System;
using System.IO;
using System.Linq;
using System.Text;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Revit2GoldSrc.Compilation;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class JackRegistrationTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "r2g-jack-" + Guid.NewGuid().ToString("N"));
        private string Map => Path.Combine(dir, "room.map"); private string Config => Path.Combine(dir, "VDKGameCfg.ini"); private string Wad => Path.Combine(dir, "test.wad");
        public JackRegistrationTests()
        {
            Directory.CreateDirectory(dir); ExportTests.WriteWad(Wad); var options = new ExportOptions { WadPath = Wad, CenterOrigin = false, GenerateEntities = false }; options.Textures.Add(BimCategory.Floor, "TEST");
            var floor = new BimElement(1, BimCategory.Floor, new[] { new Box(Frame.Identity, new Vec3(0, 0, 0), new Vec3(4, 5, .2)) }); Exporter.Publish(Exporter.Prepare(new[] { floor }, options, new ExportReport()), Map);
            File.WriteAllText(Config, "; unchanged comment\r\n[Config0]\r\nTitle=Half-Life\r\nEnabled=true\r\nMapProfile=vpHalfLife\r\nMapTool0=unchanged.exe\r\nParcel0=D:/existing.wad\r\n\r\n[Config1]\r\nTitle=Other\r\nMapProfile=other\r\nParcel0=other.wad\r\n", new UTF8Encoding(false));
        }
        public void Dispose() { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(dir, true); }
        [Fact]
        public void RegistrationPreservesOtherSettingsAndCreatesBackup()
        {
            var before = File.ReadAllBytes(Config); var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); Assert.Equal(1, plan.Added); var result = JackWadRegistration.Apply(plan, dir, () => false); Assert.True(result.Installed); Assert.Equal(before, File.ReadAllBytes(result.BackupPath!)); string text = File.ReadAllText(Config); Assert.Contains("MapTool0=unchanged.exe\r\n", text); Assert.Contains("Parcel1=" + Wad.Replace('\\', '/') + "\r\n", text); Assert.EndsWith("Parcel0=other.wad\r\n", text);
        }
        [Fact]
        public void NormalizedWadPathsAreNotDuplicated()
        { File.AppendAllText(Config, "[Config2]\r\nTitle=Half-Life copy\r\nMapProfile=vpHalfLife\r\nParcel3=" + Wad.ToUpperInvariant() + "\r\n"); Assert.Equal(2, JackWadRegistration.Profiles(Config).Count); var plan = JackWadRegistration.Prepare(Map, Config, "Config2"); Assert.Equal(0, plan.Added); var before = File.ReadAllBytes(Config); Assert.True(JackWadRegistration.Apply(plan, dir, () => false).Installed); Assert.Equal(before, File.ReadAllBytes(Config)); }
        [Fact]
        public void RunningJackPreventsMutation()
        { var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); var before = File.ReadAllBytes(Config); Assert.Throws<InvalidOperationException>(() => JackWadRegistration.Apply(plan, dir, () => true)); Assert.Equal(before, File.ReadAllBytes(Config)); Assert.Empty(Directory.GetFiles(dir, "*.bak")); }
        [Fact]
        public void ConfigChangedAfterPreparationIsNotOverwritten()
        { var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); File.AppendAllText(Config, "; external edit"); var before = File.ReadAllBytes(Config); Assert.Throws<InvalidOperationException>(() => JackWadRegistration.Apply(plan, dir, () => false)); Assert.Equal(before, File.ReadAllBytes(Config)); }
        [Fact]
        public void ReadOnlyConfigProducesProposedFileWithoutChangingOriginal()
        { var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); var before = File.ReadAllBytes(Config); File.SetAttributes(Config, FileAttributes.ReadOnly); var result = JackWadRegistration.Apply(plan, Path.Combine(dir, "fallback"), () => false); Assert.False(result.Installed); Assert.True(File.Exists(result.ProposedPath)); Assert.Contains("Parcel1=", File.ReadAllText(result.ProposedPath!)); Assert.Equal(before, File.ReadAllBytes(Config)); }
        [Fact]
        public void NonHalfLifeProfileAndMissingTextureAreRejected()
        { Assert.Throws<ArgumentException>(() => JackWadRegistration.Prepare(Map, Config, "Config1")); File.WriteAllText(Map, File.ReadAllText(Map).Replace(" TEST ", " MISSING ")); Assert.Throws<ArgumentException>(() => JackWadRegistration.Prepare(Map, Config, "Config0")); Assert.DoesNotContain("Parcel1=", File.ReadAllText(Config)); }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Utf8BomAndLineEndingsArePreserved(bool bom)
        { File.WriteAllText(Config, "[Config0]\nTitle=Half-Life á\nMapProfile=vpHalfLife\n", new UTF8Encoding(bom)); var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); JackWadRegistration.Apply(plan, dir, () => false); var bytes = File.ReadAllBytes(Config); Assert.Equal(bom, bytes.Length > 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191); Assert.Contains("Half-Life á\n", File.ReadAllText(Config)); Assert.DoesNotContain("\r", File.ReadAllText(Config)); }
        [Fact]
        public void JackOpeningDuringApplyDoesNotWriteConfig()
        { var plan = JackWadRegistration.Prepare(Map, Config, "Config0"); int calls = 0; var before = File.ReadAllBytes(Config); Assert.Throws<InvalidOperationException>(() => JackWadRegistration.Apply(plan, dir, () => ++calls > 1)); Assert.Equal(before, File.ReadAllBytes(Config)); Assert.Empty(Directory.GetFiles(dir, "*.tmp")); }
    }
}
