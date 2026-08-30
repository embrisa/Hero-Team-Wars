using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Wc3MapEngine.Contracts;

public static class EngineProtocol
{
    public const string Version = "1.0";
    public const string SchemaVersion = "1.0";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class EngineRequest
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = EngineProtocol.Version;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonObject Payload { get; set; } = new();
}

public sealed class EngineResponse
{
    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = EngineProtocol.Version;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public JsonNode? Result { get; set; }

    [JsonPropertyName("error")]
    public EngineError? Error { get; set; }
}

public sealed class EngineError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "INTERNAL_ERROR";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }

    [JsonPropertyName("details")]
    public JsonObject Details { get; set; } = new();
}
