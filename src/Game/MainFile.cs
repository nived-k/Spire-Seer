using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using SpireSeer.UI;

namespace SpireSeer.Game;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "SpireSeer";

    public static void Initialize()
    {
        var harmony = new Harmony("nived-k." + ModId);
        // patch one class at a time: if a game update renames a hooked method, only that screen stops working
        int ok = 0, failed = 0;
        foreach (var t in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
        {
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (System.Exception e) { failed++; Log.Error($"[SpireSeer] could not patch {t.Name}: {e.Message}"); }
        }
        Log.Info($"[SpireSeer] {ok} hooks installed, {failed} failed");
        RunManager.Instance.RunStarted += _ => Advisor.Safe("run started", Advisor.ShowDeck);
        RunManager.Instance.RoomEntered += () => Advisor.Safe("room entered", Advisor.ShowDeck);
        RunManager.Instance.ActEntered += () => Advisor.Safe("act entered", Advisor.ShowDeck);
        Log.Info($"[SpireSeer] loaded ({Assembly.GetExecutingAssembly().GetName().Version})");
    }
}

/// <summary>F9 / Shift+F9 and dragging the panel. Postfix only; never consumes the game's input.</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class InputHook
{
    static void Postfix(InputEvent inputEvent) => Advisor.Safe("input", () => Overlay.OnInput(inputEvent));
}

/// <summary>Show the panel on the main menu, so players can see the mod is running before they start a run.
/// (Mods are loaded during NGame's startup, so an NGame._Ready patch would come too late.)</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class MainMenuReady
{
    static void Postfix() => Advisor.Safe("main menu", Advisor.ShowMenu);
}
