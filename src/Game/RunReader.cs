using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using SpireSeer.Core;

namespace SpireSeer.Game;

/// <summary>The only place that reads run internals. Everything else works on <see cref="RunSnapshot"/>.</summary>
internal static class RunReader
{
    public static RunSnapshot? Snapshot()
    {
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null) return null;
        var p = LocalContext.GetMe(state.Players) ?? (state.Players.Count > 0 ? state.Players[0] : null);
        if (p == null) return null;
        return new RunSnapshot(
            Character: p.Character.Id.Entry.ToLowerInvariant(),
            Act: state.CurrentActIndex + 1,
            Floor: state.TotalFloor,
            Hp: p.Creature.CurrentHp,
            MaxHp: p.Creature.MaxHp,
            Gold: p.Gold,
            Deck: p.Deck.Cards.Select(c => c.Id.Entry).ToList(),
            Relics: p.Relics.Select(r => r.Id.Entry).ToList(),
            RemovalsUsed: p.ExtraFields.CardShopRemovalsUsed);
    }
}
