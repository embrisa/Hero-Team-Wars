using System.Globalization;
using System.Text.Json.Nodes;
using War3Net.Build;
using War3Net.Build.Common;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.IO.Mpq;
using Wc3MapEngine.Core.Scripts;

namespace Wc3MapEngine.Core;

public static class MapInspector
{
    private static readonly HashSet<string> ObjectMembers = new(StringComparer.OrdinalIgnoreCase)
    {
        "war3map.w3a", "war3map.w3b", "war3map.w3d", "war3map.w3h", "war3map.w3q", "war3map.w3t", "war3map.w3u"
    };

    public static JsonObject Inspect(string path)
    {
        var fileHash = Hashing.HashFileAsync(path).GetAwaiter().GetResult();
        var archive = MapArchive.Read(path);
        var parseResults = Probe(archive);
        var info = TryReadInfo(path, out var infoError);
        if (info is not null)
        {
            ResolveTriggerStrings(info, archive.Find("war3map.wts"));
        }
        var regions = TryReadRegions(path, out var regionError);

        var root = new JsonObject
        {
            ["schema_version"] = "1.0",
            ["source"] = new JsonObject
            {
                ["path"] = path,
                ["size_bytes"] = fileHash.Size,
                ["modified_utc"] = fileHash.LastWriteUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["sha256"] = fileHash.Sha256
            },
            ["metadata"] = BuildMetadata(info),
            ["players"] = BuildPlayers(info),
            ["forces"] = BuildForces(info),
            ["regions"] = BuildRegions(regions),
            ["cameras"] = BuildComponentMembers(archive, new[] { "war3map.w3c" }, "cameras"),
            ["archive_members"] = BuildArchiveMembers(archive, parseResults),
            ["capabilities"] = new JsonArray(parseResults.Select(ToJson).ToArray()),
            ["triggers"] = BuildComponentMembers(archive, new[] { "war3map.wtg", "war3map.wct", "war3map.j", "war3map.lua" }, "triggers"),
            ["scripts"] = BuildScripts(archive, info),
            ["variables"] = UnknownComponent("variables", "Trigger variable details are not exposed until the trigger format is proven for this map."),
            ["object_data"] = BuildObjectData(archive),
            ["placed_objects"] = BuildComponentMembers(archive, new[] { "war3mapUnits.doo", "war3map.doo" }, "placed_objects"),
            ["terrain_summary"] = BuildTerrainSummary(archive),
            ["imports"] = BuildComponentMembers(archive, new[] { "war3map.imp" }, "imports"),
            ["opaque_members"] = new JsonArray(archive.Members
                .Where(x => string.Equals(parseResults.First(r => string.Equals(r!["path"]!.GetValue<string>(), x.Path, StringComparison.OrdinalIgnoreCase))!["status"]!.GetValue<string>(), "preserved_opaque", StringComparison.Ordinal))
                .Select(x => (JsonNode)new JsonObject
                {
                    ["path"] = x.Path,
                    ["sha256"] = x.Sha256,
                    ["size_bytes"] = x.Size,
                    ["capability"] = "preserved_opaque",
                    ["provenance"] = "observed_archive"
                }).ToArray()),
            ["parse_warnings"] = new JsonArray(
                (infoError is null ? Array.Empty<JsonNode>() : new JsonNode[] { new JsonObject { ["component"] = "metadata", ["message"] = infoError } })
                .Concat(regionError is null ? Array.Empty<JsonNode>() : new JsonNode[] { new JsonObject { ["component"] = "regions", ["message"] = regionError } })
                .ToArray())
        };

        // Keep the data sections in their established array/object shapes while
        // making the capability and provenance of every section explicit. This
        // is intentionally separate from per-item provenance because empty and
        // unsupported sections have no item from which a caller could infer it.
        root["component_status"] = BuildComponentStatus(
            info,
            infoError,
            regions,
            regionError,
            archive,
            parseResults,
            root["triggers"] as JsonArray,
            root["scripts"] as JsonArray,
            root["cameras"] as JsonArray,
            root["object_data"] as JsonArray,
            root["placed_objects"] as JsonArray,
            root["imports"] as JsonArray,
            root["opaque_members"] as JsonArray,
            archive.Find("war3map.w3e") is not null,
            root["parse_warnings"] as JsonArray);

        return root;
    }

    public static JsonArray Probe(MapArchiveSnapshot archive)
    {
        var results = new List<JsonObject>();
        foreach (var member in archive.Members.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Path, StringComparer.Ordinal))
        {
            var status = "preserved_opaque";
            var parser = (string?)null;
            string? error = null;
            try
            {
                switch (member.Path.ToLowerInvariant())
                {
                    case "war3map.w3i":
                        _ = ReadInfo(member.Bytes);
                        status = "parsed_read_only";
                        parser = "War3Net.Build.Core: MapInfo";
                        break;
                    case "war3map.w3r":
                        _ = ReadRegions(member.Bytes);
                        status = "parsed_read_only";
                        parser = "War3Net.Build.Core: MapRegions";
                        break;
                    case "war3map.wts":
                        _ = ParseTriggerStrings(member.Bytes);
                        status = "parsed_read_only";
                        parser = "UTF-8 trigger-string inventory";
                        break;
                    default:
                        status = "preserved_opaque";
                        parser = null;
                        break;
                }
            }
            catch (Exception exception)
            {
                status = "unsupported_blocking";
                error = exception.Message;
            }

            results.Add(new JsonObject
            {
                ["path"] = member.Path,
                ["status"] = status,
                ["parser"] = parser,
                ["parser_version"] = "War3Net 6.0.3",
                ["warnings"] = new JsonArray(),
                ["error"] = error
            });
        }

        return new JsonArray(results.Cast<JsonNode>().ToArray());
    }

    private static JsonObject? TryReadInfo(string path, out string? error)
    {
        try
        {
            using var archive = MpqArchive.Open(path, loadListFile: true);
            var map = Map.Open(archive, MapFiles.Info);
            error = null;
            return map.Info is null ? null : ToInfo(map.Info);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static JsonArray? TryReadRegions(string path, out string? error)
    {
        try
        {
            using var archive = MpqArchive.Open(path, loadListFile: true);
            var map = Map.Open(archive, MapFiles.Regions);
            error = null;
            return map.Regions is null ? null : ToRegions(map.Regions);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
    }

    private static MapInfo ReadInfo(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        return reader.ReadMapInfo();
    }

    private static MapRegions ReadRegions(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        return reader.ReadMapRegions();
    }

    private static JsonObject ToInfo(MapInfo info) => new()
    {
        ["format_version"] = (int)info.FormatVersion,
        ["map_version"] = info.MapVersion,
        ["editor_version"] = (int)info.EditorVersion,
        ["game_version"] = info.GameVersion?.ToString(),
        ["title"] = Value(info.MapName, "observed_archive", "parsed_read_only"),
        ["author"] = Value(info.MapAuthor, "observed_archive", "parsed_read_only"),
        ["description"] = Value(info.MapDescription, "observed_archive", "parsed_read_only"),
        ["recommended_players"] = Value(info.RecommendedPlayers, "observed_archive", "parsed_read_only"),
        ["suggested_players"] = Value(TryParseSuggestedPlayers(info.RecommendedPlayers), "derived", "parsed_read_only"),
        ["map_flags"] = (int)info.MapFlags,
        ["tileset"] = info.Tileset.ToString(),
        ["script_language"] = info.ScriptLanguage.ToString(),
        ["camera_bounds"] = new JsonObject
        {
            ["left"] = info.CameraBounds.BottomLeft.X,
            ["bottom"] = info.CameraBounds.BottomLeft.Y,
            ["right"] = info.CameraBounds.TopRight.X,
            ["top"] = info.CameraBounds.TopRight.Y
        },
        ["playable_map_area"] = new JsonObject
        {
            ["width"] = info.PlayableMapAreaWidth,
            ["height"] = info.PlayableMapAreaHeight
        },
        ["players"] = new JsonArray(info.Players.OrderBy(x => x.Id).Select(player => (JsonNode)new JsonObject
        {
            ["id"] = player.Id + 1,
            ["name"] = player.Name,
            ["controller"] = player.Controller.ToString(),
            ["race"] = player.Race.ToString(),
            ["flags"] = (int)player.Flags,
            ["start"] = new JsonObject
            {
                ["x"] = player.StartPosition.X,
                ["y"] = player.StartPosition.Y
            },
            ["ally_low_priority_mask"] = (int)player.AllyLowPriorityFlags,
            ["ally_high_priority_mask"] = (int)player.AllyHighPriorityFlags,
            ["enemy_low_priority_mask"] = (int)player.EnemyLowPriorityFlags,
            ["enemy_high_priority_mask"] = (int)player.EnemyHighPriorityFlags,
            ["provenance"] = "observed_archive",
            ["capability"] = "parsed_read_only"
        }).ToArray()),
        ["forces"] = new JsonArray(info.Forces.Select((force, index) => (JsonNode)new JsonObject
        {
            ["index"] = index,
            ["name"] = force.Name,
            ["flags"] = (int)force.Flags,
            ["player_mask"] = (int)force.Players,
            ["player_ids"] = new JsonArray(Enumerable.Range(0, info.Players.Count).Where(id => force.Players[id]).Select(id => (JsonNode)JsonValue.Create(id + 1)!).ToArray()),
            ["provenance"] = "observed_archive",
            ["capability"] = "parsed_read_only"
        }).ToArray())
    };

    private static JsonArray BuildMetadata(JsonObject? info)
    {
        if (info is null)
        {
            return new JsonArray(UnknownValue("title"), UnknownValue("suggested_players"), UnknownValue("description"));
        }

        var result = new JsonArray();
        foreach (var field in new[] { "title", "suggested_players", "author", "description", "recommended_players", "map_flags", "tileset", "script_language", "camera_bounds", "playable_map_area", "format_version", "map_version", "editor_version", "game_version" })
        {
            if (info[field] is JsonObject value && value.ContainsKey("value"))
            {
                result.Add(new JsonObject { ["field"] = field, ["value"] = value["value"]?.DeepClone(), ["stored_value"] = value["stored_value"]?.DeepClone(), ["provenance"] = value["provenance"]?.DeepClone(), ["capability"] = value["capability"]?.DeepClone() });
            }
            else if (info[field] is not null)
            {
                result.Add(new JsonObject { ["field"] = field, ["value"] = info[field]!.DeepClone(), ["provenance"] = "observed_archive", ["capability"] = "parsed_read_only" });
            }
        }

        return result;
    }

    private static JsonArray BuildPlayers(JsonObject? info)
    {
        if (info is null)
        {
            return new JsonArray();
        }

        return info["players"]?.DeepClone() as JsonArray ?? new JsonArray();
    }

    private static JsonArray BuildForces(JsonObject? info) => info?["forces"]?.DeepClone() as JsonArray ?? new JsonArray();

    private static JsonArray BuildRegions(JsonArray? regions) => regions ?? new JsonArray();

    private static JsonArray ToRegions(MapRegions regions)
    {
        var result = new JsonArray();
        if (regions.Protected)
        {
            return result;
        }

        foreach (var region in regions.Regions.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["name"] = region.Name,
                ["min_x"] = region.Left,
                ["min_y"] = region.Bottom,
                ["max_x"] = region.Right,
                ["max_y"] = region.Top,
                ["creation_number"] = region.CreationNumber,
                ["weather"] = region.WeatherType.ToString(),
                ["ambient_sound"] = region.AmbientSound,
                ["provenance"] = "observed_archive",
                ["capability"] = "parsed_read_only"
            });
        }

        return result;
    }

    private static JsonObject Value(object? value, string provenance, string capability) => new()
    {
        ["value"] = JsonValue.Create(value),
        ["provenance"] = provenance,
        ["capability"] = capability
    };

    private static JsonNode UnknownValue(string field) => new JsonObject
    {
        ["field"] = field,
        ["value"] = null,
        ["provenance"] = "unknown",
        ["capability"] = "unsupported_blocking"
    };

    private static JsonObject UnknownComponent(string component, string reason) => new()
    {
        ["component"] = component,
        ["capability"] = "preserved_opaque",
        ["provenance"] = "unknown",
        ["reason"] = reason
    };

    private static JsonObject BuildComponentStatus(
        JsonObject? info,
        string? infoError,
        JsonArray? regions,
        string? regionError,
        MapArchiveSnapshot archive,
        JsonArray parseResults,
        JsonArray? triggers,
        JsonArray? scripts,
        JsonArray? cameras,
        JsonArray? objectData,
        JsonArray? placedObjects,
        JsonArray? imports,
        JsonArray? opaqueMembers,
        bool hasTerrain,
        JsonArray? parseWarnings)
    {
        var statuses = new JsonObject
        {
            ["source"] = ComponentStatus("observed", "observed_archive", "Map file identity, size, timestamp, and SHA-256 were observed before parsing."),
            ["metadata"] = info is null
                ? ComponentStatus("unsupported_blocking", "unknown", infoError ?? "war3map.w3i was not available for read-only parsing.")
                : ComponentStatus("parsed_read_only", "observed_archive", "war3map.w3i was parsed read-only; metadata values are observed archive data."),
            ["players"] = info is null
                ? ComponentStatus("unsupported_blocking", "unknown", infoError ?? "Player data is unavailable because war3map.w3i was not parsed.")
                : ComponentStatus("parsed_read_only", "observed_archive", "Player slots were parsed read-only from war3map.w3i."),
            ["forces"] = info is null
                ? ComponentStatus("unsupported_blocking", "unknown", infoError ?? "Force data is unavailable because war3map.w3i was not parsed.")
                : ComponentStatus("parsed_read_only", "observed_archive", "Forces were parsed read-only from war3map.w3i."),
            ["regions"] = regions is null
                ? ComponentStatus("unsupported_blocking", "unknown", regionError ?? "war3map.w3r was not available for read-only parsing.")
                : ComponentStatus("parsed_read_only", "observed_archive", "Regions were parsed read-only from war3map.w3r."),
            ["archive_members"] = ComponentStatus("mixed", "observed_archive", "Archive membership and per-member hashes are observed; parser/preservation capability is reported per member."),
            ["capabilities"] = ComponentStatus("parsed_read_only", "derived", "Per-member parser results were derived from a read-only archive probe."),
            ["triggers"] = OpaqueStatus("triggers", triggers),
            ["scripts"] = OpaqueStatus("scripts", scripts),
            ["cameras"] = OpaqueStatus("cameras", cameras),
            ["variables"] = ComponentStatus("preserved_opaque", "unknown", "Trigger variable details are not exposed until the trigger format is proven for this map."),
            ["object_data"] = OpaqueStatus("object_data", objectData),
            ["placed_objects"] = OpaqueStatus("placed_objects", placedObjects),
            ["terrain_summary"] = hasTerrain
                ? ComponentStatus("preserved_opaque", "observed_archive", "Terrain bytes are preserved but are not semantically decoded by this release.")
                : ComponentStatus("preserved_opaque", "unknown", "war3map.w3e is absent; no terrain bytes were available to decode or preserve."),
            ["imports"] = OpaqueStatus("imports", imports),
            ["opaque_members"] = opaqueMembers is { Count: > 0 }
                ? ComponentStatus("preserved_opaque", "observed_archive", "Opaque archive members are preserved byte-for-byte and identified by content hash.")
                : ComponentStatus("preserved_opaque", "observed_archive", "The archive probe found no opaque members."),
            ["parse_warnings"] = parseWarnings is { Count: > 0 }
                ? ComponentStatus("parsed_read_only", "derived", "Warnings were derived from read-only component parsing.")
                : ComponentStatus("parsed_read_only", "derived", "No parse warnings were produced by the read-only inspection.")
        };

        _ = archive;
        _ = parseResults;
        return statuses;
    }

    private static JsonObject OpaqueStatus(string component, JsonArray? values) => values is { Count: > 0 }
        ? ComponentStatus("preserved_opaque", "observed_archive", $"{component} archive members are preserved but not semantically decoded by this release.")
        : ComponentStatus("preserved_opaque", "observed_archive", $"No {component} archive members are present in this map.");

    private static JsonObject ComponentStatus(string capability, string provenance, string reason) => new()
    {
        ["capability"] = capability,
        ["provenance"] = provenance,
        ["reason"] = reason
    };

    private static JsonArray BuildArchiveMembers(MapArchiveSnapshot archive, JsonArray parseResults)
    {
        var result = new JsonArray();
        foreach (var member in archive.Members)
        {
            var probe = parseResults.First(x => string.Equals(x!["path"]!.GetValue<string>(), member.Path, StringComparison.OrdinalIgnoreCase));
            result.Add(new JsonObject
            {
                ["path"] = member.Path,
                ["size_bytes"] = member.Size,
                ["compressed_size_bytes"] = member.CompressedSize,
                ["sha256"] = member.Sha256,
                ["named"] = member.Named,
                ["flags"] = (uint)member.Flags,
                ["capability"] = probe!["status"]?.DeepClone(),
                ["provenance"] = "observed_archive"
            });
        }

        return result;
    }

    private static JsonArray BuildComponentMembers(MapArchiveSnapshot archive, IEnumerable<string> names, string component)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return new JsonArray(archive.Members.Where(x => wanted.Contains(x.Path)).Select(x => (JsonNode)new JsonObject
        {
            ["component"] = component,
            ["archive_path"] = x.Path,
            ["size_bytes"] = x.Size,
            ["sha256"] = x.Sha256,
            ["capability"] = "preserved_opaque",
            ["provenance"] = "observed_archive"
        }).ToArray());
    }

    private static JsonArray BuildScripts(MapArchiveSnapshot archive, JsonObject? info)
    {
        var language = info?["script_language"]?.GetValue<string>() ?? "unknown";
        return new JsonArray(archive.Members.Where(x => x.Path.Equals("war3map.j", StringComparison.OrdinalIgnoreCase) || x.Path.Equals("war3map.lua", StringComparison.OrdinalIgnoreCase))
            .Select(x => (JsonNode)new JsonObject
            {
                ["archive_path"] = x.Path,
                ["language"] = language,
                ["size_bytes"] = x.Size,
                ["sha256"] = x.Sha256,
                ["source_sha256"] = x.Sha256,
                ["capability"] = "preserved_opaque",
                ["provenance"] = "observed_archive"
            }).ToArray());
    }

    private static JsonArray BuildObjectData(MapArchiveSnapshot archive)
    {
        return new JsonArray(archive.Members.Where(x => ObjectMembers.Contains(x.Path)).Select(x => (JsonNode)new JsonObject
        {
            ["archive_path"] = x.Path,
            ["category"] = Path.GetFileNameWithoutExtension(x.Path).Replace("war3map.", string.Empty, StringComparison.OrdinalIgnoreCase),
            ["size_bytes"] = x.Size,
            ["sha256"] = x.Sha256,
            ["capability"] = "preserved_opaque",
            ["provenance"] = "observed_archive"
        }).ToArray());
    }

    private static JsonObject BuildTerrainSummary(MapArchiveSnapshot archive)
    {
        var member = archive.Find("war3map.w3e");
        return member is null ? UnknownComponent("terrain", "war3map.w3e is absent") : new JsonObject
        {
            ["archive_path"] = member.Path,
            ["size_bytes"] = member.Size,
            ["sha256"] = member.Sha256,
            ["capability"] = "preserved_opaque",
            ["provenance"] = "observed_archive",
            ["detail"] = "Terrain grid is preserved but not semantically decoded by this release."
        };
    }

    private static JsonObject ToJson(JsonNode? node) => (JsonObject)node!.DeepClone();

    private static void ResolveTriggerStrings(JsonObject info, ArchiveMemberData? triggerStringsMember)
    {
        if (triggerStringsMember is null)
        {
            return;
        }

        var strings = ParseTriggerStrings(triggerStringsMember.Bytes);
        foreach (var field in new[] { "title", "author", "description", "recommended_players" })
        {
            if (info[field] is not JsonObject value || value["value"] is not JsonValue stored || !stored.TryGetValue<string>(out var token))
            {
                continue;
            }

            if (strings.TryGetValue(token, out var resolved))
            {
                value["stored_value"] = token;
                value["value"] = resolved;
            }
        }

        foreach (var collectionName in new[] { "players", "forces" })
        {
            if (info[collectionName] is not JsonArray collection)
            {
                continue;
            }

            foreach (var item in collection.OfType<JsonObject>())
            {
                if (item["name"] is JsonValue stored && stored.TryGetValue<string>(out var token) && strings.TryGetValue(token, out var resolved))
                {
                    item["stored_name"] = token;
                    item["name"] = resolved;
                }
            }
        }

        if (info["recommended_players"] is JsonObject recommended && recommended["value"] is JsonValue recommendedValue && recommendedValue.TryGetValue<string>(out var text))
        {
            info["suggested_players"] = Value(TryParseSuggestedPlayers(text), "derived", "parsed_read_only");
        }
    }

    private static Dictionary<string, string> ParseTriggerStrings(byte[] bytes)
        => new(ScriptOwnership.ParseTriggerStrings(bytes), StringComparer.OrdinalIgnoreCase);

    private static int? TryParseSuggestedPlayers(string? recommendedPlayers)
    {
        if (string.IsNullOrWhiteSpace(recommendedPlayers))
        {
            return null;
        }

        var digits = new string(recommendedPlayers.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
