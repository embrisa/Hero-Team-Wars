using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Wc3MapEngine.Core.Validation;
using Xunit;

namespace Wc3MapEngine.Tests.Validation;

public sealed class Phase3ValidationTests
{
    [Fact]
    public void BaselinePassesBuildValidationWithOpaqueRiskInformation()
    {
        var source = FindSourceMap();
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "canonical.json");
        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, MapInspector.Inspect(source));
            var report = MapValidator.ValidateCanonical(canonical, source, new JsonObject
            {
                ["project_id"] = "hero-team-wars",
                ["protected_region_names"] = new JsonArray("Arena_A", "Camp_A_Player1"),
                ["explicit_teams"] = new JsonArray(new JsonArray(1, 2), new JsonArray(3, 4))
            });

            Assert.True(report["buildable"]!.GetValue<bool>());
            Assert.Equal(ValidationPipeline.ValidatorVersion, report["validator_version"]!.GetValue<string>());
            Assert.Contains(report["findings"]!.AsArray().OfType<JsonObject>(), x => x["code"]!.GetValue<string>() == "OPAQUE_MEMBER_PRESERVED");
            Assert.Contains(report["findings"]!.AsArray().OfType<JsonObject>(), x => x["code"]!.GetValue<string>() == "SCRIPT_CONNECTION_OPAQUE");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalValidatorRejectsDuplicateRegionAndInvalidImport()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        var regions = model["regions"]!.AsArray();
        regions.Add(regions[0]!.DeepClone());
        model["imports"] = new JsonArray(new JsonObject { ["path"] = "../escape", ["size_bytes"] = 1 });
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "invalid.json");
        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, model);
            var report = MapValidator.ValidateCanonical(canonical);
            var codes = report["findings"]!.AsArray().OfType<JsonObject>().Select(x => x["code"]!.GetValue<string>()).ToHashSet();
            Assert.False(report["buildable"]!.GetValue<bool>());
            Assert.Contains("REGION_NAME_INVALID", codes);
            Assert.Contains("IMPORT_PATH_INVALID", codes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalValidatorRejectsMalformedRawcodeAndMissingTeam()
    {
        var source = FindSourceMap();
        var model = MapInspector.Inspect(source);
        model["object_data"] = new JsonArray(new JsonObject { ["rawcode"] = "bad" });
        model["forces"] = new JsonArray(new JsonObject { ["index"] = 0, ["player_ids"] = new JsonArray(1, 2) });
        var directory = Path.Combine(Path.GetTempPath(), "wc3-map-mcp-phase3", Guid.NewGuid().ToString("N"));
        var canonical = Path.Combine(directory, "invalid-team.json");
        try
        {
            Directory.CreateDirectory(directory);
            JsonUtilities.WriteAtomic(canonical, model);
            var report = MapValidator.ValidateCanonical(canonical, source, new JsonObject { ["project_id"] = "hero-team-wars" });
            var codes = report["findings"]!.AsArray().OfType<JsonObject>().Select(x => x["code"]!.GetValue<string>()).ToHashSet();
            Assert.False(report["buildable"]!.GetValue<bool>());
            Assert.Contains("RAWCODE_INVALID", codes);
            Assert.Contains("EXPLICIT_TEAM_ASSIGNMENT_MISSING", codes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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
