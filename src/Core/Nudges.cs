using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireSeer.Core;

/// <summary>Plain-language warnings about HP, focus, bloat and gaps (port of coach.hp_warning / coach.nudges).</summary>
public static class Nudges
{
    public static Nudge? HpWarning(Library lib, RunSnapshot run)
    {
        int hp = run.Hp, act = Math.Min(Math.Max(run.Act, 1), 3);
        if (hp <= 0) return null;
        var dmg = lib.RoomDamage;
        double elite = dmg.TryGetValue($"{act}|elite|all", out var e) ? e : 0;
        double boss = dmg.TryGetValue($"{act}|boss|all", out var b) ? b : 0;
        if (elite > 0 && hp <= elite * 1.2)
        {
            double win = dmg.TryGetValue($"{act}|elite|win", out var w) ? w : elite;
            return new Nudge("hp", "warn",
                $"HP {hp}: an act {act} elite costs about {elite:0} HP on average (winners {win:0}). Avoid elites; rest at the next campfire or use potions.");
        }
        if (boss > 0 && hp <= boss * 1.3)
            return new Nudge("hp", hp <= boss ? "warn" : "info",
                $"HP {hp}: the act {act} boss costs about {boss:0} HP on average. Heal before the boss.");
        return null;
    }

    public static IEnumerable<Archetype> ArchetypesWith(Library lib, string theme) =>
        lib.Archetypes.Where(a => a.Name.Split(" + ").Contains(theme));

    public static List<Nudge> For(Library lib, DeckProfile prof, RunSnapshot run, IReadOnlyList<Scored>? offers = null)
    {
        var outp = new List<Nudge>();
        int act = Math.Max(run.Act, 1);
        var hw = HpWarning(lib, run);
        if (hw != null) outp.Add(hw);
        var built = prof.Themes.Where(kv => kv.Value >= DeckProfile.Active).ToList();

        // 1. spread too thin
        if (built.Count >= 3 && act <= 2)
        {
            var lead = built.First(kv => kv.Value == built.Max(x => x.Value));
            string extra = "";
            if (lib.ThemesBuiltAtAct1End.TryGetValue("1", out var one) && lib.ThemesBuiltAtAct1End.TryGetValue("3", out var three))
                extra = $" Community runs building 3+ themes at the end of act 1 won {Fmt.Pct(three.WinRate)}, vs {Fmt.Pct(one.WinRate)} with one.";
            outp.Add(new Nudge("spread", "warn",
                $"You are building {built.Count} themes at once ({string.Join(", ", built.Select(kv => $"{kv.Key} {kv.Value}"))}). " +
                $"Pick a direction: {lead.Key} is furthest along.{extra}"));
        }

        // 2. deck bloat vs winners
        if (lib.DeckSizeMedian.TryGetValue($"act{act}", out var m) && m.Winners is double med && med > 0 && prof.Size > med + 3)
            outp.Add(new Nudge("bloat", "warn",
                $"Your deck has {prof.Size} cards; winners end act {act} with about {med:0}. Lean toward skipping filler."));

        // 3. missing roles (non-starter cards) once the deck has started to grow
        if (prof.Size >= 14)
            foreach (var role in new[] { "aoe", "draw", "scaling" })
                if (prof.RoleCount(role) < 1)
                    outp.Add(new Nudge("gap", "info",
                        $"No {(role == "aoe" ? "AOE" : role)} card yet (non-starter). Most decks that win need one by act {Math.Max(act, 2)}."));

        // 4. pivot / off-theme: a strong card for another theme
        if (offers != null)
        {
            var lead = prof.Lead;
            double? leadWr = lead != "none" ? ArchetypesWith(lib, lead).Select(a => (double?)a.WinRate).Max() : null;
            foreach (var o in offers)
            {
                if (o.Themes.Count == 0 || o.Lift == null) continue;
                if (o.Themes.Contains(lead)) continue;
                var other = o.Themes.Where(t => t != lead).ToList();
                if (other.Count == 0) continue;
                var th = other[0];
                double? thWr = ArchetypesWith(lib, th).Select(a => (double?)a.WinRate).Max();
                double lift = o.Lift.Value;
                bool thOk = thWr is double tw && tw != 0;
                bool leadOk = leadWr is double lw && lw != 0;
                if (lift >= 0.04 && thOk && (leadWr == null || thWr >= leadWr + 0.03))
                {
                    var stage = act == 1 ? "This early, a pivot is cheap." : "A pivot this late needs several more pieces - take it only if it also helps now.";
                    var text = leadOk
                        ? $"{o.Name} is a strong {th} card ({Fmt.Pct(lift, true)} win when picked). {th} decks win {Fmt.Pct(thWr!.Value)} from act 3 vs {Fmt.Pct(leadWr!.Value)} for {lead}. {stage}"
                        : $"{o.Name} is a strong {th} card ({Fmt.Pct(lift, true)}) and your deck has no direction yet - a good seed.";
                    outp.Add(new Nudge("pivot", "info", text));
                }
                else if (lead != "none" && lift >= 0.06 && thOk && leadOk && thWr < leadWr)
                {
                    outp.Add(new Nudge("offtheme", "info",
                        $"{o.Name} is strong on its own ({Fmt.Pct(lift, true)}) but belongs to {th}, not your {lead} plan. Take it only if it fixes a gap."));
                }
            }
        }
        return outp;
    }
}
