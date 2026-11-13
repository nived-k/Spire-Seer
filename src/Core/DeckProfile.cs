using System.Collections.Generic;
using System.Linq;

namespace SpireSeer.Core;

/// <summary>Theme counts, archetype guess and role counts for a deck (port of coach.deck_profile / analysis.Tagger).</summary>
public sealed class DeckProfile
{
    public const int MinTheme = 3;        // cards of a theme needed to call a deck "<theme>"
    public const double SecondRatio = 0.6; // a second theme this close to the first makes "A + B"
    public const int Active = 2;          // a theme with this many cards counts as "being built"

    /// <summary>Theme -> count, most common first (ties keep first-seen order, like Python's Counter.most_common).</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Themes { get; }
    public string Archetype { get; }
    public string Lead { get; }
    public IReadOnlyDictionary<string, int> Roles { get; }
    public int Size { get; }

    private DeckProfile(List<KeyValuePair<string, int>> themes, string archetype, string lead, Dictionary<string, int> roles, int size)
    {
        Themes = themes;
        Archetype = archetype;
        Lead = lead;
        Roles = roles;
        Size = size;
    }

    public int ThemeCount(string theme)
    {
        foreach (var kv in Themes)
            if (kv.Key == theme) return kv.Value;
        return 0;
    }

    public int RoleCount(string role) => Roles.TryGetValue(role, out var n) ? n : 0;

    public static DeckProfile Build(Library lib, CommonData common, IReadOnlyList<string> deck)
    {
        var basic = new HashSet<string>(common.Basic);
        var counts = new Dictionary<string, int>(); // insertion ordered
        var roles = new Dictionary<string, int>();
        foreach (var id in deck)
        {
            if (basic.Contains(id)) continue;
            if (!lib.CardTags.TryGetValue(id, out var tag)) continue;
            foreach (var t in tag.Themes) counts[t] = counts.TryGetValue(t, out var n) ? n + 1 : 1;
            foreach (var r in tag.Roles) roles[r] = roles.TryGetValue(r, out var n) ? n + 1 : 1;
        }
        // OrderByDescending is stable, so ties keep insertion order - same as Counter.most_common
        var ordered = counts.OrderByDescending(kv => kv.Value).ToList();

        string archetype = "No clear theme";
        if (ordered.Count > 0 && ordered[0].Value >= MinTheme)
        {
            var (a, na) = (ordered[0].Key, ordered[0].Value);
            archetype = a;
            if (ordered.Count > 1)
            {
                var (b, nb) = (ordered[1].Key, ordered[1].Value);
                if (nb >= MinTheme && nb >= SecondRatio * na)
                    archetype = string.Join(" + ", new[] { a, b }.OrderBy(x => x, System.StringComparer.Ordinal));
            }
        }
        string lead = ordered.Count > 0 && ordered[0].Value >= Active ? ordered[0].Key : "none";
        return new DeckProfile(ordered, archetype, lead, roles, deck.Count);
    }
}
