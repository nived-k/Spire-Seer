using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireSeer.Core;

/// <summary>A scored option (card, SKIP, shop item, relic or event option).</summary>
public sealed class Scored
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double? Lift { get; set; }
    public int N { get; init; }
    public string Basis { get; set; } = "";
    public IReadOnlyList<string> Themes { get; init; } = Array.Empty<string>();
    public int Fit { get; set; }
    public string? Arch { get; set; }
}

public sealed class CardAdvice
{
    public required DeckProfile Profile { get; init; }
    /// <summary>One entry per offer (same order), then SKIP last.</summary>
    public required IReadOnlyList<Scored> Options { get; init; }
    /// <summary>Index into Options of the recommended pick (the last index means SKIP).</summary>
    public int Pick { get; init; }
    public string? CloseCall { get; init; }
    public required IReadOnlyList<Nudge> Nudges { get; init; }
}

/// <summary>Card reward scoring from community data (port of coach.py). No LLM.</summary>
public static class CardCoach
{
    public const double Close = 0.02; // differences in lift smaller than this are noise
    public const int Commit = 4;      // theme cards that make the deck "committed" to an archetype

    internal static string SliceLabel(int act, string lead) =>
        lead != "none" ? $"act {act}, deck leaning {lead}" : $"act {act}, no theme yet";

    /// <summary>Win rate after taking this card minus the average reward screen, in the most specific slice with data.</summary>
    public static Scored? ScoreOffer(Library lib, DeckProfile prof, string id, bool upgraded, int act)
    {
        CardEntry? c = null;
        if (upgraded) lib.Cards.TryGetValue(id + "+", out c);
        if (c == null && !lib.Cards.TryGetValue(id, out c)) return null;
        act = Math.Min(act, 3);
        var lead = prof.Lead;
        Stat v;
        string basis;
        if (c.ByLead.TryGetValue($"{act}:{lead}", out var bl)) { v = bl; basis = SliceLabel(act, lead); }
        else if (c.ByAct.TryGetValue(act.ToString(), out var ba)) { v = ba; basis = $"act {act}"; }
        else if (c.Lift != null) { v = new Stat { Lift = c.Lift, N = c.Offered }; basis = "all acts"; }
        else return null;
        return new Scored { Id = id, Name = c.Name, Lift = v.Lift, N = v.N, Basis = basis, Themes = c.Themes };
    }

    /// <summary>How a card does inside the archetype you are committed to: (lift, archetype) or (null, ...).</summary>
    public static (double? Lift, string? Arch) ArchetypeFit(Library lib, DeckProfile prof, string id)
    {
        var lead = prof.Lead;
        if (lead == "none" || prof.ThemeCount(lead) < Commit) return (null, null);
        var arch = lib.Archetypes.FirstOrDefault(a => a.Name == prof.Archetype) ?? lib.Archetypes.FirstOrDefault(a => a.Name == lead);
        if (arch == null) return (null, null);
        foreach (var r in arch.Core.Concat(arch.Support).Concat(arch.Traps))
            if (r.Id == id) return (r.Lift, arch.Name);
        return (null, arch.Name);
    }

    public static Scored ScoreSkip(Library lib, DeckProfile prof, int act)
    {
        act = Math.Min(act, 3);
        foreach (var (k, label) in new[] { ($"{act}:{prof.Lead}", SliceLabel(act, prof.Lead)), (act.ToString(), $"act {act}") })
            if (lib.Skip.TryGetValue(k, out var v))
                return new Scored { Id = "SKIP", Name = "SKIP", Lift = v.Lift, N = v.N, Basis = label };
        return new Scored { Id = "SKIP", Name = "SKIP", Lift = 0.0, N = 0, Basis = "no data" };
    }

    internal static double Round3(double x) => Math.Round(x, 3, MidpointRounding.ToEven);

    public static CardAdvice Advise(Library lib, CommonData common, RunSnapshot run, IReadOnlyList<Offer> offers)
    {
        var prof = DeckProfile.Build(lib, common, run.Deck);
        int act = Math.Max(run.Act, 1);
        var opts = new List<Scored>();
        foreach (var o in offers)
        {
            var s = ScoreOffer(lib, prof, o.Id, o.Upgraded, act);
            if (s != null)
            {
                s.Fit = s.Themes.Count > 0 ? prof.ThemeCount(s.Themes[0]) : 0;
                var (al, an) = ArchetypeFit(lib, prof, o.Id);
                if (al != null)
                {
                    // committed: weigh how the card does in this archetype's decks as heavily as the reward-screen value
                    s.Arch = an;
                    s.Lift = Round3(0.5 * s.Lift!.Value + 0.5 * al.Value);
                    s.Basis += $"; in {an} decks {Fmt.Pct(al.Value, signed: true)}";
                }
                opts.Add(s);
            }
            else
            {
                opts.Add(new Scored { Id = o.Id, Name = o.Id + (o.Upgraded ? "+" : ""), Lift = null, Basis = "not enough community data" });
            }
        }
        var skip = ScoreSkip(lib, prof, act);
        static double FitScore(Scored o) => o.Lift!.Value + 0.005 * Math.Min(o.Fit, 4);
        int best = -1;
        for (int i = 0; i < opts.Count; i++)
            if (opts[i].Lift != null && (best < 0 || FitScore(opts[i]) > FitScore(opts[best]))) best = i;
        double skipLift = skip.Lift ?? 0.0;
        int pick = best >= 0 && FitScore(opts[best]) > skipLift ? best : opts.Count;
        string? close = null;
        if (best >= 0 && Math.Abs(FitScore(opts[best]) - skipLift) < Close)
            close = $"Close call: {opts[best].Name} and SKIP are within {Close:P0} - either is fine; follow your build plan.";
        var scored = opts.Where(o => o.Lift != null).ToList();
        var all = new List<Scored>(opts) { skip };
        return new CardAdvice { Profile = prof, Options = all, Pick = pick, CloseCall = close, Nudges = Nudges.For(lib, prof, run, scored) };
    }
}

internal static class Fmt
{
    /// <summary>Python-style percent with no decimals: 0.034 -> "3%", signed -> "+3%".</summary>
    public static string Pct(double x, bool signed = false)
    {
        var r = Math.Round(x * 100, MidpointRounding.ToEven);
        var s = r.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        return signed && r >= 0 ? "+" + s : s;
    }
}
