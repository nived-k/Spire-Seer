using System.Collections.Generic;

namespace SpireSeer.Core;

/// <summary>What the coach needs to know about the run, read from the game. Ids are without the "CARD." / "RELIC." prefix.</summary>
public sealed record RunSnapshot(
    string Character,          // lower case, e.g. "necrobinder"
    int Act,                   // 1-based
    int Floor,
    int Hp,
    int MaxHp,
    int Gold,
    IReadOnlyList<string> Deck, // card ids (upgrades don't change the profile)
    IReadOnlyList<string> Relics,
    int RemovalsUsed);

/// <summary>A card on offer.</summary>
public sealed record Offer(string Id, bool Upgraded);

/// <summary>A plain-language warning or hint. Level is "warn" or "info".</summary>
public sealed record Nudge(string Kind, string Level, string Text);
