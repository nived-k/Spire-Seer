using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using SpireSeer.Core;
using SpireSeer.UI;

namespace SpireSeer.Game;

/// <summary>Glue between game hooks, the coach (Core) and the overlay: builds the panel text for each screen.</summary>
internal static class Advisor
{
    private static readonly DataStore Store = new(name =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"SpireSeer.data.{name}.json"));

    private const string Green = "#7fd67f", Red = "#e07a6a", Grey = "#9aa5a8", Gold = "#e8c25a", Warn = "#f0b45a";

    public static void Safe(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error($"[SpireSeer] {what}: {e}"); }
    }

    // ---- formatting ---------------------------------------------------------------------------------------------

    private static string Esc(string s) => s.Replace("[", "[lb]");

    private static string Lift(double? x, int n = 100)
    {
        if (x == null) return $"[color={Grey}]no data[/color]";
        var pct = (x.Value * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
        var col = x.Value > 0.005 ? Green : x.Value < -0.005 ? Red : Grey;
        var low = n < 100 ? $"[color={Grey}]?[/color]" : "";
        return $"[color={col}]{pct}[/color]{low}";
    }

    private static string Header(RunSnapshot run, DeckProfile? prof)
    {
        var sb = new StringBuilder();
        sb.Append($"[b]{Esc(Title(run.Character))}[/b]  Act {run.Act} · Floor {run.Floor} · HP {run.Hp}/{run.MaxHp} · [color={Gold}]{run.Gold}g[/color]\n");
        if (prof != null)
        {
            var themes = string.Join(", ", prof.Themes.Take(3).Select(kv => $"{kv.Key} {kv.Value}"));
            sb.Append($"Deck {prof.Size} · {Esc(prof.Archetype)}");
            if (themes.Length > 0) sb.Append($" [color={Grey}]({Esc(themes)})[/color]");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static void AppendNudges(StringBuilder sb, IEnumerable<Nudge> nudges, int max = 3)
    {
        foreach (var n in nudges.Take(max))
            sb.Append(n.Level == "warn" ? $"[color={Warn}]! {Esc(n.Text)}[/color]\n" : $"[color={Grey}]- {Esc(n.Text)}[/color]\n");
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Row(bool pick, string name, string lift, string extra = "") =>
        (pick ? $"[color={Gold}]>[/color] [b]{Esc(name)}[/b]" : $"   {Esc(name)}") + $"  {lift}" + (extra.Length > 0 ? $"  [color={Grey}]{Esc(extra)}[/color]" : "") + "\n";

    private static bool _versionPicked;

    /// <summary>Use the community data that matches the running game (main branch vs beta differ in balance).</summary>
    private static void PickDataVersion()
    {
        if (_versionPicked) return;
        _versionPicked = true;
        string? game = null;
        try { game = ReleaseInfoManager.Instance.SemVer?.ToString(); }
        catch (Exception e) { Log.Warn($"[SpireSeer] game version unknown: {e.Message}"); }
        var v = Store.SelectFor(game);
        Log.Info($"[SpireSeer] game {game ?? "?"} -> community data {v} (shipped: {string.Join(", ", Store.Common.Versions)})");
    }

    private static bool Ready(out RunSnapshot run, out Library lib)
    {
        PickDataVersion();
        run = RunReader.Snapshot()!;
        lib = null!;
        if (run == null) { Overlay.Show("", "Waiting for a run..."); return false; }
        var l = Store.For(run.Character);
        if (l == null)
        {
            Overlay.Show(Header(run, null), $"[color={Grey}]No community data for {Esc(Title(run.Character))} yet - coming in a later update.[/color]");
            return false;
        }
        lib = l;
        return true;
    }

    private static string Footer(Library lib) => $"[color={Grey}]community data {lib.Version} · F9 hide[/color]";

    // ---- screens --------------------------------------------------------------------------------------------------

    private static readonly HashSet<string> Logged = new();

    /// <summary>Logs the first time each screen kind is shown (keeps the log short but shows the hooks work).</summary>
    private static void Trace(string kind)
    {
        if (Logged.Add(kind)) Log.Info($"[SpireSeer] first {kind} panel");
    }

    public static void ShowMenu()
    {
        Trace("menu");
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Overlay.Show($"[b]Spire Seer[/b] [color={Grey}]v{v?.ToString(3)}[/color]",
            $"Ready - start or continue a run.\n[color={Grey}]F9 hide/show · Shift+F9 text size · drag to move[/color]");
    }

    public static void ShowDeck()
    {
        Trace("deck");
        if (!Ready(out var run, out var lib)) return;
        var prof = DeckProfile.Build(lib, Store.Common, run.Deck);
        var sb = new StringBuilder();
        AppendNudges(sb, Nudges.For(lib, prof, run), 4);
        sb.Append(Footer(lib));
        Overlay.Show(Header(run, prof), sb.ToString());
    }

    public static void CardReward(IReadOnlyList<CardModel> cards)
    {
        Trace("card reward");
        if (!Ready(out var run, out var lib)) return;
        var offers = cards.Select(c => new Offer(c.Id.Entry, c.IsUpgraded)).ToList();
        var a = CardCoach.Advise(lib, Store.Common, run, offers);
        var sb = new StringBuilder("[b]Card reward[/b]\n");
        for (int i = 0; i < a.Options.Count; i++)
        {
            var o = a.Options[i];
            string name = i < cards.Count ? cards[i].Title + (cards[i].IsUpgraded ? "+" : "") : "Skip";
            string extra = o.Themes.Count > 0 ? string.Join("/", o.Themes) : "";
            if (o.Arch != null) extra += (extra.Length > 0 ? " · " : "") + $"in {o.Arch}";
            sb.Append(Row(i == a.Pick, name, Lift(o.Lift, o.N), extra));
        }
        if (a.CloseCall != null) sb.Append($"[color={Grey}]Close call - either is fine; follow your build plan.[/color]\n");
        else if (a.Pick == a.Options.Count - 1) sb.Append($"[color={Grey}]Skipping did better than any card offered.[/color]\n");
        AppendNudges(sb, a.Nudges);
        sb.Append(Footer(lib));
        Overlay.Show(Header(run, a.Profile), sb.ToString());
    }

    public static void RelicChoice(IReadOnlyList<RelicModel> relics, string source, string title = "Relic choice")
    {
        Trace("relic");
        if (!Ready(out var run, out var lib)) return;
        var names = relics.ToDictionary(r => r.Id.Entry, r => r.Title.GetFormattedText());
        var a = ChoiceCoach.Relics(lib, run, relics.Select(r => r.Id.Entry).ToList(), source, id => names[id]);
        var sb = new StringBuilder($"[b]{title}[/b]\n");
        for (int i = 0; i < a.Options.Count; i++)
            sb.Append(Row(i == a.Pick, a.Options[i].Name, Lift(a.Options[i].Lift, a.Options[i].N)));
        sb.Append(Footer(lib));
        Overlay.Show(Header(run, DeckProfile.Build(lib, Store.Common, run.Deck)), sb.ToString());
    }

    public static void Event(EventModel ev)
    {
        Trace("event");
        var options = ev.CurrentOptions;
        if (ev is AncientEventModel)
        {
            var relics = options.Where(o => o.Relic != null).Select(o => o.Relic!).ToList();
            if (relics.Count > 0) RelicChoice(relics, "ancient", ev.Title.GetFormattedText());
            return;
        }
        if (!Ready(out var run, out var lib)) return;
        // option ids are the segment after ".options." in the text key, e.g. ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE
        var ids = new List<string>();
        var titles = new Dictionary<string, string>();
        foreach (var o in options)
        {
            var key = o.TextKey ?? "";
            int k = key.LastIndexOf(".options.", StringComparison.Ordinal);
            var id = k >= 0 ? key[(k + 9)..] : key;
            ids.Add(id);
            titles[id] = o.Title?.GetFormattedText() ?? id;
        }
        var a = ChoiceCoach.Event(lib, Store.Common, run, ev.Id.Entry, ids, id => titles.GetValueOrDefault(id));
        if (a == null || a.Options.All(o => o.Lift == null)) { ShowDeck(); return; }
        var sb = new StringBuilder($"[b]{Esc(ev.Title.GetFormattedText())}[/b] [color={Grey}](at {a.HpBand} HP)[/color]\n");
        for (int i = 0; i < a.Options.Count; i++)
            sb.Append(Row(i == a.Pick, a.Options[i].Name, Lift(a.Options[i].Lift, a.Options[i].N)));
        sb.Append(Footer(lib));
        Overlay.Show(Header(run, DeckProfile.Build(lib, Store.Common, run.Deck)), sb.ToString());
    }

    public static void Shop(MerchantInventory? inv)
    {
        Trace("shop");
        if (inv == null || !Ready(out var run, out var lib)) return;
        var items = new List<ShopOffer>();
        var names = new Dictionary<(string, string), string>();
        foreach (var e in inv.CharacterCardEntries.Concat(inv.ColorlessCardEntries))
            if (e.IsStocked && e.CreationResult != null)
            {
                var c = e.CreationResult.Card;
                items.Add(new ShopOffer("card", c.Id.Entry, e.Cost, c.IsUpgraded));
                names[("card", c.Id.Entry)] = c.Title;
            }
        foreach (var e in inv.RelicEntries)
            if (e.IsStocked && e.Model != null)
            {
                items.Add(new ShopOffer("relic", e.Model.Id.Entry, e.Cost));
                names[("relic", e.Model.Id.Entry)] = e.Model.Title.GetFormattedText();
            }
        foreach (var e in inv.PotionEntries)
            if (e.IsStocked && e.Model != null)
            {
                items.Add(new ShopOffer("potion", e.Model.Id.Entry, e.Cost));
                names[("potion", e.Model.Id.Entry)] = e.Model.Title.GetFormattedText();
            }
        if (inv.CardRemovalEntry is { IsStocked: true } rm) items.Add(new ShopOffer("removal", "REMOVAL", rm.Cost));

        var a = ShopCoach.Advise(lib, Store.Common, run, items);
        var sb = new StringBuilder("[b]Shop[/b]\n");
        var buy = new HashSet<int>(a.Plan.Buy);
        foreach (var i in Enumerable.Range(0, a.Options.Count).OrderByDescending(i => a.Options[i].Lift ?? -1))
        {
            var o = a.Options[i];
            var name = o.Item.Kind == "removal" ? "Card Removal" : names.GetValueOrDefault((o.Item.Kind, o.Item.Id), o.Name) + (o.Item.Upgraded ? "+" : "");
            var price = o.Affordable ? $"{o.Item.Price}g" : $"{o.Item.Price}g (can't afford)";
            sb.Append(Row(buy.Contains(i), name, Lift(o.Lift, o.N), price));
        }
        // the plan text uses ids for names; rebuild it with display names
        sb.Append($"[color={Grey}]{Esc(PlanText(a, names))}[/color]\n");
        sb.Append(Footer(lib));
        Overlay.Show(Header(run, a.Profile), sb.ToString());
    }

    private static string PlanText(ShopAdvice a, Dictionary<(string, string), string> names)
    {
        if (a.Plan.Buy.Count == 0) return a.Plan.Why;
        var list = a.Plan.Buy.OrderByDescending(i => a.Options[i].Lift)
            .Select(i => a.Options[i].Item.Kind == "removal" ? "Card Removal" : names.GetValueOrDefault((a.Options[i].Item.Kind, a.Options[i].Item.Id), a.Options[i].Name));
        return $"Buy {string.Join(", ", list)} for {a.Plan.Cost}g (keeps {a.Plan.Left}).";
    }
}
