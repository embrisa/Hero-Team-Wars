using System.Security.Cryptography;

namespace Wc3MapEngine.Core;

public static class Hashing
{
    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static async Task<(long Size, DateTime LastWriteUtc, string Sha256)> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new EngineException("FILE_NOT_FOUND", $"File does not exist: {path}");
        }

        var info = new FileInfo(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (info.Length, info.LastWriteTimeUtc, Convert.ToHexString(hash));
    }
}
