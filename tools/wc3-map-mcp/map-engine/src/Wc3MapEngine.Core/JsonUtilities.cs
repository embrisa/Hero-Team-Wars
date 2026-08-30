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

    public static bool Equal(JsonNode? left, JsonNode? right) =>
        string.Equals(left?.ToJsonString(EngineProtocol.JsonOptions), right?.ToJsonString(EngineProtocol.JsonOptions), StringComparison.Ordinal);

    public static JsonNode? Clone(JsonNode? value) => value?.DeepClone();
}
