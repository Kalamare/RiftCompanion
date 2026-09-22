using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rift.Core;

namespace Rift.Infrastructure;

public sealed class SeasonHistoryStore(string directory)
{
    private string FilePath(string platform, string puuid, DateTimeOffset from) => Path.Combine(directory, "season-history-v1",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{platform}:{puuid}:{from.ToUnixTimeSeconds()}"))) + ".json");
    public SeasonHistory? Read(string platform, string puuid, DateTimeOffset from)
    {
        var path = FilePath(platform, puuid, from);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 20_000_000) return null;
            var value = JsonSerializer.Deserialize<SeasonHistory>(File.ReadAllText(path));
            return value is not null && value.Platform == platform && value.Puuid == puuid && value.From == from &&
                value.Matches is not null && value.NextStart is >= 0 and <= 10000 && value.Until > from ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public void Save(SeasonHistory value)
    {
        var path = FilePath(value.Platform, value.Puuid, value.From);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (var old in new DirectoryInfo(Path.GetDirectoryName(path)!).GetFiles("*.json").Where(f => f.FullName != path)
                     .OrderByDescending(f => f.LastWriteTimeUtc).Skip(49)) old.Delete();
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }
}
