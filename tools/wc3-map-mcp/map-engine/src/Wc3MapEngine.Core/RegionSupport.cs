using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

/// <summary>
/// Shared invariants for the typed war3map.w3r adapter and the MCP region
/// operation vocabulary.  The Warcraft III creation number is the native
/// identity, so the MCP id is deliberately deterministic across inspection,
/// serialization, and rebuild.
/// </summary>
public static class RegionSupport
{
    public const string CodecVersion = "war3net-6.0.3-mapregions-v5";
    public const double EditorEnvelopeMargin = 256d;

    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Arena_A", "Arena_B",
        "Camp_A_Player1", "Camp_A_Player2", "Camp_B_Player3", "Camp_B_Player4"
    };

    public static readonly IReadOnlySet<string> Roles = new HashSet<string>(StringComparer.Ordinal)
    {
        "arena", "entrance", "backline", "camp", "spawn", "cleanup", "camera_boundary"
    };

    public static string StableId(int creationNumber) => $"region:{creationNumber}";

    public static string StableId(JsonObject region)
        => StringValue(region, "id") ?? StableId(IntValue(region["creation_number"]));

    public static JsonObject EmptyReferences() => new()
    {
        ["mcp_owned"] = new JsonArray(),
        ["editor_trigger"] = new JsonArray(),
        ["custom_text"] = new JsonArray(),
        ["derived_roles"] = new JsonArray()
    };

    public static bool IsProtectedName(string? name)
        => name is not null && ProtectedNames.Contains(name);

    public static string RequiredRole(JsonObject value)
    {
        var role = StringValue(value, "role");
        if (role is null || !Roles.Contains(role))
        {
            throw new EngineException("INVALID_ARGUMENT", "Region role must be one of arena, entrance, backline, camp, spawn, cleanup, or camera_boundary.");
        }
        return role;
    }

    public static void ValidateIdentity(JsonObject region, string context)
    {
        var creationNumber = IntValue(region["creation_number"]);
        if (creationNumber < 0) throw new EngineException("INVALID_ARGUMENT", $"{context} requires a non-negative creation_number.");
        var expectedId = StableId(creationNumber);
        var id = StringValue(region, "id");
        if (id is not null && !string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new EngineException("INVALID_ARGUMENT", $"{context} id must be '{expectedId}' for creation_number {creationNumber}.");
        }
        region["id"] = expectedId;
    }

    public static void ValidateEnvelope(JsonObject root, JsonObject region, string name)
    {
        var metadata = root["metadata"] as JsonArray;
        var camera = metadata?.OfType<JsonObject>().FirstOrDefault(item => string.Equals(StringValue(item, "field"), "camera_bounds", StringComparison.Ordinal));
        if (camera?["value"] is not JsonObject bounds) return;

        var left = Number(bounds["left"]);
        var bottom = Number(bounds["bottom"]);
        var right = Number(bounds["right"]);
        var top = Number(bounds["top"]);
        if (left is null || bottom is null || right is null || top is null) return;

        var minX = Number(region["min_x"]) ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires a finite min_x.");
        var minY = Number(region["min_y"]) ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires a finite min_y.");
        var maxX = Number(region["max_x"]) ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires a finite max_x.");
        var maxY = Number(region["max_y"]) ?? throw new EngineException("INVALID_ARGUMENT", $"Region '{name}' requires a finite max_y.");
        if (minX < left.Value - EditorEnvelopeMargin || minY < bottom.Value - EditorEnvelopeMargin || maxX > right.Value + EditorEnvelopeMargin || maxY > top.Value + EditorEnvelopeMargin)
        {
            throw new EngineException("COORDINATE_OUT_OF_BOUNDS", $"Region '{name}' lies outside the inspected map envelope (including the {EditorEnvelopeMargin:0} editor-unit margin).");
        }
    }

    public static bool SerializedEqual(JsonObject? left, JsonObject? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var leftProjection = SerializedProjection(left);
        var rightProjection = SerializedProjection(right);
        return JsonUtilities.Equal(leftProjection, rightProjection);
    }

    public static JsonObject SerializedProjection(JsonObject region)
    {
        var result = new JsonObject();
        foreach (var field in new[] { "id", "name", "stored_name", "min_x", "min_y", "max_x", "max_y", "creation_number", "weather", "ambient_sound", "color_argb" })
        {
            if (region[field] is not null) result[field] = region[field]!.DeepClone();
        }
        return result;
    }

    private static string? StringValue(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static int IntValue(JsonNode? value)
        => value is JsonValue node && node.TryGetValue<int>(out var integer) ? integer : -1;

    private static double? Number(JsonNode? value)
    {
        if (value is not JsonValue node) return null;
        if (node.TryGetValue<double>(out var real) && double.IsFinite(real)) return real;
        if (node.TryGetValue<float>(out var single) && float.IsFinite(single)) return single;
        if (node.TryGetValue<int>(out var integer)) return integer;
        return null;
    }
}
