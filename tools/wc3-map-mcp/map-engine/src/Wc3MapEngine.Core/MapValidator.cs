using System.Text.Json.Nodes;
using Wc3MapEngine.Core.Validation;

namespace Wc3MapEngine.Core;

/// <summary>
/// Compatibility facade for the Phase 3 validators. The implementation lives
/// under Core/Validation so map and transaction-build checks share one path.
/// </summary>
public static class MapValidator
{
    public static JsonObject ValidateMap(string path, JsonObject? context = null)
        => ValidationPipeline.ValidateMap(path, context);

    public static JsonObject ValidateCanonical(string path, string? sourcePath = null, JsonObject? context = null)
        => ValidationPipeline.ValidateCanonical(path, sourcePath, context);
}
