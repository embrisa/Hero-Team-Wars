using System.Text.Json.Nodes;
using Wc3MapEngine.Core;
using Xunit;

namespace Wc3MapEngine.Tests;

public sealed class OperationTests
{
    [Fact]
    public void MetadataOperationProducesAttributableDiff()
    {
        var canonical = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["metadata"] = new JsonArray(new JsonObject { ["field"] = "title", ["value"] = "Before", ["provenance"] = "observed_archive", ["capability"] = "parsed_read_only" }),
            ["regions"] = new JsonArray(),
            ["players"] = new JsonArray(),
            ["forces"] = new JsonArray()
        };
        var operations = new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000001",
            ["type"] = "set_map_metadata",
            ["target"] = new JsonObject { ["field"] = "title" },
            ["expected"] = "Before",
            ["value"] = "After",
            ["rationale"] = "Test"
        });

        var result = OperationApplier.Apply(canonical, operations);
        Assert.Equal("After", result["canonical_map"]!["metadata"]![0]!["value"]!.GetValue<string>());
        Assert.Equal("c0a80101-0000-4000-8000-000000000001", result["diff"]!["changes"]![0]!["operation_id"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidExpectedValueDoesNotApply()
    {
        var canonical = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["metadata"] = new JsonArray(new JsonObject { ["field"] = "title", ["value"] = "Before" }),
            ["regions"] = new JsonArray(),
            ["players"] = new JsonArray(),
            ["forces"] = new JsonArray()
        };
        var operations = new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000002",
            ["type"] = "set_map_metadata",
            ["target"] = new JsonObject { ["field"] = "title" },
            ["expected"] = "Wrong",
            ["value"] = "After",
            ["rationale"] = "Test"
        });

        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(canonical, operations));
        Assert.Equal("PRECONDITION_FAILED", exception.Code);
    }
}
