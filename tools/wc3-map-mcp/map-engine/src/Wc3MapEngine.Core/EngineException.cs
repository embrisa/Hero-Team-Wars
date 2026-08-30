namespace Wc3MapEngine.Core;

public sealed class EngineException(string code, string message, bool retryable = false, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
