using System.Text.Json;
using System.Text.Json.Nodes;
using Wc3MapEngine.Contracts;

namespace Wc3MapEngine.Core;

public static class JsonUtilities
{
    public static JsonNode Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new EngineException("FILE_NOT_FOUND", $"JSON file does not exist: {path}");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) ?? throw new EngineException("INVALID_JSON", $"JSON file is empty: {path}");
        }
        catch (JsonException exception)
        {
            throw new EngineException("INVALID_JSON", $"Invalid JSON in {path}: {exception.Message}", false, exception);
        }
    }

    public static void WriteAtomic(string path, JsonNode node)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new EngineException("INVALID_ARGUMENT", "JSON output must have a parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, node.ToJsonString(EngineProtocol.JsonOptions) + Environment.NewLine);
            _ = JsonNode.Parse(File.ReadAllText(tempPath)) ?? throw new EngineException("INVALID_JSON", "The temporary JSON output could not be parsed.");
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static bool Equal(JsonNode? left, JsonNode? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            if (leftObject.Count != rightObject.Count) return false;
            foreach (var property in leftObject)
            {
                if (!rightObject.TryGetPropertyValue(property.Key, out var rightNode) || !Equal(property.Value, rightNode)) return false;
            }

            return true;
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            return leftArray.Count == rightArray.Count && leftArray.Zip(rightArray).All(pair => Equal(pair.First, pair.Second));
        }

        if (left is JsonValue leftValue && right is JsonValue rightValue)
        {
            if (leftValue.TryGetValue<double>(out var leftNumber) && rightValue.TryGetValue<double>(out var rightNumber))
            {
                return leftNumber.Equals(rightNumber);
            }

            if (leftValue.TryGetValue<bool>(out var leftBoolean) && rightValue.TryGetValue<bool>(out var rightBoolean))
            {
                return leftBoolean == rightBoolean;
            }

            if (leftValue.TryGetValue<string>(out var leftString) && rightValue.TryGetValue<string>(out var rightString))
            {
                return string.Equals(leftString, rightString, StringComparison.Ordinal);
            }
        }

        return string.Equals(left.ToJsonString(EngineProtocol.JsonOptions), right.ToJsonString(EngineProtocol.JsonOptions), StringComparison.Ordinal);
    }

    public static JsonNode? Clone(JsonNode? value) => value?.DeepClone();
}
