using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireSeer.Core;

/// <summary>One cell of community stats: runs in the slice and win-rate lift vs the average run at that point.</summary>
public sealed class Stat
{
    public int N { get; set; }
    public double? Lift { get; set; }
}

public sealed class CardEntry
{
    public string Name { get; set; } = "";
    public List<string> Themes { get; set; } = new();
    public List<string> Roles { get; set; } = new();
    public int Offered { get; set; }
    public double? PickRate { get; set; }
    public double? Lift { get; set; }
    public Dictionary<string, Stat> ByAct { get; set; } = new();
    public Dictionary<string, Stat> ByLead { get; set; } = new();
}

public sealed class CardTag
{
    public List<string> Themes { get; set; } = new();
    public List<string> Roles { get; set; } = new();
}

public sealed class ShopItem
{
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public double? BuyRate { get; set; }
    public Dictionary<string, Stat> ByAct { get; set; } = new();
}

public sealed class ShopData
{
    public Dictionary<string, ShopItem> Items { get; set; } = new();
    public Dictionary<string, Stat> Removal { get; set; } = new();
    public Dictionary<string, Stat> Nothing { get; set; } = new();
}

public sealed class RelicChoice
{
    public Dictionary<string, Stat> ByAct { get; set; } = new();
}

public sealed class ArchRow
{
    public string Id { get; set; } = "";
    public double Lift { get; set; }
}

public sealed class Archetype
{
    public string Name { get; set; } = "";
    public double WinRate { get; set; }
    public List<ArchRow> Core { get; set; } = new();
    public List<ArchRow> Support { get; set; } = new();
    public List<ArchRow> Traps { get; set; } = new();
}

public sealed class WinRateCell
{
    public double WinRate { get; set; }
}

public sealed class Median
{
    public double? Winners { get; set; }
}

/// <summary>Per-character community library (trimmed export of the Python analysis).</summary>
public sealed class Library
{
    public string Character { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, CardEntry> Cards { get; set; } = new();
    public Dictionary<string, CardTag> CardTags { get; set; } = new();
    public Dictionary<string, Stat> Skip { get; set; } = new();
    public ShopData Shop { get; set; } = new();
    public Dictionary<string, RelicChoice> RelicChoices { get; set; } = new();
    /// <summary>event id -> option id -> HP band ("low"/"mid"/"high"/"any") -> stat</summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, Stat>>> Events { get; set; } = new();
    public List<Archetype> Archetypes { get; set; } = new();
    public Dictionary<string, WinRateCell> ThemesBuiltAtAct1End { get; set; } = new();
    public Dictionary<string, double> RoomDamage { get; set; } = new();
    public Dictionary<string, Median> DeckSizeMedian { get; set; } = new();

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static Library Load(Stream s) =>
        JsonSerializer.Deserialize<Library>(s, Json) ?? throw new InvalidDataException("empty library");
}

public sealed class EventInfo
{
    public string Name { get; set; } = "";
    /// <summary>option id -> English title (fallback label; prefer the game's localized text).</summary>
    public Dictionary<string, string> Options { get; set; } = new();
    /// <summary>Option ids of the first page, in order.</summary>
    public List<string> Top { get; set; } = new();
}

/// <summary>Data shared by all characters.</summary>
public sealed class CommonData
{
    public string Version { get; set; } = "";
    public Dictionary<string, EventInfo> Events { get; set; } = new();
    /// <summary>Starter (Basic rarity) card ids; they never count toward a theme or role.</summary>
    public List<string> Basic { get; set; } = new();
    /// <summary>Community data versions shipped, oldest first (e.g. "v0.107.1", "v0.111.0").</summary>
    public List<string> Versions { get; set; } = new();

    public static CommonData Load(Stream s) =>
        JsonSerializer.Deserialize<CommonData>(s, Library.Json) ?? throw new InvalidDataException("empty common data");
}

/// <summary>Loads and caches libraries. <paramref name="open"/> returns a stream for "common" or
/// "&lt;character&gt;.&lt;version&gt;" (e.g. "necrobinder.v0.107.1"), or null when it does not exist.</summary>
public sealed class DataStore
{
    private readonly Func<string, Stream?> _open;
    private readonly Dictionary<string, Library?> _libs = new(StringComparer.OrdinalIgnoreCase);
    private CommonData? _common;
    private string? _version;

    public DataStore(Func<string, Stream?> open) => _open = open;

    /// <summary>The community data version in use; defaults to the newest shipped.</summary>
    public string Version
    {
        get => _version ??= Common.Versions.Count > 0 ? Common.Versions[^1] : "";
        set { _version = value; _libs.Clear(); }
    }

    /// <summary>Use the data that matches the running game: the newest version not newer than the game.</summary>
    public string SelectFor(string? gameVersion) => Version = PickVersion(Common.Versions, gameVersion) ?? "";

    /// <summary>Newest available version &lt;= game version. If the game is older than everything, the oldest;
    /// if the game version is unknown, the newest. Null when nothing is available.</summary>
    public static string? PickVersion(IReadOnlyList<string> available, string? gameVersion)
    {
        if (available.Count == 0) return null;
        var sorted = available.OrderBy(Parse, Comparer<int[]>.Create(Cmp)).ToList();
        if (gameVersion == null || Parse(gameVersion).Length == 0) return sorted[^1];
        var g = Parse(gameVersion);
        string? best = null;
        foreach (var v in sorted)
            if (Cmp(Parse(v), g) <= 0) best = v;
        return best ?? sorted[0];
    }

    private static int[] Parse(string v)
    {
        var core = v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        var nums = new List<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out var n)) return nums.Count > 0 ? nums.ToArray() : Array.Empty<int>();
            nums.Add(n);
        }
        return nums.ToArray();
    }

    private static int Cmp(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    public CommonData Common
    {
        get
        {
            if (_common != null) return _common;
            using var s = _open("common") ?? throw new FileNotFoundException("common data missing");
            return _common = CommonData.Load(s);
        }
    }

    /// <summary>The library for a character (e.g. "necrobinder"), or null when there is no data for it yet.</summary>
    public Library? For(string character)
    {
        var key = $"{character.ToLowerInvariant()}.{Version}";
        if (_libs.TryGetValue(key, out var lib)) return lib;
        using var s = _open(key);
        lib = s == null ? null : Library.Load(s);
        _libs[key] = lib;
        return lib;
    }
}
