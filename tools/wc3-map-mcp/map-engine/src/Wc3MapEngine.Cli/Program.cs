using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wc3MapEngine.Contracts;
using Wc3MapEngine.Core;

namespace Wc3MapEngine.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                return SelfTest();
            }

            if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
            {
                return RunStdio();
            }

            Console.Error.WriteLine("Usage: Wc3MapEngine.Cli --stdio | --self-test");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Wc3MapEngine fatal error: {exception.Message}");
            return 1;
        }
    }

    private static int RunStdio()
    {
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            EngineResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<EngineRequest>(line, EngineProtocol.JsonOptions)
                    ?? throw new EngineException("INVALID_JSON", "The worker request was empty.");
                response = Handle(request);
            }
            catch (EngineException exception)
            {
                response = Failure(string.Empty, exception);
            }
            catch (JsonException exception)
            {
                response = Failure(string.Empty, new EngineException("INVALID_JSON", exception.Message, false, exception));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Worker request failed: {exception}");
                response = Failure(string.Empty, new EngineException("INTERNAL_ERROR", "The map engine encountered an unexpected error.", false, exception));
            }

            Console.WriteLine(JsonSerializer.Serialize(response, EngineProtocol.JsonOptions));
            Console.Out.Flush();
        }

        return 0;
    }

    private static EngineResponse Handle(EngineRequest request)
    {
        if (!request.ProtocolVersion.StartsWith("1.", StringComparison.Ordinal))
        {
            return Failure(request.RequestId, new EngineException("PROTOCOL_VERSION_UNSUPPORTED", $"Engine protocol '{request.ProtocolVersion}' is not supported."));
        }

        try
        {
            var result = request.Operation switch
            {
                "environment_status" => EnvironmentStatus(request.Payload),
                "hash_file" => HashFile(request.Payload),
                "list_archive_members" => ListArchiveMembers(request.Payload),
                "probe_map" => ProbeMap(request.Payload),
                "inspect_map" => InspectMap(request.Payload),
                "validate_map" => Validate(request.Payload),
                "validate_canonical" => ValidateCanonical(request.Payload),
                "apply_operations" => ApplyOperations(request.Payload),
                "build_map" => BuildMap(request.Payload),
                "compare_maps" => CompareMaps(request.Payload),
                _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown engine operation '{request.Operation}'.")
            };
            return new EngineResponse { RequestId = request.RequestId, Ok = true, Result = result };
        }
        catch (EngineException exception)
        {
            return Failure(request.RequestId, exception);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Operation {request.Operation} failed: {exception}");
            return Failure(request.RequestId, new EngineException("INTERNAL_ERROR", "The map engine encountered an unexpected error.", false, exception));
        }
    }

    private static JsonObject EnvironmentStatus(JsonObject payload)
    {
        var result = new JsonObject
        {
            ["engine_version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0",
            ["engine_commit"] = "local",
            ["runtime"] = Environment.Version.ToString(),
            ["framework_description"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["os"] = Environment.OSVersion.VersionString,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ["os_architecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            ["war3net_io_mpq"] = typeof(War3Net.IO.Mpq.MpqArchive).Assembly.GetName().Version?.ToString(),
            ["war3net_build_core"] = typeof(War3Net.Build.Map).Assembly.GetName().Version?.ToString()
        };

        if (payload["configured_files"] is JsonObject configuredFiles)
        {
            var observations = new JsonObject();
            foreach (var property in configuredFiles)
            {
                if (property.Value is not JsonValue value || !value.TryGetValue<string>(out var configuredPath) || string.IsNullOrWhiteSpace(configuredPath))
                {
                    throw new EngineException("INVALID_ARGUMENT", $"configured_files.{property.Key} must be a non-empty path string.");
                }

                var fullPath = Path.GetFullPath(configuredPath);
                var isFile = File.Exists(fullPath);
                var isDirectory = Directory.Exists(fullPath);
                var observation = new JsonObject
                {
                    ["path"] = fullPath,
                    ["exists"] = isFile || isDirectory,
                    ["kind"] = isFile ? "file" : isDirectory ? "directory" : "missing"
                };

                if (isFile)
                {
                    var version = FileVersionInfo.GetVersionInfo(fullPath);
                    observation["file_version"] = version.FileVersion;
                    observation["product_version"] = version.ProductVersion;
                    observation["product_name"] = version.ProductName;
                }

                observations[property.Key] = observation;
            }

            result["configured_files"] = observations;
        }

        return result;
    }

    private static JsonObject HashFile(JsonObject payload)
    {
        var path = RequiredPath(payload, "path");
        var hash = Hashing.HashFileAsync(path).GetAwaiter().GetResult();
        return new JsonObject
        {
            ["path"] = path,
            ["size_bytes"] = hash.Size,
            ["modified_utc"] = hash.LastWriteUtc.ToUniversalTime().ToString("O"),
            ["sha256"] = hash.Sha256
        };
    }

    private static JsonObject ListArchiveMembers(JsonObject payload)
    {
        var archive = MapArchive.Read(RequiredPath(payload, "map_path"));
        var capabilities = MapInspector.Probe(archive).OfType<JsonObject>()
            .ToDictionary(x => x["path"]!.GetValue<string>(), x => x, StringComparer.OrdinalIgnoreCase);
        return new JsonObject
        {
            ["map_path"] = archive.SourcePath,
            ["map_sha256"] = Hashing.Sha256(File.ReadAllBytes(archive.SourcePath)),
            ["members"] = new JsonArray(archive.Members.Select(member => (JsonNode)new JsonObject
            {
                ["path"] = member.Path,
                ["size_bytes"] = member.Size,
                ["compressed_size_bytes"] = member.CompressedSize,
                ["sha256"] = member.Sha256,
                ["named"] = member.Named,
                ["flags"] = (uint)member.Flags,
                ["capability"] = capabilities[member.Path]["status"]!.DeepClone(),
                ["parser"] = capabilities[member.Path]["parser"]?.DeepClone(),
                ["parser_version"] = capabilities[member.Path]["parser_version"]?.DeepClone(),
                ["warnings"] = capabilities[member.Path]["warnings"]?.DeepClone() ?? new JsonArray(),
                ["error"] = capabilities[member.Path]["error"]?.DeepClone()
            }).ToArray())
        };
    }

    private static JsonObject ProbeMap(JsonObject payload)
    {
        var archive = MapArchive.Read(RequiredPath(payload, "map_path"));
        return new JsonObject { ["map_path"] = archive.SourcePath, ["capabilities"] = MapInspector.Probe(archive) };
    }

    private static JsonObject InspectMap(JsonObject payload) => MapInspector.Inspect(RequiredPath(payload, "map_path"));

    private static JsonObject Validate(JsonObject payload) => MapValidator.ValidateMap(RequiredPath(payload, "map_path"));

    private static JsonObject ValidateCanonical(JsonObject payload) => MapValidator.ValidateCanonical(RequiredPath(payload, "canonical_path"));

    private static JsonObject ApplyOperations(JsonObject payload)
    {
        var canonicalPath = RequiredPath(payload, "canonical_path");
        var operations = payload["operations"] as JsonArray ?? throw new EngineException("INVALID_ARGUMENT", "apply_operations requires an operations array.");
        var result = OperationApplier.Apply(JsonUtilities.Read(canonicalPath), operations);
        if (payload["output_path"]?.GetValue<string>() is { Length: > 0 } outputPath)
        {
            JsonUtilities.WriteAtomic(outputPath, result["canonical_map"]!);
        }

        return result;
    }

    private static JsonObject BuildMap(JsonObject payload) => MapBuilder.Build(
        RequiredPath(payload, "source_map_path"),
        RequiredPath(payload, "canonical_path"),
        RequiredPath(payload, "output_path"));

    private static JsonObject CompareMaps(JsonObject payload)
    {
        var leftPath = RequiredPath(payload, "left_path");
        var rightPath = RequiredPath(payload, "right_path");
        var left = IsCanonical(leftPath) ? JsonUtilities.Read(leftPath) : MapInspector.Inspect(leftPath);
        var right = IsCanonical(rightPath) ? JsonUtilities.Read(rightPath) : MapInspector.Inspect(rightPath);
        var leftMembers = left?["archive_members"] as JsonArray ?? new JsonArray();
        var rightMembers = right?["archive_members"] as JsonArray ?? new JsonArray();
        var memberChanges = new JsonArray();
        foreach (var path in leftMembers.OfType<JsonObject>().Select(x => x["path"]?.GetValue<string>()).Union(rightMembers.OfType<JsonObject>().Select(x => x["path"]?.GetValue<string>())).Where(x => x is not null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var leftMember = leftMembers.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["path"]?.GetValue<string>(), path, StringComparison.OrdinalIgnoreCase));
            var rightMember = rightMembers.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["path"]?.GetValue<string>(), path, StringComparison.OrdinalIgnoreCase));
            var leftHash = leftMember?["sha256"]?.GetValue<string>();
            var rightHash = rightMember?["sha256"]?.GetValue<string>();
            var memberMetadataChanged = leftMember is null || rightMember is null
                || !string.Equals(leftHash, rightHash, StringComparison.OrdinalIgnoreCase)
                || !JsonUtilities.Equal(leftMember?["size_bytes"], rightMember?["size_bytes"])
                || !JsonUtilities.Equal(leftMember?["compressed_size_bytes"], rightMember?["compressed_size_bytes"])
                || !JsonUtilities.Equal(leftMember?["named"], rightMember?["named"])
                || !JsonUtilities.Equal(leftMember?["flags"], rightMember?["flags"]);
            if (memberMetadataChanged)
            {
                memberChanges.Add(new JsonObject
                {
                    ["path"] = path,
                    ["left_sha256"] = leftHash,
                    ["right_sha256"] = rightHash,
                    ["left_size_bytes"] = leftMember?["size_bytes"]?.DeepClone(),
                    ["right_size_bytes"] = rightMember?["size_bytes"]?.DeepClone(),
                    ["left_compressed_size_bytes"] = leftMember?["compressed_size_bytes"]?.DeepClone(),
                    ["right_compressed_size_bytes"] = rightMember?["compressed_size_bytes"]?.DeepClone(),
                    ["change_type"] = leftMember is null ? "added" : rightMember is null ? "removed" : "updated"
                });
            }
        }

        var leftOrder = leftMembers.OfType<JsonObject>().Select(x => x["path"]?.GetValue<string>()).Where(x => x is not null).Select(x => x!).ToArray();
        var rightOrder = rightMembers.OfType<JsonObject>().Select(x => x["path"]?.GetValue<string>()).Where(x => x is not null).Select(x => x!).ToArray();
        var containerDifferences = new JsonArray();
        if (!leftOrder.SequenceEqual(rightOrder, StringComparer.OrdinalIgnoreCase))
        {
            containerDifferences.Add(new JsonObject
            {
                ["kind"] = "member_order",
                ["left"] = new JsonArray(leftOrder.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["right"] = new JsonArray(rightOrder.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
            });
        }

        var leftSource = left?["source"] as JsonObject;
        var rightSource = right?["source"] as JsonObject;
        if (leftSource is not null || rightSource is not null)
        {
            var sourceIdentityChanged = !JsonUtilities.Equal(leftSource?["sha256"], rightSource?["sha256"])
                || !JsonUtilities.Equal(leftSource?["size_bytes"], rightSource?["size_bytes"])
                || !JsonUtilities.Equal(leftSource?["modified_utc"], rightSource?["modified_utc"])
                || !JsonUtilities.Equal(leftSource?["path"], rightSource?["path"]);
            if (sourceIdentityChanged)
            {
                containerDifferences.Add(new JsonObject
                {
                    ["kind"] = "source_identity",
                    ["left_sha256"] = leftSource?["sha256"]?.DeepClone(),
                    ["right_sha256"] = rightSource?["sha256"]?.DeepClone(),
                    ["left_size_bytes"] = leftSource?["size_bytes"]?.DeepClone(),
                    ["right_size_bytes"] = rightSource?["size_bytes"]?.DeepClone(),
                    ["left_path"] = leftSource?["path"]?.DeepClone(),
                    ["right_path"] = rightSource?["path"]?.DeepClone()
                });
            }
        }

        return new JsonObject
        {
            ["schema_version"] = "1.0",
            ["left_path"] = leftPath,
            ["right_path"] = rightPath,
            ["archive_differences"] = memberChanges,
            ["container_differences"] = containerDifferences,
            ["semantic_differences"] = SemanticDiff.CompareCanonical(left, right, "compare")
        };
    }

    private static bool IsCanonical(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string RequiredPath(JsonObject payload, string name) => payload[name]?.GetValue<string>() is { Length: > 0 } path
        ? path
        : throw new EngineException("INVALID_ARGUMENT", $"Missing required path property '{name}'.");

    private static EngineResponse Failure(string requestId, EngineException exception) => new()
    {
        RequestId = requestId,
        Ok = false,
        Error = new EngineError
        {
            Code = exception.Code,
            Message = exception.Message,
            Retryable = exception.Retryable,
            Details = new JsonObject()
        }
    };

    private static int SelfTest()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        var expected = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        if (!string.Equals(Hashing.Sha256(bytes), expected, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("SHA-256 self-test failed.");
            return 1;
        }

        var response = new EngineResponse { RequestId = "self-test", Ok = true, Result = new JsonObject { ["sha256_known_vector"] = true } };
        Console.WriteLine(JsonSerializer.Serialize(response, EngineProtocol.JsonOptions));
        return 0;
    }
}
