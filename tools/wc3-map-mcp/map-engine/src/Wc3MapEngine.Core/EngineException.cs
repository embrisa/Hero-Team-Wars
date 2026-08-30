using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core;

public sealed class EngineException : Exception
{
    public EngineException(string code, string message, bool retryable = false, Exception? inner = null, JsonObject? details = null)
        : base(message, inner)
    {
        Code = code;
        Retryable = retryable;
        Details = details ?? new JsonObject();
    }

    public string Code { get; }
    public bool Retryable { get; }
    public JsonObject Details { get; }
}
