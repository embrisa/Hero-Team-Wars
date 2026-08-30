using System.Text.Json.Nodes;

namespace Wc3MapEngine.Core.Build;

public static class ArchiveComparison
{
    public static JsonObject Compare(MapArchiveSnapshot source, MapArchiveSnapshot rebuilt, IReadOnlySet<string> expectedChangedMembers)
    {
        var sourceByPath = source.Members.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var rebuiltByPath = rebuilt.Members.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var membershipEqual = sourceByPath.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(rebuiltByPath.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var contentChanges = new JsonArray();
        var compressionChanges = new JsonArray();
        var unexpectedChanges = new JsonArray();
        var preservedOpaqueMembers = new JsonArray();
        foreach (var path in sourceByPath.Keys.Union(rebuiltByPath.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            sourceByPath.TryGetValue(path, out var left);
            rebuiltByPath.TryGetValue(path, out var right);
            if (left is null || right is null)
            {
                contentChanges.Add(Change(path, left?.Sha256, right?.Sha256, left?.Size, right?.Size, "membership"));
                if (!expectedChangedMembers.Contains(path)) unexpectedChanges.Add(JsonValue.Create(path));
                continue;
            }

            if (!expectedChangedMembers.Contains(path))
            {
                preservedOpaqueMembers.Add(new JsonObject
                {
                    ["path"] = path,
                    ["source_sha256"] = left.Sha256,
                    ["rebuilt_sha256"] = right.Sha256,
                    ["content_hash_equal"] = left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase)
                });
            }

            if (!string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                contentChanges.Add(Change(path, left.Sha256, right.Sha256, left.Size, right.Size, "content"));
                if (!expectedChangedMembers.Contains(path)) unexpectedChanges.Add(JsonValue.Create(path));
            }

            if (left.CompressedSize != right.CompressedSize || left.Flags != right.Flags)
            {
                compressionChanges.Add(new JsonObject
                {
                    ["path"] = path,
                    ["source_compressed_size_bytes"] = left.CompressedSize,
                    ["rebuilt_compressed_size_bytes"] = right.CompressedSize,
                    ["source_flags"] = (uint)left.Flags,
                    ["rebuilt_flags"] = (uint)right.Flags
                });
            }
        }

        var opaquePaths = source.Members.Where(member => !expectedChangedMembers.Contains(member.Path)).Select(member => member.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var opaqueEqual = opaquePaths.All(path => sourceByPath.TryGetValue(path, out var left) && rebuiltByPath.TryGetValue(path, out var right) && left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase));
        var special = new JsonArray();
        foreach (var path in new[] { "(listfile)", "(attributes)" })
        {
            sourceByPath.TryGetValue(path, out var left);
            rebuiltByPath.TryGetValue(path, out var right);
            special.Add(new JsonObject
            {
                ["path"] = path,
                ["present_in_source"] = left is not null,
                ["present_in_rebuild"] = right is not null,
                ["source_sha256"] = left?.Sha256,
                ["rebuilt_sha256"] = right?.Sha256,
                ["content_hash_equal"] = left is not null && right is not null && left.Sha256.Equals(right.Sha256, StringComparison.OrdinalIgnoreCase)
            });
        }

        return new JsonObject
        {
            ["membership_equal"] = membershipEqual,
            ["source_member_order"] = new JsonArray(source.Members.Select(x => JsonValue.Create(x.Path)).ToArray()),
            ["rebuilt_member_order"] = new JsonArray(rebuilt.Members.Select(x => JsonValue.Create(x.Path)).ToArray()),
            ["member_order_changed"] = !source.Members.Select(x => x.Path).SequenceEqual(rebuilt.Members.Select(x => x.Path), StringComparer.OrdinalIgnoreCase),
            ["content_changes"] = contentChanges,
            ["changed_member_hashes"] = contentChanges.DeepClone(),
            ["compression_metadata_changes"] = compressionChanges,
            ["unexpected_content_changes"] = unexpectedChanges,
            ["planned_membership_changes"] = new JsonArray(contentChanges.OfType<JsonObject>().Where(item => item["kind"]?.GetValue<string>() == "membership").Select(item => item["path"]?.DeepClone()).ToArray()),
            ["opaque_members_preserved"] = opaqueEqual,
            ["preserved_opaque_members"] = preservedOpaqueMembers,
            ["special_member_checks"] = special
        };
    }

    private static JsonObject Change(string path, string? sourceHash, string? rebuiltHash, long? sourceSize, long? rebuiltSize, string kind) => new()
    {
        ["path"] = path,
        ["kind"] = kind,
        ["source_sha256"] = sourceHash,
        ["rebuilt_sha256"] = rebuiltHash,
        ["source_size_bytes"] = sourceSize,
        ["rebuilt_size_bytes"] = rebuiltSize
    };
}
