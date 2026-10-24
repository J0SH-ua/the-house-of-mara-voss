using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Shared foundation for the code-built final-act rooms.
///
/// Rooms 1-3 are TileMap-layer scenes. Rooms 5 and 6 are authored,
/// full-screen paintings, so their interactive objects are explicit design
/// rectangles over the artwork instead of tiles. This base keeps them on the
/// same production systems as the rest of the game: RoomExamineTracker for
/// objectives/clues/saves, Hotspots for Tab/hint rings (via tagged target
/// nodes), Dialogue for text, CursorHelper for hover, and GameState for
/// corruption, verbs, shards and room completion.
/// </summary>
public abstract partial class MemoryRoomBase : Node2D
{
    protected const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    protected const string SearchCursorPath = "res://assets/images/core/cursor_magnifying_glass.png";
    protected const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";
    protected const string EyeCursorPath = "res://assets/images/core/cursor_eye.png";

    protected sealed class Hotspot
    {
        public string Id { get; }
        public string DisplayName { get; }
        public Rect2 Rect { get; }
        public Vector2 Center { get; }
        public string SfxId { get; }
        public float SfxPitch { get; }
        public string CursorPath { get; }
        public bool Examinable { get; }

        public Hotspot(
            string id,
            string displayName,
            Rect2 rect,
            Vector2 center,
            string sfxId = Sfx.Examine,
            float sfxPitch = 1f,
            string cursorPath = SearchCursorPath,
            bool examinable = true)
        {
            Id = id;
            DisplayName = displayName;
            Rect = rect;
            Center = center;
            SfxId = string.IsNullOrWhiteSpace(sfxId) ? Sfx.Examine : sfxId;
            SfxPitch = sfxPitch;
            CursorPath = string.IsNullOrWhiteSpace(cursorPath) ? SearchCursorPath : cursorPath;
            Examinable = examinable;
        }
    }

    private readonly List<Hotspot> _hotspots = new();
    private readonly Dictionary<string, Hotspot> _hotspotsById = new();

    private Font _font;
    private Texture2D _backgroundTexture;
    private Color _backgroundTint = Colors.White;
    private Label _hoverLabel;
    private Hotspot _hovered;
    private bool _hoverCursorOwned;
    private ulong _lastHoverTickMsec;
    private ulong _lastClarityDeniedMsec;

    /// <summary>Set by a room while it owns a multi-line or transition sequence.</summary>
    protected bool Busy { get; set; }

    protected abstract string RoomName { get; }
    protected abstract string BackgroundPath { get; }
    protected abstract IEnumerable<Hotspot> BuildHotspots();
    protected abstract void OnHotspotClicked(Hotspot hotspot);

    protected virtual void OnRoomReady()
    {
    }

    /// <summary>Room-specific overlays (Anchor rings, Trace lines), drawn over the art.</summary>
    protected virtual void DrawRoom()
    {
    }

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);

        _font = GD.Load<FontFile>(FontPath);
        _backgroundTexture = GD.Load<Texture2D>(BackgroundPath);
        if (_backgroundTexture == null)
            Logger.Error($"[{RoomName}] Could not load room background: {BackgroundPath}");

        foreach (var hotspot in BuildHotspots())
        {
            if (hotspot == null || string.IsNullOrWhiteSpace(hotspot.Id))
                continue;
            _hotspots.Add(hotspot);
            _hotspotsById[hotspot.Id] = hotspot;

            // Hotspots.RevealAll/RevealObject understand these tagged targets,
            // so Tab and third-level hints work exactly as in TileMap rooms.
            var target = new Node2D
            {
                Name = $"{hotspot.Id}Target",
                Position = hotspot.Center,
            };
            target.SetMeta("memory_object", hotspot.Id);
            AddChild(target);
        }

        _hoverLabel = CreateLabel(string.Empty, Vector2.Zero, 19,
            new Color(0.97f, 0.93f, 0.84f), new Vector2(190, 34));
        _hoverLabel.Visible = false;
        var chip = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.035f, 0.03f, 0.82f),
            BorderColor = new Color(0.78f, 0.66f, 0.46f, 0.45f),
        };
        chip.SetBorderWidthAll(1);
        chip.SetCornerRadiusAll(4);
        chip.ContentMarginLeft = 8;
        chip.ContentMarginRight = 8;
        chip.ContentMarginTop = 4;
        chip.ContentMarginBottom = 4;
        _hoverLabel.AddThemeStyleboxOverride("normal", chip);

        OnRoomReady();
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        ClearHover();
    }

    public override void _Process(double delta)
    {
        if (Busy || Dialogue.IsInputBlocked || Dialogue.IsShowing ||
            GetTree().Paused || SceneManager.IsChanging)
        {
            SetHovered(null);
            return;
        }

        Vector2 mouse = GetGlobalMousePosition();
        SetHovered(IsOverUi(mouse) ? null : FindHotspot(mouse));
        if (_hovered != null)
            QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse || !mouse.Pressed ||
            mouse.ButtonIndex != MouseButton.Left)
            return;

        if (Busy || Dialogue.IsInputBlocked || Dialogue.IsShowing ||
            GetTree().Paused || SceneManager.IsChanging)
            return;

        Vector2 position = GetGlobalMousePosition();
        if (IsOverUi(position))
            return;

        var hotspot = FindHotspot(position);
        if (hotspot == null)
            return;

        GetTree().Root.SetInputAsHandled();
        Hotspots.SpawnRipple(position);

        // Clarity reveals TileMap layers in Rooms 1-3. These rooms have no
        // false tile surface; their puzzle language is the Memory Verbs.
        if (ClaritySystem.IsArmed)
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastClarityDeniedMsec > 1200)
            {
                _lastClarityDeniedMsec = now;
                AudioManager.Play(Sfx.Denied, -10f, 1.05f);
                GameState.Instance?.Toast(
                    "Clarity finds no false surface to lift here. This room asks to be remembered differently.");
            }
            return;
        }

        OnHotspotClicked(hotspot);
    }

    public override void _Draw()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        if (_backgroundTexture != null)
            DrawTextureRect(_backgroundTexture, screen, false, _backgroundTint);
        else
            DrawRect(screen, new Color(0.03f, 0.025f, 0.02f));

        if (_hovered != null)
        {
            float alpha = Settings.ReduceMotionOrDefault()
                ? 0.72f
                : 0.62f + 0.22f * (float)Math.Sin(Time.GetTicksMsec() / 210.0);
            DrawRect(_hovered.Rect, new Color(1f, 0.89f, 0.58f, 0.07f));
            DrawRect(_hovered.Rect, new Color(1f, 0.89f, 0.58f, alpha), false, 2f);
        }

        DrawRoom();
    }

    // -------------------------------------------------------------- helpers

    protected Hotspot GetHotspot(string id)
    {
        return id != null && _hotspotsById.TryGetValue(id, out var hotspot) ? hotspot : null;
    }

    protected Hotspot FindHotspot(Vector2 position)
    {
        // Later hotspots are more specific (a desk drawer over the desk).
        for (int i = _hotspots.Count - 1; i >= 0; i--)
        {
            if (_hotspots[i].Rect.HasPoint(position))
                return _hotspots[i];
        }
        return null;
    }

    protected bool WasExamined(string objectId)
    {
        return RoomExamineTracker.GetClickedLayers(RoomName, objectId) != null;
    }

    /// <returns>True only for the first recorded look at this object.</returns>
    protected bool RecordExamine(Hotspot hotspot)
    {
        if (hotspot == null)
            return false;
        var clicked = RoomExamineTracker.GetClickedLayers(RoomName, hotspot.Id);
        int next = (clicked?.Count ?? 0) + 1;
        RoomExamineTracker.RecordExamine(RoomName, hotspot.Id, $"Examine{next}");
        return clicked == null;
    }

    protected bool AllRequiredExamined()
    {
        return RoomExamineTracker.HasRoomBeenFullyExamined(RoomName);
    }

    protected void GetProgress(out int done, out int total)
    {
        RoomExamineTracker.GetRoomProgress(RoomName, out done, out total, out _);
    }

    protected void SetBackgroundTint(Color tint)
    {
        _backgroundTint = tint;
        QueueRedraw();
    }

    protected async Task Say(string text, float duration = 4.5f, int fontSize = 24)
    {
        if (!GodotObject.IsInstanceValid(this) || string.IsNullOrWhiteSpace(text))
            return;
        await Dialogue.ShowText(this, text, duration, 576f, 324f, FontPath, fontSize);
    }

    protected async Task SaySequence(params string[] lines)
    {
        if (lines == null)
            return;
        foreach (var line in lines)
        {
            if (!GodotObject.IsInstanceValid(this))
                return;
            await Say(line, 5f, 24);
        }
    }

    /// <summary>
    /// Standard final-act examine: record first looks, replay a short repeat
    /// line afterwards, and make repeated looking fray the memory just as it
    /// does in the TileMap rooms.
    /// </summary>
    protected async Task ExamineObjectAsync(
        Hotspot hotspot,
        string[] firstLines,
        string repeatLine)
    {
        if (hotspot == null || Busy)
            return;

        Busy = true;
        try
        {
            bool first = RecordExamine(hotspot);
            AudioManager.PlayVaried(hotspot.SfxId, -5f, hotspot.SfxPitch, 0.04f);
            if (first)
            {
                await SaySequence(firstLines ?? Array.Empty<string>());
            }
            else
            {
                GameState.Instance?.AddCorruption(1);
                if (!string.IsNullOrWhiteSpace(repeatLine))
                    await Say(repeatLine, 3.5f, 24);
            }
        }
        finally
        {
            Busy = false;
            QueueRedraw();
        }
    }

    protected Label CreateLabel(
        string text,
        Vector2 position,
        int fontSize,
        Color color,
        Vector2 size)
    {
        var label = new Label
        {
            Text = text ?? string.Empty,
            Position = position,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 2600,
        };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
        return label;
    }

    protected async Task WaitSeconds(float seconds)
    {
        if (!IsInsideTree() || seconds <= 0f)
            return;
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    // -------------------------------------------------------------- hover/UI

    private void SetHovered(Hotspot next)
    {
        if (_hovered == next)
            return;

        if (_hoverCursorOwned)
        {
            CursorHelper.EndHover(this);
            _hoverCursorOwned = false;
        }

        _hovered = next;
        if (_hovered == null)
        {
            if (_hoverLabel != null)
                _hoverLabel.Visible = false;
            QueueRedraw();
            return;
        }

        CursorHelper.BeginHover(this, _hovered.CursorPath, 32, 32);
        _hoverCursorOwned = true;

        ulong now = Time.GetTicksMsec();
        if (now - _lastHoverTickMsec > 90)
        {
            _lastHoverTickMsec = now;
            AudioManager.Play(Sfx.UiHover, -13f);
        }

        if (_hoverLabel != null)
        {
            _hoverLabel.Text = _hovered.DisplayName;
            float x = Mathf.Clamp(_hovered.Center.X - 95f, 8f, 954f);
            float y = Mathf.Clamp(_hovered.Rect.Position.Y - 42f, 44f, 600f);
            _hoverLabel.Position = new Vector2(x, y);
            _hoverLabel.Visible = true;
        }
        QueueRedraw();
    }

    private void ClearHover()
    {
        if (_hoverCursorOwned)
        {
            CursorHelper.EndHover(this);
            _hoverCursorOwned = false;
        }
        _hovered = null;
        if (_hoverLabel != null && GodotObject.IsInstanceValid(_hoverLabel))
            _hoverLabel.Visible = false;
    }

    /// <summary>
    /// GameUI is a root-viewport CanvasLayer above the room SubViewport.
    /// Node _Input can otherwise claim a room click in the same press that
    /// activates its Step Back / Memory Verb buttons.
    /// </summary>
    private bool IsOverUi(Vector2 position)
    {
        if (position.Y >= 606f)
            return true;

        if (position.X >= 954f && position.X <= 1144f && position.Y >= 552f && position.Y <= 606f)
            return true;

        if (position.X >= 12f && position.X <= 276f && position.Y >= 552f &&
            SceneManager.TryGetBackTarget(SceneManager.CurrentScenePath, out _, out _))
            return true;

        var gs = GameState.Instance;
        bool anyVerb = gs != null &&
                       (gs.HasVerb(MemoryVerb.Echo) || gs.HasVerb(MemoryVerb.Anchor) ||
                        gs.HasVerb(MemoryVerb.Trace) || gs.HasVerb(MemoryVerb.Release));
        return anyVerb && position.X >= 220f && position.X <= 932f &&
               position.Y >= 540f && position.Y <= 604f;
    }
}
