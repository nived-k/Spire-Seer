using System;
using System.Collections.Generic;
using System.Linq;

namespace SpireSeer.Core;

/// <summary>An item for sale. Kind: "card", "relic", "potion" or "removal".</summary>
public sealed record ShopOffer(string Kind, string Id, int Price, bool Upgraded = false);

public sealed class ShopOption
{
    public required ShopOffer Item { get; init; }
    public string Name { get; init; } = "";
    public double? Lift { get; init; }
    public int N { get; init; }
    public string Basis { get; init; } = "";
    public bool LowData => Lift != null && N < 100;
    public bool Affordable { get; set; }
}

public sealed class ShopPlan
{
    /// <summary>Indices into ShopAdvice.Options, in purchase-plan order.</summary>
    public List<int> Buy { get; init; } = new();
    public double TotalLift { get; init; }
    public int Cost { get; init; }
    public int Left { get; init; }
    public string Why { get; init; } = "";
}

public sealed class ShopAdvice
{
    public required IReadOnlyList<ShopOption> Options { get; init; }
    public required ShopPlan Plan { get; init; }
    public Stat? BuyNothing { get; init; }
    public required DeckProfile Profile { get; init; }
    public required IReadOnlyList<Nudge> Nudges { get; init; }
}

/// <summary>Shop advice: value of each item and the best set within your gold (port of coach_more.advise_shop / best_plan).</summary>
public static class ShopCoach
{
    public const double FitBonus = 0.005;

    /// <summary>Default removal price when the game's price is unknown.</summary>
    public static int RemovalPrice(int removalsUsed) => 75 + 25 * removalsUsed;

    private static Stat? ShopValue(Library lib, string kind, string id, int act, out string basis)
    {
        basis = "";
        if (!lib.Shop.Items.TryGetValue($"{kind}:{id}", out var it) || it.ByAct.Count == 0) return null;
        if (it.ByAct.TryGetValue(act.ToString(), out var v)) { basis = $"bought in act {act} shops"; return v; }
        basis = "bought in shops (other acts)";
        Stat? best = null;
        foreach (var x in it.ByAct.Values) if (best == null || x.N > best.N) best = x;
        return best;
    }

    public static ShopAdvice Advise(Library lib, CommonData common, RunSnapshot run, IReadOnlyList<ShopOffer> items, Func<string, string, string>? nameOf = null)
    {
        var prof = DeckProfile.Build(lib, common, run.Deck);
        int act = Math.Min(Math.Max(run.Act, 1), 3);
        int gold = run.Gold;
        var opts = new List<ShopOption>();
        foreach (var it in items)
        {
            string label = nameOf?.Invoke(it.Kind, it.Id) ?? it.Id;
            if (it.Kind == "removal")
            {
                lib.Shop.Removal.TryGetValue(act.ToString(), out var rv);
                opts.Add(new ShopOption { Item = it, Name = "Card Removal", Lift = rv?.Lift, N = rv?.N ?? 0, Basis = $"used removal in act {act}" });
                continue;
            }
            double? lift = null;
            int n = 0;
            string basis = "";
            if (it.Kind == "card")
            {
                // a card does the same job however you got it: use the card-reward value (bigger samples)
                var s = CardCoach.ScoreOffer(lib, prof, it.Id, it.Upgraded, act);
                if (s != null) { lift = s.Lift; n = s.N; basis = "card value: " + s.Basis; label = s.Name; }
            }
            if (lift == null)
            {
                var v = (it.Kind == "card" && it.Upgraded ? ShopValue(lib, it.Kind, it.Id + "+", act, out basis) : null)
                        ?? ShopValue(lib, it.Kind, it.Id, act, out basis);
                if (v != null) { lift = v.Lift; n = v.N; }
                else basis = "not enough shop data";
            }
            var themes = it.Kind == "card" && lib.CardTags.TryGetValue(it.Id, out var tag) ? tag.Themes : new List<string>();
            int fit = themes.Count > 0 ? Math.Min(prof.ThemeCount(themes[0]), 4) : 0;
            opts.Add(new ShopOption
            {
                Item = it,
                Name = label + (it.Kind == "card" && it.Upgraded ? "+" : ""),
                Lift = lift == null ? null : CardCoach.Round3(lift.Value + FitBonus * fit),
                N = n,
                Basis = basis,
            });
        }
        foreach (var o in opts) o.Affordable = o.Item.Price <= gold;
        lib.Shop.Nothing.TryGetValue(act.ToString(), out var nothing);
        var plan = BestPlan(opts, gold);
        return new ShopAdvice { Options = opts, Plan = plan, BuyNothing = nothing, Profile = prof, Nudges = Nudges.For(lib, prof, run) };
    }

    /// <summary>Highest total lift within budget; positive-lift items only, at most one removal, up to 4 items.
    /// Enumerates combinations in the same order as Python's itertools.combinations so ties resolve identically.</summary>
    public static ShopPlan BestPlan(IReadOnlyList<ShopOption> opts, int gold, int maxItems = 4)
    {
        var good = Enumerable.Range(0, opts.Count)
            .Where(i => opts[i].Lift != null && opts[i].Item.Price > 0 && opts[i].Lift > 0.003).ToList();
        List<int> bestSet = new();
        double bestTot = 0.0;
        int bestCost = 0;
        var cur = new List<int>();

        void Rec(int start, int k)
        {
            if (cur.Count == k)
            {
                int cost = cur.Sum(i => opts[i].Item.Price);
                if (cost > gold) return;
                if (cur.Count(i => opts[i].Item.Kind == "removal") > 1) return;
                double tot = 0;
                foreach (var i in cur) tot += opts[i].Lift!.Value;
                if (tot > bestTot + 1e-9) { bestSet = new List<int>(cur); bestTot = tot; bestCost = cost; }
                return;
            }
            for (int j = start; j < good.Count; j++)
            {
                cur.Add(good[j]);
                Rec(j + 1, k);
                cur.RemoveAt(cur.Count - 1);
            }
        }
        for (int k = 1; k <= Math.Min(maxItems, good.Count); k++) Rec(0, k);

        if (bestSet.Count == 0)
        {
            if (good.Count > 0 && good.All(i => opts[i].Item.Price > gold))
            {
                var top = good.OrderByDescending(i => opts[i].Lift).First();
                return new ShopPlan
                {
                    Cost = 0, Left = gold,
                    Why = $"You have {gold} gold - nothing worth buying is affordable (best: {opts[top].Name} {opts[top].Item.Price}g, {Fmt.Pct(opts[top].Lift!.Value, true)}). Save the gold.",
                };
            }
            return new ShopPlan { Cost = 0, Left = gold, Why = "Nothing on sale did better than an average shop visit for this act - save the gold." };
        }
        var names = string.Join(", ", bestSet.OrderByDescending(i => opts[i].Lift).Select(i => opts[i].Name));
        var why = $"Buy {names} for {bestCost} gold (keeps {gold - bestCost}).";
        int alt = -1;
        foreach (var i in good)
            if (!bestSet.Contains(i) && (alt < 0 || opts[i].Lift > opts[alt].Lift)) alt = i;
        if (alt >= 0 && opts[alt].Item.Price > gold - bestCost)
            why += $" {opts[alt].Name} ({Fmt.Pct(opts[alt].Lift!.Value, true)}) does not fit the budget alongside them.";
        return new ShopPlan { Buy = bestSet, TotalLift = CardCoach.Round3(bestTot), Cost = bestCost, Left = gold - bestCost, Why = why };
    }
}
