using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace SpireSeer.Game;

// Postfixes only, never change game state, never throw into the game (Safe() logs and swallows).

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
internal static class CardRewardShown
{
    static void Postfix(IReadOnlyList<CardCreationResult> options) =>
        Advisor.Safe("card reward", () => Advisor.CardReward(options.Select(o => o.Card).ToList()));
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "_ExitTree")]
internal static class CardRewardClosed
{
    static void Postfix() => Advisor.Safe("card reward closed", Advisor.ShowDeck);
}

[HarmonyPatch(typeof(NChooseARelicSelection), nameof(NChooseARelicSelection.ShowScreen))]
internal static class RelicChoiceShown
{
    static void Postfix(IReadOnlyList<RelicModel> relics) =>
        Advisor.Safe("relic choice", () => Advisor.RelicChoice(relics, "boss"));
}

[HarmonyPatch(typeof(NChooseARelicSelection), "_ExitTree")]
internal static class RelicChoiceClosed
{
    static void Postfix() => Advisor.Safe("relic choice closed", Advisor.ShowDeck);
}

/// <summary>Called whenever an event page shows its options (including Ancients, whose options are relics).</summary>
[HarmonyPatch(typeof(NEventRoom), "SetOptions")]
internal static class EventOptionsShown
{
    static void Postfix(EventModel eventModel) => Advisor.Safe("event", () => Advisor.Event(eventModel));
}

[HarmonyPatch(typeof(NEventRoom), "_ExitTree")]
internal static class EventClosed
{
    static void Postfix() => Advisor.Safe("event closed", Advisor.ShowDeck);
}

[HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Open))]
internal static class ShopOpened
{
    static void Postfix(NMerchantInventory __instance) => Advisor.Safe("shop", () => Advisor.Shop(__instance.Inventory));
}

[HarmonyPatch(typeof(NMerchantInventory), "OnPurchaseCompleted")]
internal static class ShopPurchase
{
    static void Postfix(NMerchantInventory __instance) => Advisor.Safe("shop purchase", () => Advisor.Shop(__instance.Inventory));
}

[HarmonyPatch(typeof(NMerchantInventory), "Close")]
internal static class ShopClosed
{
    static void Postfix() => Advisor.Safe("shop closed", Advisor.ShowDeck);
}
