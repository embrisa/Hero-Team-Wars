using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

public static class SemanticDiff
{
    private static readonly HashSet<string> NonSemanticSections = new(StringComparer.OrdinalIgnoreCase)
    {
        // File/container identity and parser observations are reported by the
        // archive/container diff. They are not gameplay semantics.
        "source",
        "archive_members",
        "capabilities",
        "component_status",
        "parse_warnings",
        "opaque_members",
        "cameras",

        // These sections currently contain only opaque archive-member
        // metadata. Until a parser exposes their actual values, a changed
        // member hash is an archive observation rather than a semantic claim.
        "triggers",
        "variables",
        "object_data_members",
        "terrain_summary",
        "imports"
    };

    private static readonly HashSet<string> NonSemanticObjectMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "provenance",
        "capability",
        "stored_value",
        "stored_name",
        "archive_path",
        "sha256",
        "size_bytes",
        "compressed_size_bytes",
        "named",
        "flags",
        "parser",
        "parser_version",
        "status",
        "warnings",
        "error",
        "reason",
        "detail",
        // The full staged script source is carried in a transaction revision
        // for the builder, while source_sha256 remains in the semantic diff.
        "source"
    };

    public static JsonArray Compare(JsonNode? before, JsonNode? after, string operationId = "")
    {
        var changes = new JsonArray();
        Walk(before, after, "$", changes, operationId);
        return changes;
    }

    /// <summary>
    /// Compares canonical map values that are semantically decoded or are
    /// otherwise safe to treat as gameplay data. Archive/container identity,
    /// compression/order metadata, parser capabilities, and opaque members are
    /// deliberately excluded from this result and belong in an archive diff.
    /// </summary>
    public static JsonArray CompareCanonical(JsonNode? before, JsonNode? after, string operationId = "")
    {
        return Compare(ProjectCanonical(before), ProjectCanonical(after), operationId);
    }

    private static JsonNode? ProjectCanonical(JsonNode? node, bool root = true)
    {
        if (node is JsonObject objectNode)
        {
            var projected = new JsonObject();
            foreach (var property in objectNode.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (root && NonSemanticSections.Contains(property.Key))
                {
                    continue;
                }

                if (NonSemanticObjectMembers.Contains(property.Key))
                {
                    continue;
                }

                projected[property.Key] = ProjectCanonical(property.Value, false);
            }

            return projected;
        }

        if (node is JsonArray array)
        {
            var projected = new JsonArray();
            foreach (var item in array)
            {
                projected.Add(ProjectCanonical(item, false));
            }

            return projected;
        }

        return node?.DeepClone();
    }

    private static void Walk(JsonNode? before, JsonNode? after, string path, JsonArray changes, string operationId)
    {
        if (JsonUtilities.Equal(before, after))
        {
            return;
        }

        if (before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            foreach (var key in beforeObject.Select(x => x.Key).Union(afterObject.Select(x => x.Key)).OrderBy(x => x, StringComparer.Ordinal))
            {
                beforeObject.TryGetPropertyValue(key, out var left);
                afterObject.TryGetPropertyValue(key, out var right);
                Walk(left, right, $"{path}.{key}", changes, operationId);
            }

            return;
        }

        if (before is JsonArray beforeArray && after is JsonArray afterArray)
        {
            var length = Math.Max(beforeArray.Count, afterArray.Count);
            for (var index = 0; index < length; index++)
            {
                Walk(index < beforeArray.Count ? beforeArray[index] : null, index < afterArray.Count ? afterArray[index] : null, $"{path}[{index}]", changes, operationId);
            }

            return;
        }

        var componentPath = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$', '.');
        var separator = componentPath.IndexOfAny(['.', '[']);
        var component = (separator >= 0 ? componentPath[..separator] : componentPath) is { Length: > 0 } firstComponent
            ? firstComponent
            : "unknown";
        changes.Add(new JsonObject
        {
            ["component"] = component,
            ["path"] = path,
            ["before"] = before?.DeepClone(),
            ["after"] = after?.DeepClone(),
            ["change_type"] = before is null ? "added" : after is null ? "removed" : "changed",
            ["operation_id"] = operationId,
            ["provenance"] = "derived",
            ["target"] = null
        });
    }
}
