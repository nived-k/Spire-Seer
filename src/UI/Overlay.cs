using System;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireSeer.UI;

/// <summary>Saved per user in the game's user data folder (never inside the mod folder, which Steam may overwrite).</summary>
internal sealed class OverlaySettings
{
    public float X { get; set; } = 24;
    public float Y { get; set; } = 120;
    public bool Visible { get; set; } = true;
    public int FontSize { get; set; } = 15;

    private static string PathOf() => System.IO.Path.Combine(OS.GetUserDataDir(), "SpireSeer.settings.json");

    public static OverlaySettings Load()
    {
        try
        {
            var p = PathOf();
            if (File.Exists(p)) return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(p)) ?? new();
        }
        catch (Exception e) { Log.Warn($"[SpireSeer] settings: {e.Message}"); }
        return new();
    }

    public void Save()
    {
        try { File.WriteAllText(PathOf(), JsonSerializer.Serialize(this)); }
        catch (Exception e) { Log.Warn($"[SpireSeer] settings: {e.Message}"); }
    }
}

/// <summary>A small draggable panel drawn above the game. Built from stock Godot nodes (no custom Node classes).
/// Input arrives through a postfix on NGame._Input (see InputHook).</summary>
internal static class Overlay
{
    private static readonly int[] Sizes = { 13, 15, 18, 21 };

    private static CanvasLayer? _layer;
    private static PanelContainer? _panel;
    private static RichTextLabel? _header, _body;
    private static OverlaySettings _cfg = new();
    private static bool _dragging;
    private static Vector2 _dragOffset;
    private static string _lastHeader = "", _lastBody = "";

    private static void Build()
    {
        _cfg = OverlaySettings.Load();
        _layer = new CanvasLayer { Layer = 100, Name = "SpireSeerOverlay" };
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, Position = new Vector2(_cfg.X, _cfg.Y) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.07f, 0.08f, 0.86f),
            BorderColor = new Color(0.91f, 0.76f, 0.35f, 0.6f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        _panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        _header = MakeLabel();
        _body = MakeLabel();
        box.AddChild(_header);
        box.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });
        box.AddChild(_body);
        _panel.AddChild(box);
        _layer.AddChild(_panel);
        ApplyFont();
        _layer.Visible = _cfg.Visible;
    }

    private static RichTextLabel MakeLabel() => new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(380, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static void ApplyFont()
    {
        foreach (var l in new[] { _header, _body })
        {
            if (l == null) continue;
            l.AddThemeFontSizeOverride("normal_font_size", _cfg.FontSize);
            l.AddThemeFontSizeOverride("bold_font_size", _cfg.FontSize);
            l.CustomMinimumSize = new Vector2(380 * _cfg.FontSize / 15f, 0);
        }
        _panel?.ResetSize();
    }

    /// <summary>Adds the layer under the game's root node once the game exists.</summary>
    private static bool EnsureAttached()
    {
        if (_layer == null) Build();
        if (_layer!.IsInsideTree()) return true;
        var game = NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game)) return false;
        if (_layer.GetParent() == null) game.CallDeferred(Node.MethodName.AddChild, _layer);
        return false;
    }

    public static void Show(string header, string body)
    {
        _lastHeader = header;
        _lastBody = body;
        EnsureAttached();
        if (_header == null || _body == null) return;
        _header.Visible = header.Length > 0;
        _header.Text = header.TrimEnd('\n');
        _body.Text = body.TrimEnd('\n');
        _panel!.ResetSize();
        ClampToScreen();
    }

    private static void ClampToScreen()
    {
        if (_panel == null || !_panel.IsInsideTree()) return;
        var vp = _panel.GetViewportRect().Size;
        var p = _panel.Position;
        var s = _panel.Size;
        _panel.Position = new Vector2(Math.Clamp(p.X, 0, Math.Max(0, vp.X - s.X)), Math.Clamp(p.Y, 0, Math.Max(0, vp.Y - 40)));
    }

    public static void OnInput(InputEvent e)
    {
        if (_layer == null) return;
        if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.F9)
        {
            if (k.ShiftPressed)
            {
                // Shift+F9 cycles the text size
                int i = Array.IndexOf(Sizes, _cfg.FontSize);
                _cfg.FontSize = Sizes[(i + 1) % Sizes.Length];
                ApplyFont();
            }
            else
            {
                _cfg.Visible = !_cfg.Visible;
                _layer.Visible = _cfg.Visible;
            }
            _cfg.Save();
            return;
        }
        if (_panel == null || !_layer.Visible || !_panel.IsInsideTree()) return;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            var mouse = _panel.GetGlobalMousePosition();
            if (mb.Pressed && _panel.GetGlobalRect().HasPoint(mouse))
            {
                _dragging = true;
                _dragOffset = mouse - _panel.GlobalPosition;
            }
            else if (!mb.Pressed && _dragging)
            {
                _dragging = false;
                ClampToScreen();
                _cfg.X = _panel.Position.X;
                _cfg.Y = _panel.Position.Y;
                _cfg.Save();
            }
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            _panel.GlobalPosition = _panel.GetGlobalMousePosition() - _dragOffset;
        }
    }

    /// <summary>Re-shows the last text (e.g. after the layer got attached).</summary>
    public static void Refresh() => Show(_lastHeader, _lastBody);
}
