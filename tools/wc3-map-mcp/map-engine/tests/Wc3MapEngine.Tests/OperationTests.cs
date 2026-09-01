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

    [Fact]
    public void FailedBatchDoesNotMutateTheInputCanonicalValue()
    {
        var canonical = Canonical();
        var operations = new JsonArray(
            MetadataOperation("c0a80101-0000-4000-8000-000000000003", "Before", "After"),
            new JsonObject
            {
                ["operation_id"] = "c0a80101-0000-4000-8000-000000000004",
                ["type"] = "update_region",
                ["target"] = new JsonObject { ["name"] = "Missing" },
                ["expected"] = new JsonObject { ["name"] = "Missing" },
                ["value"] = new JsonObject { ["min_x"] = 1 },
                ["rationale"] = "Force the batch to fail."
            });

        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(canonical, operations));

        Assert.Equal("INVALID_ARGUMENT", exception.Code);
        Assert.Equal("Before", canonical["metadata"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void PlayerForceAndRegionOperationsApplyWithExplicitPreconditions()
    {
        var canonical = Canonical();
        var playerBefore = new JsonObject
        {
            ["capability"] = "parsed_read_only",
            ["provenance"] = "observed_archive",
            ["id"] = 1,
            ["name"] = "Player 1",
            ["controller"] = "User",
            ["race"] = "NightElf",
            ["flags"] = 1,
            ["start"] = new JsonObject { ["x"] = 0, ["y"] = 0 }
        };
        canonical["players"] = new JsonArray(playerBefore);
        var forceBefore = new JsonObject
        {
            ["index"] = 0,
            ["name"] = "Force 1",
            ["flags"] = 9,
            ["player_mask"] = 3,
            ["player_ids"] = new JsonArray(1)
        };
        canonical["forces"] = new JsonArray(forceBefore);

        var operations = new JsonArray(
            new JsonObject
            {
                ["operation_id"] = "c0a80101-0000-4000-8000-000000000005",
                ["type"] = "set_player_slot",
                ["target"] = new JsonObject { ["id"] = 1 },
                ["expected"] = new JsonObject
                {
                    ["id"] = 1,
                    ["name"] = "Player 1",
                    ["controller"] = "User",
                    ["race"] = "NightElf",
                    ["flags"] = 1,
                    ["start"] = new JsonObject { ["y"] = 0, ["x"] = 0 },
                    ["provenance"] = "observed_archive",
                    ["capability"] = "parsed_read_only"
                },
                ["value"] = new JsonObject { ["controller"] = "Computer", ["start"] = new JsonObject { ["x"] = 10, ["y"] = 20 } },
                ["rationale"] = "Test player slot."
            },
            new JsonObject
            {
                ["operation_id"] = "c0a80101-0000-4000-8000-000000000006",
                ["type"] = "set_force",
                ["target"] = new JsonObject { ["index"] = 0 },
                ["expected"] = forceBefore.DeepClone(),
                ["value"] = new JsonObject { ["player_ids"] = new JsonArray(1), ["flags"] = 9 },
                ["rationale"] = "Test force membership."
            },
            new JsonObject
            {
                ["operation_id"] = "c0a80101-0000-4000-8000-000000000007",
                ["type"] = "create_region",
                ["target"] = new JsonObject(),
                ["value"] = new JsonObject { ["name"] = "Arena_B", ["min_x"] = 0, ["min_y"] = 0, ["max_x"] = 128, ["max_y"] = 128 },
                ["rationale"] = "Test region creation."
            });

        var result = OperationApplier.Apply(canonical, operations);

        Assert.Equal("Computer", result["canonical_map"]!["players"]![0]!["controller"]!.GetValue<string>());
        Assert.Contains(result["canonical_map"]!["regions"]!.AsArray().OfType<JsonObject>(), node => node["name"]!.GetValue<string>() == "Arena_B");
        Assert.Equal(3, result["applied_operation_ids"]!.AsArray().Count);
        Assert.All(result["diff"]!["changes"]!.AsArray().OfType<JsonObject>(), change => Assert.Equal("intended_design", change["provenance"]!.GetValue<string>()));
    }

    [Fact]
    public void CrossFeatureBatchIsAppliedInDeclaredDependencyOrder()
    {
        var canonical = Canonical();
        var ids = Enumerable.Range(1, 4).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var operations = new JsonArray(
            new JsonObject
            {
                ["operation_id"] = ids[3], ["type"] = "place_object", ["target"] = new JsonObject(),
                ["value"] = new JsonObject { ["kind"] = "unit", ["rawcode"] = "Z001", ["owner_id"] = 2, ["position"] = new JsonObject { ["x"] = 32, ["y"] = 32, ["z"] = 0 } },
                ["rationale"] = "Exercise placement dependency ordering."
            },
            new JsonObject
            {
                ["operation_id"] = ids[2], ["type"] = "create_object_definition", ["target"] = new JsonObject { ["category"] = "unit", ["rawcode"] = "Z001" },
                ["value"] = new JsonObject { ["category"] = "unit", ["object_kind"] = "custom", ["base_rawcode"] = "hfoo", ["custom_rawcode"] = "Z001", ["rawcode"] = "Z001", ["unknown_ids"] = new JsonArray(), ["modifications"] = new JsonArray() },
                ["rationale"] = "Exercise object-definition dependency ordering."
            },
            new JsonObject
            {
                ["operation_id"] = ids[1], ["type"] = "create_force", ["target"] = new JsonObject { ["index"] = 0 },
                ["value"] = new JsonObject { ["name"] = "Team 1", ["flags"] = 9, ["player_ids"] = new JsonArray(2), ["player_mask"] = 2 },
                ["rationale"] = "Exercise force dependency ordering."
            },
            new JsonObject
            {
                ["operation_id"] = ids[0], ["type"] = "create_player_slot", ["target"] = new JsonObject { ["id"] = 2 },
                ["value"] = new JsonObject { ["id"] = 2, ["name"] = "Player 2", ["controller"] = "User", ["race"] = "Selectable", ["flags"] = 0, ["start"] = new JsonObject { ["x"] = 0, ["y"] = 0 } },
                ["rationale"] = "Exercise player dependency ordering."
            });

        var result = OperationApplier.Apply(canonical, operations);
        Assert.Equal(new[] { ids[0], ids[1], ids[2], ids[3] }, result["applied_operation_ids"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(new[] { "create_player_slot", "create_force", "create_object_definition", "place_object" }, result["diff"]!["dependency_order"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Contains(result["diff"]!["groups"]!.AsArray().OfType<JsonObject>(), group => group["component"]!.GetValue<string>() == "players");
        Assert.Contains(result["canonical_map"]!["placed_objects"]!.AsArray().OfType<JsonObject>(), placement => placement["rawcode"]!.GetValue<string>() == "Z001");
    }

    [Fact]
    public void ScriptOperationStagesMcpOwnedJassWithHashPrecondition()
    {
        const string before = "function main takes nothing returns nothing\nendfunction\n";
        const string after = "function main takes nothing returns nothing\n    call BJDebugMsg(\"phase changed\")\nendfunction\nfunction config takes nothing returns nothing\n    call SetPlayers(1)\nendfunction\n";
        var canonical = Canonical();
        canonical["scripts"] = new JsonArray(new JsonObject
        {
            ["archive_path"] = "war3map.j",
            ["language"] = "Jass",
            ["size_bytes"] = System.Text.Encoding.UTF8.GetByteCount(before),
            ["sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["source_sha256"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["capability"] = "preserved_opaque",
            ["provenance"] = "observed_archive"
        });
        var operation = new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000008",
            ["type"] = "set_script_source",
            ["target"] = new JsonObject { ["archive_path"] = "war3map.j" },
            ["expected"] = Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(before)),
            ["value"] = new JsonObject { ["language"] = "jass", ["source"] = after },
            ["rationale"] = "Change the gameplay debug message."
        });

        var result = OperationApplier.Apply(canonical, operation);

        var staged = result["canonical_map"]!["scripts"]![0]!;
        Assert.Equal(after, staged["source"]!.GetValue<string>());
        Assert.Equal(Hashing.Sha256(System.Text.Encoding.UTF8.GetBytes(after)), staged["source_sha256"]!.GetValue<string>());
        Assert.Contains(result["diff"]!["changes"]!.AsArray().OfType<JsonObject>(), change => change["component"]!.GetValue<string>() == "scripts");
    }

    [Fact]
    public void ScriptOperationRequiresTheCurrentHash()
    {
        var canonical = Canonical();
        canonical["scripts"] = new JsonArray(new JsonObject
        {
            ["archive_path"] = "war3map.j",
            ["language"] = "Jass",
            ["size_bytes"] = 1,
            ["sha256"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["source_sha256"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            ["capability"] = "preserved_opaque",
            ["provenance"] = "observed_archive"
        });

        var exception = Assert.Throws<EngineException>(() => OperationApplier.Apply(canonical, new JsonArray(new JsonObject
        {
            ["operation_id"] = "c0a80101-0000-4000-8000-000000000009",
            ["type"] = "set_script_source",
            ["target"] = new JsonObject { ["archive_path"] = "war3map.j" },
            ["value"] = new JsonObject { ["language"] = "jass", ["source"] = "function main takes nothing returns nothing\nendfunction\n" },
            ["rationale"] = "Missing precondition must be rejected."
        })));

        Assert.Equal("PRECONDITION_REQUIRED", exception.Code);
    }

    private static JsonObject Canonical() => new()
    {
        ["schema_version"] = "1.0",
        ["metadata"] = new JsonArray(new JsonObject { ["field"] = "title", ["value"] = "Before", ["provenance"] = "observed_archive", ["capability"] = "parsed_read_only" }),
        ["regions"] = new JsonArray(new JsonObject { ["name"] = "Arena_A", ["min_x"] = 0, ["min_y"] = 0, ["max_x"] = 64, ["max_y"] = 64 }),
        ["players"] = new JsonArray(),
        ["forces"] = new JsonArray(),
        ["scripts"] = new JsonArray()
    };

    private static JsonObject MetadataOperation(string id, string expected, string value) => new()
    {
        ["operation_id"] = id,
        ["type"] = "set_map_metadata",
        ["target"] = new JsonObject { ["field"] = "title" },
        ["expected"] = expected,
        ["value"] = value,
        ["rationale"] = "Test metadata."
    };
}
