using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

/// <summary>
/// Base class for every clickable examine layer.
/// Left-click cycles Examine1 → Examine2 → … and plays the layer's dialogue;
/// while Clarity is armed, left-click reveals instead; right-click releases
/// Clarity for free. Examining past the first layer corrupts the memory —
/// routed through GameState so tiers, audio and HUD react.
/// </summary>
public partial class ExamineHandler : TileMapLayer
{
    [Export(PropertyHint.MultilineText)] public string ExamineText = "";
    [Export] public float DialogueDuration = 3f;
    [Export(PropertyHint.MultilineText)] public string DialogueDurationText = "";
    [Export] public int DialogueFontSize = 32;
    [ExportCategory("Hover Cursor")]
    [Export(PropertyHint.File, "*.png,*.webp,*.jpg")] public string UnlockedCursorTexturePath = "res://assets/images/core/cursor_magnifying_glass.png";
    [Export(PropertyHint.File, "*.png,*.webp,*.jpg")] public string LockedCursorTexturePath = "res://assets/images/core/cursor_locked_magnifying_glass.png";
    [Export] public int CursorWidth = 30;
    [Export] public int CursorHeight = 30;
    [Export] public bool HoverEnabled = true;
    [ExportCategory("Audio")]
    [Export] public string ExamineSfxId = Sfx.Examine;

    private bool _isHovering = false;
    private string _currentHoverCursorTexturePath = string.Empty;
    private Rect2I _usedRect;
    private bool _hasUsedRect;
    private int _cachedTrackerVersion = -1;
    private bool _cachedRoomReady;
    private static ulong _lastHoverTickMsec;

    public override void _Ready()
    {
        SetProcess(true);
        SetProcessInput(true);

        // Cache the tile bounds once: the per-frame hover test then only pays
        // for a rect check on the ~one layer actually under the mouse.
        _usedRect = GetUsedRect();
        _hasUsedRect = _usedRect.Size.X > 0 && _usedRect.Size.Y > 0;
    }

    public override void _ExitTree()
    {
        // Always clean up cursor on removal, regardless of any state flags.
        if (_isHovering)
        {
            CursorHelper.EndHover(this);
            _isHovering = false;
            _currentHoverCursorTexturePath = string.Empty;
        }
    }

    private bool MouseOnTile(out Vector2I cell)
    {
        Vector2 localMouse = ToLocal(GetGlobalMousePosition());
        cell = LocalToMap(localMouse);
        if (_hasUsedRect && !_usedRect.HasPoint(cell))
            return false;
        return GetCellSourceId(cell) != -1;
    }

    /// <summary>Faint paper-brush tick on hover-enter, throttled so sweeping
    /// the mouse across a crowded shelf doesn't machine-gun.</summary>
    private static void PlayHoverTick()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastHoverTickMsec < 90)
            return;
        _lastHoverTickMsec = now;
        AudioManager.Play(Sfx.UiHover, -12f);
    }

    public override void _Input(InputEvent @event)
    {
        // Right-click anywhere on an examine layer releases Clarity for free.
        if (@event is InputEventMouseButton rightClick && rightClick.Pressed &&
            rightClick.ButtonIndex == MouseButton.Right && ClaritySystem.IsArmed)
        {
            ClaritySystem.Disarm();
            GetTree().Root.SetInputAsHandled();
            return;
        }

        if (Dialogue.IsInputBlocked)
            return;

        if (@event is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
            return;

        if (!MouseOnTile(out _))
            return;

        // If clarifying, delegate entirely to clarify logic and return.
        // ClaritySystem keeps the mode armed when there's nothing to reveal.
        if (ClaritySystem.IsArmed)
        {
            ClaritySystem.TryReveal(this);
            GetTree().Root.SetInputAsHandled();
            return;
        }

        AudioManager.PlayVaried(string.IsNullOrWhiteSpace(ExamineSfxId) ? Sfx.Examine : ExamineSfxId, -3f, 1f, 0.05f);
        Hotspots.SpawnRipple(GetGlobalMousePosition());

        ExamineHelper.CycleExamine(this);
        RoomExamineTracker.OnExamineClicked(this);
        OnExamineClicked();
        OnAnyExamineClicked();
        CheckRoomCompletion();

        var dialogueSegments = GetDialogueSegments();
        var dialogueDurations = GetDialogueDurations();
        var globalPos = GetGlobalPosition();
        _ = ShowDialogueSequenceAsync(dialogueSegments, dialogueDurations, globalPos);

        GetTree().Root.SetInputAsHandled();
    }

    private List<string> GetDialogueSegments()
    {
        var segments = new List<string>();

        if (string.IsNullOrWhiteSpace(ExamineText))
            return segments;

        var pipeSplit = ExamineText.Split('|');
        foreach (var part in pipeSplit)
        {
            var lineSplit = part.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lineSplit)
            {
                var segment = line.Trim();
                if (!string.IsNullOrWhiteSpace(segment))
                    segments.Add(segment);
            }
        }

        return segments;
    }

    private List<float> GetDialogueDurations()
    {
        var durations = new List<float>();

        if (!string.IsNullOrWhiteSpace(DialogueDurationText))
        {
            var pipeSplit = DialogueDurationText.Split('|');
            foreach (var part in pipeSplit)
            {
                var lineSplit = part.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lineSplit)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ||
                        float.TryParse(trimmed, out val))
                    {
                        durations.Add(val);
                    }
                }
            }
        }

        if (durations.Count == 0)
            durations.Add(DialogueDuration);

        return durations;
    }

    private async Task ShowDialogueSequenceAsync(List<string> segments, List<float> durations, Vector2 globalPos)
    {
        Dialogue.BeginInputBlock();

        // When input is blocked the cursor must be cleared immediately —
        // _Process will not run its EndHover path while blocked.
        if (_isHovering)
        {
            CursorHelper.EndHover(this);
            _isHovering = false;
            _currentHoverCursorTexturePath = string.Empty;
        }

        try
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var text = segments[i];
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                float duration = durations[Math.Min(i, durations.Count - 1)];
                await Dialogue.ShowText(this, text, duration, globalPos.X, globalPos.Y, fontSize: DialogueFontSize);
            }
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private void OnAnyExamineClicked()
    {
        // Only looking *deeper* than the first layer destabilises the memory.
        if (ExamineHelper.ExtractExamineNumber(Name.ToString()) != 1)
        {
            if (GameState.Instance != null)
                GameState.Instance.AddCorruption(1);
            else if (Globals.Instance != null)
                Globals.Instance.CORRUPTION_COUNT++;
        }
    }

    private void CheckRoomCompletion()
    {
        string roomName = RoomExamineTracker.ResolveRoomName(this);
        if (RoomExamineTracker.HasRoomBeenFullyExamined(roomName))
            GameState.Instance?.MarkRoomCompleted(roomName);
    }

    protected virtual void OnExamineClicked()
    {
    }

    public override void _Process(double delta)
    {
        // While input is blocked (dialogue playing), forcibly end any hover so
        // the cursor doesn't stay stuck as a magnifying glass during dialogue.
        if (Dialogue.IsInputBlocked || !HoverEnabled)
        {
            if (_isHovering)
            {
                CursorHelper.EndHover(this);
                _isHovering = false;
                _currentHoverCursorTexturePath = string.Empty;
            }
            return;
        }

        bool hasTile = MouseOnTile(out _);

        if (hasTile)
        {
            // Determine which cursor to show.
            string desiredCursor = UnlockedCursorTexturePath;

            var parent = GetParent();
            bool isFinal = parent != null && parent.Name.ToString().EndsWith("Final");
            if (isFinal)
            {
                // Recompute the expensive room check only when progress changed.
                if (_cachedTrackerVersion != RoomExamineTracker.Version)
                {
                    _cachedTrackerVersion = RoomExamineTracker.Version;
                    _cachedRoomReady = RoomExamineTracker.HasRoomBeenFullyExamined(
                        RoomExamineTracker.ResolveRoomName(this));
                }
                desiredCursor = _cachedRoomReady ? UnlockedCursorTexturePath : LockedCursorTexturePath;
            }

            // Only call BeginHover when we first enter or when the cursor type changes.
            if (!_isHovering || _currentHoverCursorTexturePath != desiredCursor)
            {
                bool entering = !_isHovering;
                CursorHelper.BeginHover(this, desiredCursor, CursorWidth, CursorHeight);
                if (entering)
                    PlayHoverTick();
                _currentHoverCursorTexturePath = desiredCursor;
                _isHovering = true;
            }
        }
        else
        {
            if (_isHovering)
            {
                CursorHelper.EndHover(this);
                _isHovering = false;
                _currentHoverCursorTexturePath = string.Empty;
            }
        }
    }
}
