using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireSeer.Core;

public sealed class ChoiceAdvice
{
    public required IReadOnlyList<Scored> Options { get; init; }
    /// <summary>Index of the best option, or -1 when nothing has data.</summary>
    public int Pick { get; init; }
    public string HpBand { get; init; } = "any";
}

/// <summary>Relic choices (Ancient / boss / elite) and events (port of coach_more.advise_relics / advise_event).</summary>
public static class ChoiceCoach
{
    private static int BestIndex(IReadOnlyList<Scored> opts)
    {
        int best = -1;
        for (int i = 0; i < opts.Count; i++)
            if (opts[i].Lift != null && (best < 0 || opts[i].Lift > opts[best].Lift)) best = i;
        return best;
    }

    /// <summary>source: "ancient", "boss" or "elite" - a guess; whichever choice data exists is used, preferring it.</summary>
    public static ChoiceAdvice Relics(Library lib, RunSnapshot run, IReadOnlyList<string> relicIds, string source = "ancient", Func<string, string>? nameOf = null)
    {
        int act = Math.Min(Math.Max(run.Act, 1), 3);
        var srcs = new[] { source }.Concat(new[] { "ancient", "boss", "elite" }.Where(x => x != source)).ToList();
        var opts = new List<Scored>();
        foreach (var id in relicIds)
        {
            RelicChoice? d = null;
            string? used = null;
            foreach (var x in srcs)
                if (lib.RelicChoices.TryGetValue($"{x}:{id}", out d)) { used = x; break; }
            Stat? v = null;
            if (d != null && d.ByAct.Count > 0)
            {
                // Ancients greet you at the start of an act; data is keyed by the act being entered
                if (!d.ByAct.TryGetValue(act.ToString(), out v) && !d.ByAct.TryGetValue(Math.Min(act + 1, 3).ToString(), out v))
                    foreach (var x in d.ByAct.Values) if (v == null || x.N > v.N) v = x;
            }
            opts.Add(new Scored
            {
                Id = id, Name = nameOf?.Invoke(id) ?? id, Lift = v?.Lift, N = v?.N ?? 0,
                Basis = v != null ? $"{used} choice (n={v.N:N0})" : "not enough data",
            });
        }
        return new ChoiceAdvice { Options = opts, Pick = BestIndex(opts) };
    }

    public static string HpBand(int hp, int maxHp)
    {
        if (hp <= 0 || maxHp <= 0) return "any";
        double r = (double)hp / maxHp;
        return r < 0.4 ? "low" : r < 0.7 ? "mid" : "high";
    }

    /// <summary>Rank an event's options. optionIds: the options on screen; defaults to the event's first page.
    /// Returns null for an unknown event.</summary>
    public static ChoiceAdvice? Event(Library lib, CommonData common, RunSnapshot run, string eventId,
        IReadOnlyList<string>? optionIds = null, Func<string, string?>? titleOf = null)
    {
        if (!common.Events.TryGetValue(eventId, out var ev)) return null;
        lib.Events.TryGetValue(eventId, out var stats);
        var band = HpBand(run.Hp, run.MaxHp);
        var opts = new List<Scored>();
        foreach (var oid in optionIds ?? ev.Top)
        {
            Dictionary<string, Stat>? s = null;
            stats?.TryGetValue(oid ?? "", out s);
            Stat? v = null;
            bool atBand = s != null && s.TryGetValue(band, out v);
            if (!atBand && s != null) s.TryGetValue("any", out v);
            var title = titleOf?.Invoke(oid ?? "") ?? (ev.Options.TryGetValue(oid ?? "", out var t) ? t : oid ?? "?");
            opts.Add(new Scored
            {
                Id = oid ?? "", Name = title, Lift = v?.Lift, N = v?.N ?? 0,
                Basis = v != null ? (atBand ? $"chosen at {band} HP" : "chosen at any HP") : "not enough data",
            });
        }
        return new ChoiceAdvice { Options = opts, Pick = BestIndex(opts), HpBand = band };
    }
}
