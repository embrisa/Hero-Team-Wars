using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class MapInspectionTests
{
    [Fact]
    public async Task HeroTeamWarsInventoryIsStableAndSourceIsUnchanged()
    {
        var source = FindSourceMap();
        var before = await Hashing.HashFileAsync(source);
        var first = MapArchive.Read(source);
        var second = MapArchive.Read(source);

        Assert.Equal(17, first.Members.Count);
        Assert.Equal(first.Members.Select(x => (x.Path, x.Sha256)), second.Members.Select(x => (x.Path, x.Sha256)));
        var inspection = MapInspector.Inspect(source);
        Assert.Equal("Hero Team Wars - Two Arena MVP", inspection["metadata"]![0]!["value"]!.GetValue<string>());
        var after = await Hashing.HashFileAsync(source);
        Assert.Equal(before.Sha256, after.Sha256);
        Assert.Equal(before.Size, after.Size);
    }

    [Fact]
    public void TruncatedArchiveIsRejectedWithoutWritingBesideSource()
    {
        var source = FindSourceMap();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-tests", Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryDirectory, "truncated.w3m");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(temporaryPath, File.ReadAllBytes(source).Take(32).ToArray());
            var exception = Assert.Throws<EngineException>(() => MapArchive.Read(temporaryPath));
            Assert.Contains(exception.Code, new[] { "PARSE_FAILED", "FILE_NOT_FOUND" });
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // War3Net can retain a handle when rejecting a truncated archive on Windows.
                // The unique temp directory is outside the workspace and will be reclaimed by the OS.
            }
        }
    }

    private static string FindSourceMap()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var candidate = Path.Combine(current.FullName, "map", "HeroTeamWars_M0_2Arena.w3m");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("The local Hero Team Wars source fixture was not found.");
    }
}
