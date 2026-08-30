using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

public static class SemanticDiff
{
    public static JsonArray Compare(JsonNode? before, JsonNode? after, string operationId = "")
    {
        var changes = new JsonArray();
        Walk(before, after, "$", changes, operationId);
        return changes;
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

        var component = path.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
        changes.Add(new JsonObject
        {
            ["component"] = component,
            ["path"] = path,
            ["before"] = before?.DeepClone(),
            ["after"] = after?.DeepClone(),
            ["change_type"] = before is null ? "added" : after is null ? "removed" : "updated",
            ["operation_id"] = operationId,
            ["provenance"] = "derived"
        });
    }
}
