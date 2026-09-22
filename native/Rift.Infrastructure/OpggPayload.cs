using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rift.Core;

namespace Rift.Infrastructure;

// Decode OP.GG's compact class/value wire format as data, never executable code.
public static class OpggPayload
{
    public static OpggProfile Parse(string text, string riotId, string platform, DateTimeOffset fetched)
    {
        if (text.Length > 1_000_000) throw new JsonException("OP.GG payload too large");
        var root = text.TrimStart().StartsWith('{') ? JsonNode.Parse(text) : new Decoder(text).Read();
        var s = root?["data"]?["summoner"] ?? throw new JsonException("OP.GG profile missing");
        string Str(JsonNode? n) => n is null ? "" : n.GetValue<string>();
        int? Int(JsonNode? n) => n is null ? null : n.GetValue<int>();
        string returnedId = Str(s["game_name"]) + "#" + Str(s["tagline"]);
        if (!returnedId.Equals(riotId, StringComparison.OrdinalIgnoreCase)) throw new JsonException("OP.GG identity mismatch");
        if (s["region"] is { } region && !Str(region).Equals(OpggClient.Region(platform), StringComparison.OrdinalIgnoreCase)) throw new JsonException("OP.GG region mismatch");
        int? rank = Int(s["ladder_rank"]?["rank"]), total = Int(s["ladder_rank"]?["total"]);
        if (rank is <= 0 || total is <= 0 || rank > total || rank is null || total is null) { rank = null; total = null; }
        var most = s["most_champions"];
        var champions = new List<OpggChampion>();
        if (most?["champion_stats"] is JsonArray rows)
            foreach (var row in rows.Take(300))
            {
                int id = Int(row?["id"]) ?? 0, play = Int(row?["play"]) ?? 0, win = Int(row?["win"]) ?? -1, lose = Int(row?["lose"]) ?? -1;
                long? Count(string key) => row?[key]?.GetValue<long>();
                var kills = Count("kill"); var deaths = Count("death"); var assists = Count("assist");
                if (id <= 0 || play <= 0 || win < 0 || lose < 0 || (long)win + lose != play || kills is null or < 0 || deaths is null or < 0 || assists is null or < 0) continue;
                var laneCs = Count("minion_kill"); var neutralCs = Count("neutral_minion_kill"); var seconds = Count("game_length_second");
                champions.Add(new(id, Str(row?["champion_name"]), play, win, kills.Value, deaths.Value, assists.Value)
                { Cs = laneCs is >= 0 && neutralCs is >= 0 ? checked(laneCs + neutralCs) : null, Seconds = seconds is > 0 ? seconds : null });
            }
        DateTimeOffset? updated = DateTimeOffset.TryParse(Str(s["updated_at"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        var games = Int(most?["play"]); var wins = Int(most?["win"]); var losses = Int(most?["lose"]);
        bool validTotal = games is >= 0 && wins is >= 0 && losses is >= 0 && (long)wins + losses == games;
        return new(returnedId, platform, fetched, updated, rank, total, Str(most?["game_type"]), Int(most?["season_id"]), champions.ToArray())
        { SeasonGames = validTotal ? games : null, SeasonWins = validTotal ? wins : null, SeasonLosses = validTotal ? losses : null };
    }
    private sealed class Decoder(string source)
    {
        private readonly Dictionary<string, string[]> types = [];
        private string value = "";
        private int offset, nodes;
        public JsonNode? Read()
        {
            var lines = source.Replace("\r", "").Split('\n'); int i = 0;
            for (; i < lines.Length && lines[i].StartsWith("class "); i++)
            {
                var match = Regex.Match(lines[i], @"^class ([A-Za-z][A-Za-z0-9_]*): ([A-Za-z0-9_, ]+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (!match.Success || types.Count >= 100) throw new JsonException("Invalid OP.GG schema");
                var fields = match.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries);
                if (fields.Distinct().Count() != fields.Length || !types.TryAdd(match.Groups[1].Value, fields)) throw new JsonException("Duplicate OP.GG schema");
            }
            value = string.Join('\n', lines.Skip(i)).Trim();
            var result = Node(0); Space(); if (offset != value.Length) throw new JsonException("OP.GG trailing data"); return result;
        }
        private void Space() { while (offset < value.Length && char.IsWhiteSpace(value[offset])) offset++; }
        private void Expect(char c) { Space(); if (offset >= value.Length || value[offset++] != c) throw new JsonException("Invalid OP.GG value"); }
        private JsonNode? Node(int depth)
        {
            if (depth > 32 || ++nodes > 20000) throw new JsonException("OP.GG nesting limit");
            Space(); if (offset >= value.Length) throw new JsonException("Missing OP.GG value");
            if (value[offset] == '[')
            {
                offset++; var array = new JsonArray(); Space();
                if (offset < value.Length && value[offset] != ']') { while (true) { array.Add(Node(depth + 1)); Space(); if (offset >= value.Length || value[offset] != ',') break; offset++; } }
                Expect(']'); return array;
            }
            int start = offset;
            if (value[offset] == '"')
            {
                offset++; bool escaped = false;
                while (offset < value.Length) { char c = value[offset++]; if (!escaped && c == '"') return JsonNode.Parse(value[start..offset]); if (!escaped && c == '\\') escaped = true; else escaped = false; }
                throw new JsonException("Unterminated string");
            }
            while (offset < value.Length && (char.IsLetterOrDigit(value[offset]) || value[offset] is '_' or '-' or '+' or '.')) offset++;
            string token = value[start..offset];
            if (types.TryGetValue(token, out var fields))
            {
                Expect('('); var obj = new JsonObject();
                for (int j = 0; j < fields.Length; j++) { if (j > 0) Expect(','); obj[fields[j]] = Node(depth + 1); }
                Expect(')'); return obj;
            }
            if (token == "null") return null;
            if (token.Length == 0 || char.IsLetter(token[0])) throw new JsonException("Unknown OP.GG token");
            return JsonNode.Parse(token);
        }
    }
}
