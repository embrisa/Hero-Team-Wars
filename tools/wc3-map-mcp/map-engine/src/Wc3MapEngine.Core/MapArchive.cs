using System.Collections.ObjectModel;
using War3Net.IO.Mpq;

namespace Wc3MapEngine.Core;

public sealed record ArchiveMemberData(
    string Path,
    long CompressedSize,
    long Size,
    string Sha256,
    bool Named,
    byte[] Bytes,
    MpqFileFlags Flags);

public sealed class MapArchiveSnapshot
{
    public MapArchiveSnapshot(string sourcePath, IReadOnlyList<ArchiveMemberData> members)
    {
        SourcePath = sourcePath;
        Members = new ReadOnlyCollection<ArchiveMemberData>(members.ToList());
    }

    public string SourcePath { get; }
    public IReadOnlyList<ArchiveMemberData> Members { get; }

    public ArchiveMemberData? Find(string path) => Members.FirstOrDefault(x =>
        string.Equals(NormalizePath(x.Path), NormalizePath(path), StringComparison.OrdinalIgnoreCase));

    public static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
}

public static class MapArchive
{
    public static MapArchiveSnapshot Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new EngineException("FILE_NOT_FOUND", $"Map does not exist: {path}");
        }

        try
        {
            using var archive = MpqArchive.Open(path, loadListFile: true);
            var members = new List<ArchiveMemberData>();
            var opaqueIndex = 0;
            foreach (var entry in archive)
            {
                if ((entry.Flags & MpqFileFlags.Garbage) != 0 || entry.Flags == 0)
                {
                    continue;
                }

                using var stream = archive.OpenFile(entry);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                var name = entry.FileName;
                var named = !string.IsNullOrWhiteSpace(name);
                var pathLabel = named
                    ? MapArchiveSnapshot.NormalizePath(name!)
                    : $"(opaque-{opaqueIndex++:D4}@{entry.FilePosition:X8})";
                members.Add(new ArchiveMemberData(
                    pathLabel,
                    entry.CompressedSize,
                    entry.FileSize,
                    Hashing.Sha256(bytes),
                    named,
                    bytes,
                    entry.Flags));
            }

            var duplicates = members.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                throw new EngineException("DUPLICATE_ARCHIVE_MEMBER", $"The archive contains duplicate named members: {string.Join(", ", duplicates)}");
            }

            return new MapArchiveSnapshot(path, members
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .ToList());
        }
        catch (EngineException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EngineException("PARSE_FAILED", $"Unable to read MPQ map archive: {path}", false, exception);
        }
    }

    public static void Rebuild(string sourcePath, string outputPath, IReadOnlyDictionary<string, byte[]> replacements)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException("BUILD_FAILED", "A map archive cannot be rebuilt over its source path.");
        }

        if (File.Exists(outputPath))
        {
            throw new EngineException("OUTPUT_EXISTS", $"Refusing to overwrite an existing build: {outputPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new EngineException("INVALID_ARGUMENT", "The build output must have a parent directory."));
        try
        {
            using var archive = MpqArchive.Open(sourcePath, loadListFile: true);
            var sourceFiles = archive.GetMpqFiles().ToList();
            var sourceNames = sourceFiles.OfType<MpqKnownFile>()
                .Select(file => MapArchiveSnapshot.NormalizePath(file.FileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var replacement in replacements.Keys)
            {
                if (!sourceNames.Contains(MapArchiveSnapshot.NormalizePath(replacement)))
                {
                    throw new EngineException("BUILD_FAILED", $"The build plan names archive member '{replacement}', but it is not present in the source archive.");
                }
            }

            var outputFiles = new List<MpqFile>(sourceFiles.Count);
            foreach (var file in sourceFiles)
            {
                if (file is MpqKnownFile known && replacements.TryGetValue(MapArchiveSnapshot.NormalizePath(known.FileName), out var bytes))
                {
                    file.Dispose();
                    outputFiles.Add(MpqFile.New(new MemoryStream(bytes, writable: false), known.FileName));
                }
                else
                {
                    outputFiles.Add(file);
                }
            }

            var options = new MpqArchiveCreateOptions
            {
                BlockSize = MpqArchiveCreateOptions.DefaultBlockSize,
                WriteArchiveFirst = true,
                ListFileCreateMode = MpqFileCreateMode.None,
                AttributesCreateMode = MpqFileCreateMode.None,
                SignatureCreateMode = MpqFileCreateMode.None
            };
            using var rebuilt = MpqArchive.Create(outputPath, outputFiles, options);
        }
        catch (EngineException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(outputPath);
            throw new EngineException("BUILD_FAILED", $"Unable to rebuild MPQ map: {outputPath}", false, exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original build error. The caller can recover the partial path from its log.
        }
    }
}
