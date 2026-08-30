using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class SemanticDiffTests
{
    [Fact]
    public void CanonicalComparisonExcludesSourceAndArchiveContainerMetadata()
    {
        var before = Canonical("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var after = Canonical("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var member = (JsonObject)((JsonArray)after["archive_members"]!)[0]!;
        member["compressed_size_bytes"] = 1;
        member["flags"] = 123;
        ((JsonArray)after["opaque_members"]!)[0]!["sha256"] = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        ((JsonObject)after["component_status"]!["metadata"]!)["reason"] = "Parser wording changed";

        Assert.Empty(SemanticDiff.CompareCanonical(before, after));
    }

    [Fact]
    public void CanonicalComparisonStillReportsDecodedMetadataChanges()
    {
        var before = Canonical("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var after = Canonical("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        ((JsonObject)((JsonArray)after["metadata"]!)[0]!) ["value"] = "After";

        var changes = SemanticDiff.CompareCanonical(before, after);

        Assert.Contains(changes.OfType<JsonObject>(), change => change["path"]?.GetValue<string>() == "$.metadata[0].value");
    }

    private static JsonObject Canonical(string sourceHash)
    {
        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["source"] = new JsonObject
            {
                ["path"] = "map.w3m",
                ["size_bytes"] = 10,
                ["modified_utc"] = "2026-08-30T00:00:00Z",
                ["sha256"] = sourceHash
            },
            ["metadata"] = new JsonArray(new JsonObject
            {
                ["field"] = "title",
                ["value"] = "Before",
                ["provenance"] = "observed_archive",
                ["capability"] = "parsed_read_only"
            }),
            ["players"] = new JsonArray(),
            ["forces"] = new JsonArray(),
            ["regions"] = new JsonArray(),
            ["archive_members"] = new JsonArray(new JsonObject
            {
                ["path"] = "war3map.w3e",
                ["size_bytes"] = 10,
                ["compressed_size_bytes"] = 5,
                ["sha256"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                ["named"] = true,
                ["flags"] = 0,
                ["capability"] = "preserved_opaque",
                ["provenance"] = "observed_archive"
            }),
            ["opaque_members"] = new JsonArray(new JsonObject
            {
                ["path"] = "war3map.w3e",
                ["size_bytes"] = 10,
                ["sha256"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                ["capability"] = "preserved_opaque",
                ["provenance"] = "observed_archive"
            }),
            ["component_status"] = new JsonObject
            {
                ["metadata"] = new JsonObject
                {
                    ["capability"] = "parsed_read_only",
                    ["provenance"] = "observed_archive",
                    ["reason"] = "Read-only parser"
                }
            }
        };
    }
}
