using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using War3Net.Build;
using War3Net.Build.Common;
using War3Net.Build.Environment;
using War3Net.Build.Extensions;
using War3Net.Build.Info;
using War3Net.Build.Object;
using War3Net.Build.Widget;
using War3Net.Common.Extensions;

namespace Wc3MapEngine.Core;

/// <summary>
/// Narrow, typed adapters for map members which the MCP is allowed to mutate.
/// The canonical JSON intentionally contains the complete known War3Net value
/// for each record so a changed member can be rebuilt without falling back to
/// arbitrary MPQ patching. Values not represented by the pinned library remain
/// visible as opaque archive members and are never silently discarded.
/// </summary>
public static class MapComponentCodec
{
    public const string CodecVersion = "war3net-6.0.3-typed-components-1";

    private static readonly Dictionary<string, string> ObjectCategoryByMember = new(StringComparer.OrdinalIgnoreCase)
    {
        ["war3map.w3u"] = "unit",
        ["war3map.w3a"] = "ability",
        ["war3map.w3t"] = "item",
        ["war3map.w3b"] = "destructable",
        ["war3map.w3d"] = "doodad",
        ["war3map.w3h"] = "buff",
        ["war3map.w3q"] = "upgrade"
    };

    public static bool IsObjectMember(string path) => ObjectCategoryByMember.ContainsKey(path);

    public static string ObjectCategory(string path)
        => ObjectCategoryByMember.TryGetValue(path, out var category)
            ? category
            : throw new EngineException("INVALID_ARGUMENT", $"'{path}' is not a supported object-data member.");

    public static string ObjectMemberForCategory(string category)
        => category.ToLowerInvariant() switch
        {
            "unit" => "war3map.w3u",
            "ability" => "war3map.w3a",
            "item" => "war3map.w3t",
            "destructable" => "war3map.w3b",
            "doodad" => "war3map.w3d",
            "buff" => "war3map.w3h",
            "upgrade" => "war3map.w3q",
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported object category '{category}'.")
        };

    public static JsonObject ToPlayer(PlayerData player)
        => new()
        {
            ["id"] = player.Id + 1,
            ["name"] = player.Name,
            ["controller"] = player.Controller.ToString(),
            ["race"] = player.Race.ToString(),
            ["flags"] = (int)player.Flags,
            ["start"] = new JsonObject { ["x"] = player.StartPosition.X, ["y"] = player.StartPosition.Y },
            ["ally_low_priority_mask"] = player.AllyLowPriorityFlags.ToInt32(),
            ["ally_high_priority_mask"] = player.AllyHighPriorityFlags.ToInt32(),
            ["enemy_low_priority_mask"] = player.EnemyLowPriorityFlags.ToInt32(),
            ["enemy_high_priority_mask"] = player.EnemyHighPriorityFlags.ToInt32(),
            ["provenance"] = "observed_archive",
            ["capability"] = "parsed_read_only"
        };

    public static JsonObject ToForce(ForceData force, int index, int playerCount)
        => new()
        {
            ["index"] = index,
            ["name"] = force.Name,
            ["flags"] = (int)force.Flags,
            ["player_mask"] = force.Players.ToInt32(),
            ["player_ids"] = new JsonArray(Enumerable.Range(0, playerCount)
                .Where(player => force.Players[player])
                .Select(player => (JsonNode)JsonValue.Create(player + 1)!)
                .ToArray()),
            ["provenance"] = "observed_archive",
            ["capability"] = "parsed_read_only"
        };

    public static JsonArray ToRegions(MapRegions regions)
    {
        var result = new JsonArray();
        if (regions.Protected)
        {
            throw new EngineException("UNSUPPORTED_COMPONENT", "war3map.w3r is protected and cannot be represented safely by the typed region codec.");
        }

        foreach (var region in regions.Regions)
        {
            result.Add(new JsonObject
            {
                ["id"] = RegionId(region),
                ["name"] = region.Name,
                ["min_x"] = region.Left,
                ["min_y"] = region.Bottom,
                ["max_x"] = region.Right,
                ["max_y"] = region.Top,
                ["creation_number"] = region.CreationNumber,
                ["weather"] = region.WeatherType.ToString(),
                ["ambient_sound"] = region.AmbientSound,
                ["color_argb"] = region.Color.ToArgb(),
                ["references"] = RegionSupport.EmptyReferences(),
                ["provenance"] = "observed_archive",
                ["capability"] = "typed_write_enabled",
                ["codec_version"] = RegionSupport.CodecVersion
            });
        }

        return result;
    }

    public static JsonArray ToUnits(MapUnits units)
    {
        var result = new JsonArray();
        foreach (var unit in units.Units)
        {
            result.Add(new JsonObject
            {
                ["id"] = PlacementId("unit", unit.CreationNumber),
                ["member"] = "war3mapUnits.doo",
                ["kind"] = "unit",
                ["rawcode"] = unit.TypeId.ToRawcode(),
                ["skin_rawcode"] = unit.SkinId.ToRawcode(),
                ["owner_id"] = unit.OwnerId + 1,
                ["flags"] = unit.Flags,
                ["unknown_1"] = unit.Unk1,
                ["unknown_2"] = unit.Unk2,
                ["hit_points"] = unit.HP,
                ["mana_points"] = unit.MP,
                ["gold_amount"] = unit.GoldAmount,
                ["target_acquisition"] = unit.TargetAcquisition,
                ["hero_level"] = unit.HeroLevel,
                ["hero_strength"] = unit.HeroStrength,
                ["hero_agility"] = unit.HeroAgility,
                ["hero_intelligence"] = unit.HeroIntelligence,
                ["inventory"] = new JsonArray(unit.InventoryData.Select(item => (JsonNode)new JsonObject
                {
                    ["slot"] = item.Slot,
                    ["rawcode"] = item.ItemId.ToRawcode()
                }).ToArray()),
                ["abilities"] = new JsonArray(unit.AbilityData.Select(ability => (JsonNode)new JsonObject
                {
                    ["rawcode"] = ability.AbilityId.ToRawcode(),
                    ["autocast_active"] = ability.IsAutocastActive,
                    ["hero_ability_level"] = ability.HeroAbilityLevel
                }).ToArray()),
                ["random_data_mode"] = unit.RandomDataMode.ToString(),
                ["random_data"] = RandomData(unit.RandomData),
                ["custom_player_color_id"] = unit.CustomPlayerColorId,
                ["waygate_destination_region_id"] = unit.WaygateDestinationRegionId,
                ["variation"] = unit.Variation,
                ["position"] = Position(unit.Position),
                ["facing"] = unit.Rotation,
                ["scale"] = Position(unit.Scale),
                ["map_item_table_id"] = unit.MapItemTableId,
                ["item_table_sets"] = ToItemTableSets(unit.ItemTableSets),
                ["creation_number"] = unit.CreationNumber,
                ["provenance"] = "observed_archive",
                ["capability"] = "roundtrip_verified"
            });
        }

        return result;
    }

    public static JsonArray ToDoodads(MapDoodads doodads)
    {
        var result = new JsonArray();
        foreach (var doodad in doodads.Doodads)
        {
            result.Add(new JsonObject
            {
                ["id"] = PlacementId("doodad", doodad.CreationNumber),
                ["member"] = "war3map.doo",
                ["kind"] = "doodad",
                ["rawcode"] = doodad.TypeId.ToRawcode(),
                ["state"] = doodad.State.ToString(),
                ["life"] = doodad.Life,
                ["variation"] = doodad.Variation,
                ["position"] = Position(doodad.Position),
                ["facing"] = doodad.Rotation,
                ["scale"] = Position(doodad.Scale),
                ["skin_rawcode"] = doodad.SkinId.ToRawcode(),
                ["map_item_table_id"] = doodad.MapItemTableId,
                ["item_table_sets"] = ToItemTableSets(doodad.ItemTableSets),
                ["creation_number"] = doodad.CreationNumber,
                ["provenance"] = "observed_archive",
                ["capability"] = "roundtrip_verified"
            });
        }

        foreach (var special in doodads.SpecialDoodads)
        {
            result.Add(new JsonObject
            {
                ["id"] = $"special-doodad:{special.TypeId.ToRawcode()}:{special.Position.X}:{special.Position.Y}",
                ["member"] = "war3map.doo",
                ["kind"] = "special_doodad",
                ["rawcode"] = special.TypeId.ToRawcode(),
                ["variation"] = special.Variation,
                ["position"] = new JsonObject { ["x"] = special.Position.X, ["y"] = special.Position.Y, ["z"] = 0 },
                ["provenance"] = "observed_archive",
                ["capability"] = "roundtrip_verified"
            });
        }

        return result;
    }

    public static JsonArray ToObjectDefinitions(string archivePath, byte[] bytes)
    {
        var category = ObjectCategory(archivePath);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        var value = ReadObjectMember(reader, category);
        var result = new JsonArray();
        foreach (var (custom, modification) in EnumerateObjectModifications(value, category))
        {
            result.Add(ToObjectDefinition(archivePath, category, custom, modification));
        }

        return result;
    }

    public static MapInfo BuildInfo(MapInfo source, JsonArray players, JsonArray forces)
    {
        source.Players.Clear();
        source.Players.AddRange(players.OfType<JsonObject>().Select(ToPlayer).OrderBy(player => player.Id));
        source.Forces.Clear();
        source.Forces.AddRange(forces.OfType<JsonObject>().Select(ToForce));
        return source;
    }

    public static MapRegions BuildRegions(MapRegions source, JsonArray canonicalRegions)
    {
        source.Regions.Clear();
        source.Regions.AddRange(canonicalRegions.OfType<JsonObject>().Select(ToRegion));
        return source;
    }

    public static MapUnits BuildUnits(MapUnits source, JsonArray placements)
    {
        source.Units.Clear();
        source.Units.AddRange(placements.OfType<JsonObject>()
            .Where(item => !string.Equals(item["member"]?.GetValue<string>(), "war3map.doo", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item["kind"]?.GetValue<string>(), "doodad", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item["kind"]?.GetValue<string>(), "special_doodad", StringComparison.OrdinalIgnoreCase))
            .Select(ToUnit)
            .ToList());
        return source;
    }

    public static MapDoodads BuildDoodads(MapDoodads source, JsonArray placements)
    {
        source.Doodads.Clear();
        source.Doodads.AddRange(placements.OfType<JsonObject>()
            .Where(item => string.Equals(item["member"]?.GetValue<string>(), "war3map.doo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item["kind"]?.GetValue<string>(), "doodad", StringComparison.OrdinalIgnoreCase))
            .Select(ToDoodad)
            .ToList());
        return source;
    }

    public static byte[] SerializeInfo(MapInfo info) => Serialize(writer => writer.Write(info));
    public static byte[] SerializeRegions(MapRegions regions) => Serialize(writer => writer.Write(regions));
    public static byte[] SerializeUnits(MapUnits units) => Serialize(writer => writer.Write(units));
    public static byte[] SerializeDoodads(MapDoodads doodads) => Serialize(writer => writer.Write(doodads));

    public static byte[] SerializeObjectMember(string archivePath, byte[] sourceBytes, JsonArray definitions)
    {
        var category = ObjectCategory(archivePath);
        using var sourceStream = new MemoryStream(sourceBytes, writable: false);
        using var sourceReader = new BinaryReader(sourceStream);
        var source = ReadObjectMember(sourceReader, category);
        var format = source switch
        {
            UnitObjectData value => value.FormatVersion,
            AbilityObjectData value => value.FormatVersion,
            ItemObjectData value => value.FormatVersion,
            DestructableObjectData value => value.FormatVersion,
            DoodadObjectData value => value.FormatVersion,
            BuffObjectData value => value.FormatVersion,
            UpgradeObjectData value => value.FormatVersion,
            _ => throw new EngineException("BUILD_UNSUPPORTED", $"Object member '{archivePath}' has no supported format.")
        };

        var rebuilt = BuildObjectMember(category, format, definitions);
        return Serialize(writer => WriteObjectMember(writer, rebuilt));
    }

    public static byte[] SerializeObjectMember(string archivePath, JsonArray definitions)
    {
        var category = ObjectCategory(archivePath);
        var rebuilt = BuildObjectMember(category, ObjectDataFormatVersion.v3, definitions);
        return Serialize(writer => WriteObjectMember(writer, rebuilt));
    }

    public static string RegionId(Region region) => $"region:{region.CreationNumber}";
    public static string PlacementId(string kind, int creationNumber) => $"{kind}:{creationNumber}";

    private static PlayerData ToPlayer(JsonObject value)
    {
        var id = RequiredInt(value, "id", 1, 24) - 1;
        var start = RequiredPosition2(value["start"], "start");
        return new PlayerData(id)
        {
            Name = String(value, "stored_name") ?? String(value, "name") ?? $"Player {id + 1}",
            Controller = EnumValue<PlayerController>(value, "controller", PlayerController.None),
            Race = EnumValue<PlayerRace>(value, "race", PlayerRace.Selectable),
            Flags = (PlayerFlags)RequiredInt(value, "flags", 0, int.MaxValue),
            StartPosition = start,
            AllyLowPriorityFlags = new Bitmask32(RequiredInt(value, "ally_low_priority_mask", 0, int.MaxValue)),
            AllyHighPriorityFlags = new Bitmask32(RequiredInt(value, "ally_high_priority_mask", 0, int.MaxValue)),
            EnemyLowPriorityFlags = new Bitmask32(RequiredInt(value, "enemy_low_priority_mask", 0, int.MaxValue)),
            EnemyHighPriorityFlags = new Bitmask32(RequiredInt(value, "enemy_high_priority_mask", 0, int.MaxValue))
        };
    }

    private static ForceData ToForce(JsonObject value)
    {
        var ids = PlayerIds(value["player_ids"]);
        var mask = value["player_mask"] is null ? ids.Aggregate(0, (current, id) => current | (1 << (id - 1))) : RequiredInt(value, "player_mask", int.MinValue, int.MaxValue);
        return new ForceData
        {
            Name = String(value, "stored_name") ?? String(value, "name") ?? "Force",
            Flags = (ForceFlags)RequiredInt(value, "flags", 0, int.MaxValue),
            Players = new Bitmask32(mask)
        };
    }

    private static Region ToRegion(JsonObject value)
        => new()
        {
            Name = String(value, "stored_name") ?? RequiredString(value, "name"),
            Left = RequiredFloat(value, "min_x"),
            Bottom = RequiredFloat(value, "min_y"),
            Right = RequiredFloat(value, "max_x"),
            Top = RequiredFloat(value, "max_y"),
            CreationNumber = RequiredInt(value, "creation_number", 0, int.MaxValue),
            WeatherType = EnumValue<WeatherType>(value, "weather", WeatherType.None),
            AmbientSound = String(value, "ambient_sound") ?? string.Empty,
            Color = value["color_argb"] is null ? System.Drawing.Color.Empty : System.Drawing.Color.FromArgb(RequiredInt(value, "color_argb", int.MinValue, int.MaxValue))
        };

    private static UnitData ToUnit(JsonObject value)
    {
        var rawcode = RequiredRawcode(value, "rawcode");
        var position = RequiredPosition(value["position"], "position");
        var scale = RequiredPosition(value["scale"], "scale");
        var owner = RequiredInt(value, "owner_id", 1, 24) - 1;
        return new UnitData
        {
            Flags = (byte)RequiredInt(value, "flags", 0, byte.MaxValue),
            OwnerId = owner,
            Unk1 = (byte)RequiredInt(value, "unknown_1", 0, byte.MaxValue),
            Unk2 = (byte)RequiredInt(value, "unknown_2", 0, byte.MaxValue),
            HP = RequiredInt(value, "hit_points", int.MinValue, int.MaxValue),
            MP = RequiredInt(value, "mana_points", int.MinValue, int.MaxValue),
            GoldAmount = RequiredInt(value, "gold_amount", 0, int.MaxValue),
            TargetAcquisition = RequiredFloat(value, "target_acquisition"),
            HeroLevel = RequiredInt(value, "hero_level", 0, int.MaxValue),
            HeroStrength = RequiredInt(value, "hero_strength", 0, int.MaxValue),
            HeroAgility = RequiredInt(value, "hero_agility", 0, int.MaxValue),
            HeroIntelligence = RequiredInt(value, "hero_intelligence", 0, int.MaxValue),
            InventoryData = (value["inventory"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(item => new InventoryItemData
            {
                Slot = RequiredInt(item, "slot", 0, 5),
                ItemId = RequiredRawcode(item, "rawcode")
            }).ToList(),
            AbilityData = (value["abilities"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(item => new ModifiedAbilityData
            {
                AbilityId = RequiredRawcode(item, "rawcode"),
                IsAutocastActive = Boolean(item, "autocast_active"),
                HeroAbilityLevel = RequiredInt(item, "hero_ability_level", 0, int.MaxValue)
            }).ToList(),
            RandomData = ToRandomData(value),
            CustomPlayerColorId = RequiredInt(value, "custom_player_color_id", -1, 24),
            WaygateDestinationRegionId = RequiredInt(value, "waygate_destination_region_id", -1, int.MaxValue),
            TypeId = rawcode,
            Variation = RequiredInt(value, "variation", 0, int.MaxValue),
            Position = position,
            Rotation = RequiredFloat(value, "facing"),
            Scale = scale,
            SkinId = value["skin_rawcode"] is null ? rawcode : RequiredRawcode(value, "skin_rawcode"),
            MapItemTableId = RequiredInt(value, "map_item_table_id", -1, int.MaxValue),
            CreationNumber = RequiredInt(value, "creation_number", 0, int.MaxValue)
        };
    }

    private static DoodadData ToDoodad(JsonObject value)
        => new()
        {
            State = EnumValue<DoodadState>(value, "state", DoodadState.Normal),
            Life = (byte)RequiredInt(value, "life", 0, byte.MaxValue),
            TypeId = RequiredRawcode(value, "rawcode"),
            Variation = RequiredInt(value, "variation", 0, int.MaxValue),
            Position = RequiredPosition(value["position"], "position"),
            Rotation = RequiredFloat(value, "facing"),
            Scale = RequiredPosition(value["scale"], "scale"),
            SkinId = value["skin_rawcode"] is null ? RequiredRawcode(value, "rawcode") : RequiredRawcode(value, "skin_rawcode"),
            MapItemTableId = RequiredInt(value, "map_item_table_id", -1, int.MaxValue),
            CreationNumber = RequiredInt(value, "creation_number", 0, int.MaxValue)
        };

    private static RandomUnitData? ToRandomData(JsonObject value)
    {
        if (value["random_data"] is not JsonObject random)
        {
            return null;
        }

        var mode = String(random, "mode") ?? String(value, "random_data_mode") ?? "Any";
        return mode switch
        {
            "Any" => new RandomUnitAny
            {
                Level = RequiredInt(random, "level", 1, int.MaxValue),
                Class = EnumValue<ItemClass>(random, "class", (ItemClass)0)
            },
            "GlobalTable" => new RandomUnitGlobalTable
            {
                TableId = RequiredInt(random, "table_id", 0, int.MaxValue),
                Column = RequiredInt(random, "column", 0, int.MaxValue)
            },
            "CustomTable" => new RandomUnitCustomTable
            {
                RandomUnits = (random["units"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(unit => new RandomUnitTableUnit
                {
                    UnitId = RequiredRawcode(unit, "rawcode"),
                    Chance = RequiredInt(unit, "chance", 0, int.MaxValue)
                }).ToList()
            },
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown random_data mode '{mode}'.")
        };
    }

    private static JsonObject ToObjectDefinition(string archivePath, string category, bool custom, object modification)
    {
        var oldId = RequiredObjectId(modification, "OldId");
        var newId = RequiredObjectId(modification, "NewId");
        var result = new JsonObject
        {
            ["id"] = $"{archivePath}:{(custom ? "new" : "base")}:{oldId.ToRawcode()}:{newId.ToRawcode()}",
            ["archive_path"] = archivePath,
            ["category"] = category,
            ["object_kind"] = custom ? "custom" : "base",
            ["base_rawcode"] = oldId.ToRawcode(),
            ["custom_rawcode"] = newId.ToRawcode(),
            ["rawcode"] = (custom ? newId : oldId).ToRawcode(),
            ["unknown_ids"] = new JsonArray(GetProperty<List<int>>(modification, "Unk").Select(id => (JsonNode)JsonValue.Create(id.ToRawcode())!).ToArray()),
            ["modifications"] = new JsonArray()
        };
        var mods = result["modifications"]!.AsArray();
        foreach (var item in (GetProperty<System.Collections.IEnumerable>(modification, "Modifications") ?? Array.Empty<object>()).Cast<object>())
        {
            var mod = new JsonObject
            {
                ["id"] = RequiredObjectId(item, "Id").ToRawcode(),
                ["type"] = EnumValueFromProperty(item, "Type"),
                ["value"] = ObjectModificationValue(item)
            };
            if (item.GetType().Name.Contains("Level", StringComparison.Ordinal))
            {
                mod["level"] = GetProperty<int>(item, "Level");
                mod["pointer"] = GetProperty<int>(item, "Pointer");
            }
            else if (item.GetType().Name.Contains("Variation", StringComparison.Ordinal))
            {
                mod["variation"] = GetProperty<int>(item, "Variation");
                mod["pointer"] = GetProperty<int>(item, "Pointer");
            }
            mods.Add(mod);
        }

        result["provenance"] = "observed_archive";
        result["capability"] = "roundtrip_verified";
        return result;
    }

    private static object ReadObjectMember(BinaryReader reader, string category)
        => category switch
        {
            "unit" => reader.ReadUnitObjectData(),
            "ability" => reader.ReadAbilityObjectData(),
            "item" => reader.ReadItemObjectData(),
            "destructable" => reader.ReadDestructableObjectData(),
            "doodad" => reader.ReadDoodadObjectData(),
            "buff" => reader.ReadBuffObjectData(),
            "upgrade" => reader.ReadUpgradeObjectData(),
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported object category '{category}'.")
        };

    private static IEnumerable<(bool Custom, object Modification)> EnumerateObjectModifications(object value, string category)
    {
        var simple = category is "unit" or "item" or "destructable" or "buff";
        foreach (var property in value.GetType().GetProperties().Where(property => property.Name is "BaseUnits" or "NewUnits" or "BaseItems" or "NewItems" or "BaseDestructables" or "NewDestructables" or "BaseBuffs" or "NewBuffs" or "BaseAbilities" or "NewAbilities" or "BaseDoodads" or "NewDoodads" or "BaseUpgrades" or "NewUpgrades"))
        {
            if (!property.Name.StartsWith("Base", StringComparison.Ordinal) && !property.Name.StartsWith("New", StringComparison.Ordinal)) continue;
            if (property.GetValue(value) is not System.Collections.IEnumerable items) continue;
            var custom = property.Name.StartsWith("New", StringComparison.Ordinal);
            foreach (var item in items)
            {
                if (simple && item.GetType().Name.Contains("SimpleObjectModification", StringComparison.Ordinal)) yield return (custom, item);
                else if (!simple && item.GetType().Name.Contains("ObjectModification", StringComparison.Ordinal)) yield return (custom, item);
            }
        }
    }

    private static object BuildObjectMember(string category, ObjectDataFormatVersion format, JsonArray definitions)
    {
        var values = definitions.OfType<JsonObject>().Select(value => (Value: value, Custom: string.Equals(value["object_kind"]?.GetValue<string>(), "custom", StringComparison.OrdinalIgnoreCase))).ToList();
        return category switch
        {
            "unit" => new UnitObjectData(format) { BaseUnits = values.Where(x => !x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList(), NewUnits = values.Where(x => x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList() },
            "item" => new ItemObjectData(format) { BaseItems = values.Where(x => !x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList(), NewItems = values.Where(x => x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList() },
            "destructable" => new DestructableObjectData(format) { BaseDestructables = values.Where(x => !x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList(), NewDestructables = values.Where(x => x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList() },
            "buff" => new BuffObjectData(format) { BaseBuffs = values.Where(x => !x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList(), NewBuffs = values.Where(x => x.Custom).Select(x => ToSimpleObjectModification(x.Value)).ToList() },
            "ability" => new AbilityObjectData(format) { BaseAbilities = values.Where(x => !x.Custom).Select(x => ToLevelObjectModification(x.Value)).ToList(), NewAbilities = values.Where(x => x.Custom).Select(x => ToLevelObjectModification(x.Value)).ToList() },
            "upgrade" => new UpgradeObjectData(format) { BaseUpgrades = values.Where(x => !x.Custom).Select(x => ToLevelObjectModification(x.Value)).ToList(), NewUpgrades = values.Where(x => x.Custom).Select(x => ToLevelObjectModification(x.Value)).ToList() },
            "doodad" => new DoodadObjectData(format) { BaseDoodads = values.Where(x => !x.Custom).Select(x => ToVariationObjectModification(x.Value)).ToList(), NewDoodads = values.Where(x => x.Custom).Select(x => ToVariationObjectModification(x.Value)).ToList() },
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unsupported object category '{category}'.")
        };
    }

    private static SimpleObjectModification ToSimpleObjectModification(JsonObject value)
        => new()
        {
            OldId = RequiredRawcode(value, "base_rawcode"),
            NewId = RequiredRawcode(value, "custom_rawcode"),
            Unk = RawcodeList(value["unknown_ids"]),
            Modifications = (value["modifications"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(ToSimpleObjectDataModification).ToList()
        };

    private static LevelObjectModification ToLevelObjectModification(JsonObject value)
        => new()
        {
            OldId = RequiredRawcode(value, "base_rawcode"),
            NewId = RequiredRawcode(value, "custom_rawcode"),
            Unk = RawcodeList(value["unknown_ids"]),
            Modifications = (value["modifications"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(ToLevelObjectDataModification).ToList()
        };

    private static VariationObjectModification ToVariationObjectModification(JsonObject value)
        => new()
        {
            OldId = RequiredRawcode(value, "base_rawcode"),
            NewId = RequiredRawcode(value, "custom_rawcode"),
            Unk = RawcodeList(value["unknown_ids"]),
            Modifications = (value["modifications"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(ToVariationObjectDataModification).ToList()
        };

    private static SimpleObjectDataModification ToSimpleObjectDataModification(JsonObject value)
        => new() { Id = RequiredRawcode(value, "id"), Type = ObjectType(value["type"]), Value = ObjectValue(value["type"], value["value"]) };

    private static LevelObjectDataModification ToLevelObjectDataModification(JsonObject value)
        => new() { Level = RequiredInt(value, "level", 0, int.MaxValue), Pointer = RequiredInt(value, "pointer", 0, int.MaxValue), Id = RequiredRawcode(value, "id"), Type = ObjectType(value["type"]), Value = ObjectValue(value["type"], value["value"]) };

    private static VariationObjectDataModification ToVariationObjectDataModification(JsonObject value)
        => new() { Variation = RequiredInt(value, "variation", 0, int.MaxValue), Pointer = RequiredInt(value, "pointer", 0, int.MaxValue), Id = RequiredRawcode(value, "id"), Type = ObjectType(value["type"]), Value = ObjectValue(value["type"], value["value"]) };

    private static void WriteObjectMember(BinaryWriter writer, object value)
    {
        switch (value)
        {
            case UnitObjectData item: writer.Write(item); break;
            case AbilityObjectData item: writer.Write(item); break;
            case ItemObjectData item: writer.Write(item); break;
            case DestructableObjectData item: writer.Write(item); break;
            case DoodadObjectData item: writer.Write(item); break;
            case BuffObjectData item: writer.Write(item); break;
            case UpgradeObjectData item: writer.Write(item); break;
            default: throw new EngineException("BUILD_UNSUPPORTED", "Unsupported object-data value.");
        }
    }

    private static JsonNode ObjectModificationValue(object item)
    {
        var type = EnumValueFromProperty(item, "Type");
        return type switch
        {
            "Int" => JsonValue.Create(GetProperty<int>(item, "ValueAsInt"))!,
            "Real" or "Unreal" => JsonValue.Create(GetProperty<float>(item, "ValueAsFloat"))!,
            "String" => JsonValue.Create(GetProperty<string>(item, "ValueAsString"))!,
            "Bool" => JsonValue.Create(GetProperty<bool>(item, "ValueAsBool"))!,
            "Char" => JsonValue.Create(GetProperty<char>(item, "ValueAsChar").ToString())!,
            _ => throw new EngineException("PARSE_FAILED", $"Unknown object-data value type '{type}'.")
        };
    }

    private static object ObjectValue(JsonNode? type, JsonNode? value)
    {
        var kind = type?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "Object-data modification type is required.");
        return kind switch
        {
            "Int" => value?.GetValue<int>() ?? throw new EngineException("INVALID_ARGUMENT", "Int object-data value is required."),
            "Real" or "Unreal" => value?.GetValue<float>() ?? throw new EngineException("INVALID_ARGUMENT", "Real object-data value is required."),
            "String" => value?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "String object-data value is required."),
            "Bool" => value?.GetValue<bool>() ?? throw new EngineException("INVALID_ARGUMENT", "Bool object-data value is required."),
            "Char" => (value?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "Char object-data value is required."))[0],
            _ => throw new EngineException("INVALID_ARGUMENT", $"Unknown object-data value type '{kind}'.")
        };
    }

    private static ObjectDataType ObjectType(JsonNode? value)
        => Enum.TryParse<ObjectDataType>(value?.GetValue<string>(), ignoreCase: false, out var result)
            ? result
            : throw new EngineException("INVALID_ARGUMENT", $"Unknown object-data type '{value}'.");

    private static string EnumValueFromProperty(object item, string property)
        => GetProperty<object>(item, property)?.ToString() ?? throw new EngineException("PARSE_FAILED", $"Object-data property '{property}' was null.");

    private static T GetProperty<T>(object item, string property)
    {
        var value = item.GetType().GetProperty(property)?.GetValue(item);
        if (value is T typed) return typed;
        throw new EngineException("PARSE_FAILED", $"Object-data property '{property}' had an unexpected type.");
    }

    private static int RequiredObjectId(object item, string property)
        => GetProperty<int>(item, property);

    private static JsonObject Position(Vector3 value) => new() { ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z };

    private static JsonNode? RandomData(RandomUnitData? value)
    {
        return value switch
        {
            RandomUnitAny any => new JsonObject { ["mode"] = "Any", ["level"] = any.Level, ["class"] = any.Class.ToString() },
            RandomUnitGlobalTable global => new JsonObject { ["mode"] = "GlobalTable", ["table_id"] = global.TableId, ["column"] = global.Column },
            RandomUnitCustomTable custom => new JsonObject
            {
                ["mode"] = "CustomTable",
                ["units"] = new JsonArray(custom.RandomUnits.Select(unit => (JsonNode)new JsonObject { ["rawcode"] = unit.UnitId.ToRawcode(), ["chance"] = unit.Chance }).ToArray())
            },
            _ => null
        };
    }

    private static JsonArray ToItemTableSets<T>(IEnumerable<T> values)
        => new(values.Select(value => (JsonNode)new JsonObject
        {
            ["items"] = new JsonArray((value?.GetType().GetProperty("Items")?.GetValue(value) as System.Collections.IEnumerable ?? Array.Empty<object>())
                .Cast<object>()
                .Select(item => (JsonNode)new JsonObject
                {
                    ["rawcode"] = GetProperty<int>(item, "ItemId").ToRawcode(),
                    ["chance"] = GetProperty<int>(item, "Chance")
                }).ToArray())
        }).ToArray());

    private static List<int> RawcodeList(JsonNode? value)
        => (value as JsonArray ?? new JsonArray()).Select(item => RequiredRawcode(item)).ToList();

    private static int RequiredRawcode(JsonObject value, string property) => RequiredRawcode(value[property]);
    private static int RequiredRawcode(JsonNode? value)
    {
        var rawcode = value?.GetValue<string>() ?? throw new EngineException("INVALID_ARGUMENT", "A rawcode is required.");
        if (rawcode.Length != 4 || rawcode.Any(character => character < 0x20 || character > 0x7E)) throw new EngineException("INVALID_ARGUMENT", $"Rawcode '{rawcode}' must contain exactly four printable ASCII characters.");
        return rawcode.FromRawcode();
    }

    private static IEnumerable<int> PlayerIds(JsonNode? value)
        => (value as JsonArray ?? new JsonArray()).Select(item => item?.GetValue<int>() ?? throw new EngineException("INVALID_ARGUMENT", "Force player_ids must contain integers."));

    private static string? String(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static string RequiredString(JsonObject value, string property)
        => String(value, property) is { Length: > 0 } text ? text : throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a non-empty string.");

    private static int RequiredInt(JsonObject value, string property, int minimum, int maximum)
    {
        if (value[property] is JsonValue node && node.TryGetValue<int>(out var integer) && integer >= minimum && integer <= maximum) return integer;
        throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be an integer between {minimum} and {maximum}.");
    }

    private static float RequiredFloat(JsonObject value, string property)
    {
        if (value[property] is JsonValue node)
        {
            if (node.TryGetValue<float>(out var single) && float.IsFinite(single)) return single;
            if (node.TryGetValue<double>(out var number) && double.IsFinite(number)) return (float)number;
            if (node.TryGetValue<int>(out var integer)) return integer;
        }
        throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be a finite number.");
    }

    private static Vector2 RequiredPosition2(JsonNode? value, string property)
    {
        var objectValue = value as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be an object.");
        return new Vector2(RequiredFloat(objectValue, "x"), RequiredFloat(objectValue, "y"));
    }

    private static Vector3 RequiredPosition(JsonNode? value, string property)
    {
        var objectValue = value as JsonObject ?? throw new EngineException("INVALID_ARGUMENT", $"Property '{property}' must be an object.");
        return new Vector3(RequiredFloat(objectValue, "x"), RequiredFloat(objectValue, "y"), RequiredFloat(objectValue, "z"));
    }

    private static bool Boolean(JsonObject value, string property)
        => value[property] is JsonValue node && node.TryGetValue<bool>(out var result) ? result : false;

    private static T EnumValue<T>(JsonObject value, string property, T fallback) where T : struct, Enum
        => value[property] is JsonValue node && node.TryGetValue<string>(out var text) && Enum.TryParse<T>(text, true, out var result) ? result : fallback;

    private static byte[] Serialize(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) write(writer);
        return stream.ToArray();
    }
}
