using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The second half of the lockpick Clarity rewind: after the lock
/// itself has run backwards in Mara's hands, the house does too.
///
/// This is deliberately a presentation scene, not a tour through the
/// interactive rooms. It shows the places she actually crossed, in
/// reverse and in true black-and-white — under the house, back up
/// the duct as the far grille settles square, Room 5, the hallway,
/// and the walked legs through the kept room and sitting room to
/// the kitchen — without waking any room's dialogue, objectives or
/// autosave beats along the way. The state has already been rewound
/// by GameState before this scene starts; the little overlays here
/// only show that happening (the breaker cooling, her looking at
/// the basement's kept things coming undone).
/// </summary>
public partial class LockpickRewindJourney : Node2D
{
    public const string FromBasementFlag = "lockpick_rewind_from_basement";
    public const string BreakerWasOnFlag = "lockpick_rewind_breaker_was_on";

    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string KitchenCabinetScenePath = "res://scenes/world/kitchen_cabinet.tscn";
    private const float CrossfadeSeconds = 0.16f;

    private static readonly Vector2 ScreenSize = new(1152, 648);
    private static readonly Vector2 ScreenCenter = new(576, 324);

    // The duct mouth in Vent_Crawl_BG.png, measured off the art the
    // same way VentCrawl seats its grille (1376x768: x 616-756,
    // y 332-400) — so the grille that flew off down there is the
    // grille that settles back here.
    private static readonly Vector2 VentOpeningMin = new(0.4477f, 0.4323f);
    private static readonly Vector2 VentOpeningMax = new(0.5494f, 0.5208f);

    // The basement's investigated things, at their hotspot centres
    // in the 1152x648 room: furnace, preserve shelves, toy crate.
    private static readonly Vector2[] BasementInvestigatedCentres =
    [
        new(153f / 1152f, 328f / 648f),
        new(503f / 1152f, 348f / 648f),
        new(838f / 1152f, 438f / 648f),
    ];

    private enum ShotKind
    {
        Still,
        Basement,
        Vent,
        Walk,
    }

    private sealed class RewindShot
    {
        public Texture2D Texture;
        public string Caption;
        public float Seconds;
        public ShotKind Kind;
        public Vector2 StartFocus;
        public Vector2 EndFocus;
        public float StartZoom;
        public float EndZoom;
    }

    // How LockpickPuzzle hands this scene its one backwards run.
    // The two booleans that matter after a quit-and-continue are
    // also saved as story flags; the rest is presentation only.
    private static bool? _pendingFromBasement;
    private static bool? _pendingBreakerWasOn;
    private static bool? _pendingDuctWasOpen;
    private static int _pendingInvestigatedMask = -1;

    public static void Arm(bool fromBasement, bool breakerWasOn, bool ductWasOpen, int investigatedMask)
    {
        _pendingFromBasement = fromBasement;
        _pendingBreakerWasOn = breakerWasOn;
        _pendingDuctWasOpen = ductWasOpen;
        _pendingInvestigatedMask = investigatedMask;
    }

    private readonly List<RewindShot> _shots = new();
    private FontFile _font;
    private Label _rewindLabel;
    private Label _captionLabel;

    private bool _fromBasement;
    private bool _breakerWasOn;
    private bool _ductWasOpen;
    private int _investigatedMask;
    private bool _reduceMotion;

    private float _elapsed;
    private float _totalSeconds;
    private int _currentShotIndex = -1;
    private bool _finished;
    private bool _inputBlockOwned;

    private int _breakerClickStage;
    private int _ventKickStage;
    private bool _ventCrawlPlayed;
    private float _footstepTimer;
    private int _footstepIndex;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();
        _font = GD.Load<FontFile>(FontPath);
        _reduceMotion = Settings.ReduceMotionOrDefault();

        var gs = GameState.Instance;
        _fromBasement = _pendingFromBasement ?? (gs?.HasFlag(FromBasementFlag) ?? false);
        _breakerWasOn = _pendingBreakerWasOn ?? (gs?.HasFlag(BreakerWasOnFlag) ?? false);
        _ductWasOpen = _pendingDuctWasOpen ?? _fromBasement;
        _investigatedMask = _pendingInvestigatedMask >= 0 ? _pendingInvestigatedMask : 0;
        _pendingFromBasement = null;
        _pendingBreakerWasOn = null;
        _pendingDuctWasOpen = null;
        _pendingInvestigatedMask = -1;

        BuildShots();
        BuildLabels();

        Dialogue.BeginInputBlock();
        _inputBlockOwned = true;
        GameUI.Instance?.SetMonochromeFlashback(1f);
        AudioManager.Play(Sfx.WindGust, -11f, 1.35f);
        QueueRedraw();
    }

    private void AddShot(string texturePath, string caption, float seconds, ShotKind kind,
        Vector2 startFocus, Vector2 endFocus, float startZoom, float endZoom)
    {
        var texture = GD.Load<Texture2D>(texturePath);
        if (texture == null)
            return;
        var shot = new RewindShot
        {
            Texture = texture,
            Caption = caption,
            Seconds = seconds * (_reduceMotion ? 0.82f : 1f),
            Kind = kind,
            StartFocus = startFocus,
            EndFocus = endFocus,
            StartZoom = startZoom,
            EndZoom = endZoom,
        };
        _shots.Add(shot);
        _totalSeconds += shot.Seconds;
    }

    private void BuildShots()
    {
        if (_fromBasement)
        {
            // From the trunk, back across the kept-under things to
            // the vent overhead; then the crawl itself, reversed:
            // close on the far grille and shrinking away down the
            // duct, exactly the forward crawl's zoom run backwards.
            string basementCaption =
                _breakerWasOn && _investigatedMask != 0
                    ? "under the house — the breaker cools, the looking comes undone"
                    : _breakerWasOn
                        ? "under the house — the breaker cools"
                        : _investigatedMask != 0
                            ? "under the house — the looking comes undone"
                            : "under the house";
            AddShot("res://assets/images/bgs/Basement_BG.png",
                basementCaption,
                1.15f, ShotKind.Basement,
                new Vector2(0.84f, 0.72f), new Vector2(0.284f, 0.211f), 1.34f, 1.07f);
            AddShot("res://assets/images/bgs/Vent_Crawl_BG.png",
                "the duct — kicks landing backwards, the grille settling square",
                1.55f, ShotKind.Vent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 2.68f, 1.14f);
        }

        AddShot("res://assets/images/bgs/Room_5_BG.png",
            "the room that will not stay one room",
            0.62f, ShotKind.Still,
            new Vector2(0.36f, 0.78f), new Vector2(0.58f, 0.45f), 1.34f, 1.08f);
        AddShot("res://assets/images/bgs/BgRoom4.png",
            "the hallway of photographs",
            0.55f, ShotKind.Still,
            new Vector2(0.58f, 0.46f), new Vector2(0.44f, 0.52f), 1.26f, 1.06f);
        // The walked legs, unwalked: each starts where its forward
        // journey ended (close on the door) and recedes to where it
        // began, using the same foci RoomJourney walks between.
        AddShot("res://assets/images/bgs/Journey_Hallway_Right.png",
            "left at the photographs — backwards",
            0.78f, ShotKind.Walk,
            new Vector2(773f / 1152f, 255f / 648f), new Vector2(460f / 1152f, 330f / 648f),
            1.75f, 1.04f);
        AddShot("res://assets/images/bgs/Room_3_BG.png",
            "the kept room",
            0.55f, ShotKind.Still,
            new Vector2(0.42f, 0.52f), new Vector2(0.55f, 0.47f), 1.24f, 1.06f);
        AddShot("res://assets/images/bgs/Journey_Hallway_Left.png",
            "right at the bent runner — backwards",
            0.78f, ShotKind.Walk,
            new Vector2(398f / 1152f, 240f / 648f), new Vector2(690f / 1152f, 330f / 648f),
            1.75f, 1.04f);
        AddShot("res://assets/images/bgs/Room2_no_objects.png",
            "the sitting room",
            0.55f, ShotKind.Still,
            new Vector2(0.55f, 0.48f), new Vector2(0.43f, 0.53f), 1.24f, 1.06f);
        AddShot("res://assets/images/bgs/Journey_Hallway_Door.png",
            "the hall — backwards",
            0.78f, ShotKind.Walk,
            new Vector2(580f / 1152f, 318f / 648f), new Vector2(0.5f, 0.5f),
            1.7f, 1.04f);
        AddShot("res://assets/images/bgs/Room_1_BG.png",
            "the kitchen — under the counter",
            0.6f, ShotKind.Still,
            new Vector2(0.58f, 0.48f), new Vector2(0.5f, 0.55f), 1.24f, 1.05f);
    }

    private void BuildLabels()
    {
        _rewindLabel = MakeLabel("<<  R E W I N D",
            new Vector2(276, 72), new Vector2(600, 44), 34,
            new Color(0.97f, 0.95f, 0.9f, 0.95f));
        _rewindLabel.ZIndex = 300;
        _captionLabel = MakeLabel(string.Empty,
            new Vector2(176, 500), new Vector2(800, 36), 23,
            new Color(0.93f, 0.9f, 0.82f, 0.94f));
        _captionLabel.ZIndex = 300;
    }

    private Label MakeLabel(string text, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(label);
        return label;
    }

    private bool GetShotPosition(out int index, out float localT, out RewindShot shot)
    {
        index = -1;
        localT = 0f;
        shot = null;
        float cursor = 0f;
        for (int i = 0; i < _shots.Count; i++)
        {
            float seconds = _shots[i].Seconds;
            if (_elapsed < cursor + seconds || i == _shots.Count - 1)
            {
                index = i;
                shot = _shots[i];
                localT = seconds > 0f
                    ? Mathf.Clamp((_elapsed - cursor) / seconds, 0f, 1f)
                    : 1f;
                return true;
            }
            cursor += seconds;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        if (_finished || _shots.Count == 0)
            return;

        float dt = Mathf.Max((float)delta, 0f);
        _elapsed += dt;
        if (_elapsed >= _totalSeconds)
        {
            Finish();
            return;
        }

        if (!GetShotPosition(out int index, out float localT, out var shot))
            return;
        if (index != _currentShotIndex)
            OnShotStarted(index, shot);
        UpdateShotSounds(shot, localT, dt);

        if (_captionLabel != null && _captionLabel.Text != shot.Caption)
            _captionLabel.Text = shot.Caption;
        if (_rewindLabel != null)
        {
            bool strobeOn = _reduceMotion || (Time.GetTicksMsec() / 90) % 2 == 0;
            _rewindLabel.Modulate = new Color(1, 1, 1, strobeOn ? 0.95f : 0.5f);
        }
        QueueRedraw();
    }

    private void OnShotStarted(int index, RewindShot shot)
    {
        _currentShotIndex = index;
        _breakerClickStage = 0;
        _ventKickStage = 0;
        _ventCrawlPlayed = false;
        _footstepTimer = 0f;

        if (shot.Kind == ShotKind.Vent && _ductWasOpen)
            AudioManager.Play(Sfx.VentBreak, -9f, 1.28f);
        else if (index > 0 && shot.Kind != ShotKind.Walk)
            AudioManager.Play(Sfx.WindGust, -20f, 1.12f + index * 0.03f);
    }

    private void UpdateShotSounds(RewindShot shot, float localT, float dt)
    {
        if (shot.Kind == ShotKind.Basement && _breakerWasOn)
        {
            // The three green eyes closing, one click at a time.
            float threshold = _breakerClickStage switch
            {
                0 => 0.22f,
                1 => 0.37f,
                2 => 0.52f,
                _ => 2f,
            };
            if (localT >= threshold)
            {
                AudioManager.Play(Sfx.LockpickTick, -11f, 1.12f - _breakerClickStage * 0.16f);
                _breakerClickStage++;
            }
        }
        else if (shot.Kind == ShotKind.Vent)
        {
            if (_ductWasOpen)
            {
                // Three kicks, landing backwards into a square frame.
                float threshold = _ventKickStage switch
                {
                    0 => 0.07f,
                    1 => 0.18f,
                    2 => 0.29f,
                    _ => 2f,
                };
                if (localT >= threshold)
                {
                    AudioManager.Play(Sfx.VentKick, -7f, 1.14f - _ventKickStage * 0.13f);
                    _ventKickStage++;
                }
            }
            if (!_ventCrawlPlayed && localT >= 0.42f)
            {
                _ventCrawlPlayed = true;
                AudioManager.PlayVaried(Sfx.VentCrawl, -10f, 1.12f, 0.04f);
            }
        }
        else if (shot.Kind == ShotKind.Walk)
        {
            _footstepTimer -= dt;
            if (_footstepTimer <= 0f)
            {
                _footstepTimer = 0.25f;
                string sfx = _footstepIndex++ % 2 == 0
                    ? Sfx.FootstepCarpetA
                    : Sfx.FootstepCarpetB;
                AudioManager.PlayVaried(sfx, -15f,
                    Mathf.Lerp(1.18f, 0.94f, localT), 0.025f);
            }
        }
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;

        var gs = GameState.Instance;
        gs?.ClearFlag(FromBasementFlag, save: false);
        gs?.ClearFlag(BreakerWasOnFlag, save: false);
        AudioManager.Play(Sfx.FlashlightClick, -8f, 0.9f);
        SceneManager.ChangeScene(KitchenCabinetScenePath);
    }

    public override void _ExitTree()
    {
        if (_inputBlockOwned)
        {
            Dialogue.EndInputBlock();
            _inputBlockOwned = false;
        }
        var ui = GameUI.Instance;
        if (ui != null && GodotObject.IsInstanceValid(ui))
            ui.SetMonochromeFlashback(0f);
    }

    // ------------------------------------------------------------ drawing

    private static float Smoothstep(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void BackgroundTransform(RewindShot shot, float localT,
        out Vector2 position, out Vector2 drawnSize)
    {
        float useT = _reduceMotion ? 0.5f : Smoothstep(localT);
        float zoom = Mathf.Lerp(shot.StartZoom, shot.EndZoom, useT);
        Vector2 focus = shot.StartFocus.Lerp(shot.EndFocus, useT);
        if (!_reduceMotion && (shot.Kind == ShotKind.Walk || shot.Kind == ShotKind.Vent))
        {
            focus += new Vector2(
                (float)Math.Sin(_elapsed * 2.1f) * 0.004f,
                (float)Math.Cos(_elapsed * 1.7f) * 0.003f);
        }

        Vector2 textureSize = shot.Texture.GetSize();
        float coverScale = Mathf.Max(
            ScreenSize.X / textureSize.X, ScreenSize.Y / textureSize.Y);
        drawnSize = textureSize * coverScale * zoom;
        position = ScreenCenter - focus * drawnSize;
        // Keep the painting covering the screen even when the focus
        // sits near an edge (the trunk, the vent overhead).
        position = new Vector2(
            Mathf.Clamp(position.X, ScreenSize.X - drawnSize.X, 0f),
            Mathf.Clamp(position.Y, ScreenSize.Y - drawnSize.Y, 0f));
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, ScreenSize), new Color(0.02f, 0.02f, 0.02f), true);
        if (_shots.Count == 0 ||
            !GetShotPosition(out int index, out float localT, out var shot))
            return;

        DrawShot(shot, localT, 1f);
        float remaining = shot.Seconds * (1f - localT);
        if (index + 1 < _shots.Count && remaining < CrossfadeSeconds)
        {
            DrawShot(_shots[index + 1], 0f,
                Mathf.Clamp(1f - remaining / CrossfadeSeconds, 0f, 1f));
        }

        DrawTapeDamage();
        DrawRect(new Rect2(0, 0, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, 1152, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, 648), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawShot(RewindShot shot, float localT, float alpha)
    {
        if (shot?.Texture == null || alpha <= 0.01f)
            return;
        BackgroundTransform(shot, localT, out Vector2 position, out Vector2 drawnSize);
        DrawTextureRect(shot.Texture, new Rect2(position, drawnSize), false,
            new Color(1f, 1f, 1f, alpha));
        if (shot.Kind == ShotKind.Basement)
            DrawBasementReset(position, drawnSize, localT, alpha);
        else if (shot.Kind == ShotKind.Vent)
            DrawVentGrille(position, drawnSize, localT, alpha);
    }

    private void DrawBasementReset(Vector2 bgPos, Vector2 bgSize, float localT, float alpha)
    {
        // The breaker box where Basement.cs draws it, riding the
        // same painting transform: three lamps, then none, and the
        // big handle travelling back down.
        Vector2 boxPos = bgPos + new Vector2(452f / 1152f, 62f / 648f) * bgSize;
        Vector2 boxSize = new Vector2(196f / 1152f, 128f / 648f) * bgSize;
        var box = new Rect2(boxPos, boxSize);
        DrawRect(box, new Color(0.16f, 0.17f, 0.18f, 0.94f * alpha), true);
        DrawRect(box, new Color(0.62f, 0.63f, 0.6f, 0.9f * alpha), false, 2.5f);
        DrawRect(box.Grow(-7f), new Color(0.08f, 0.09f, 0.1f, 0.92f * alpha), true);

        float lampRadius = Mathf.Max(2.5f, boxSize.X * 0.045f);
        for (int i = 0; i < 3; i++)
        {
            Vector2 lamp = boxPos + new Vector2(
                boxSize.X * (0.24f + 0.26f * i), boxSize.Y * 0.3f);
            bool on = _breakerWasOn && localT < 0.22f + 0.15f * i;
            DrawCircle(lamp, lampRadius, on
                ? new Color(0.45f, 1f, 0.55f, 0.95f * alpha)
                : new Color(0.1f, 0.14f, 0.11f, 0.95f * alpha));
        }

        Vector2 leverBase = boxPos + new Vector2(boxSize.X * 0.5f, boxSize.Y * 0.68f);
        float leverT = _breakerWasOn
            ? Smoothstep((localT - 0.5f) / 0.16f)
            : 1f;
        float leverLength = boxSize.Y * 0.24f;
        Vector2 leverEnd = leverBase + new Vector2(
            (float)Math.Sin(leverT * Math.PI) * 0.35f,
            -(float)Math.Cos(leverT * Math.PI)) * leverLength;
        DrawLine(leverBase, leverEnd,
            new Color(0.8f, 0.8f, 0.76f, 0.95f * alpha), Mathf.Max(2f, boxSize.X * 0.02f), true);
        DrawCircle(leverEnd, Mathf.Max(2.5f, boxSize.X * 0.03f),
            new Color(0.32f, 0.3f, 0.28f, 0.98f * alpha));

        // What she had already looked at, un-looked at: the rings
        // her attention left on the kept-under things close down
        // and are gone.
        float scale = (bgSize.X / ScreenSize.X + bgSize.Y / ScreenSize.Y) * 0.5f;
        float ringLeft = 1f - localT;
        for (int i = 0; i < BasementInvestigatedCentres.Length; i++)
        {
            if ((_investigatedMask & (1 << i)) == 0)
                continue;
            Vector2 centre = bgPos + BasementInvestigatedCentres[i] * bgSize;
            float radius = Mathf.Lerp(10f, 54f, ringLeft) * scale;
            float ringAlpha = alpha * Mathf.Clamp(ringLeft * 1.4f, 0f, 1f);
            if (ringAlpha <= 0.02f)
                continue;
            DrawArc(centre, radius, 0f, Mathf.Tau, 40,
                new Color(1f, 0.9f, 0.62f, 0.75f * ringAlpha), 2.5f, true);
            DrawArc(centre, radius * 0.72f, 0f, Mathf.Tau, 32,
                new Color(1f, 0.95f, 0.8f, 0.35f * ringAlpha), 1.5f, true);
        }
    }

    private void DrawVentGrille(Vector2 bgPos, Vector2 bgSize, float localT, float alpha)
    {
        Vector2 topLeft = bgPos + VentOpeningMin * bgSize;
        Vector2 bottomRight = bgPos + VentOpeningMax * bgSize;
        var rect = new Rect2(topLeft, bottomRight - topLeft);
        float scale = rect.Size.Y / 153f;

        // Bent off its frame by three kicks when the shot opens;
        // square in its housing again a third of the way through.
        float unbend = _ductWasOpen
            ? 1f - Smoothstep(localT / 0.34f)
            : 0f;
        rect.Position += new Vector2(28f + 21f * scale, 12f + 12f * scale) * unbend;
        if (!_reduceMotion && unbend > 0.01f)
        {
            rect.Position += new Vector2(
                (float)Math.Sin(_elapsed * 80f) * 1.5f * unbend, 0f);
        }

        DrawRect(rect, new Color(0.05f, 0.055f, 0.06f, 0.98f * alpha), true);
        DrawRect(rect, new Color(0.32f, 0.36f, 0.4f, 0.95f * alpha), false,
            Mathf.Max(1.5f, 4f * scale));
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, Mathf.Max(1.5f, 3f * scale))),
            new Color(0.55f, 0.62f, 0.7f, 0.45f * alpha), true);
        var inner = rect.Grow(-13f * scale);
        if (inner.Size.X <= 2f || inner.Size.Y <= 2f)
            return;
        DrawRect(inner, new Color(0.015f, 0.015f, 0.018f, alpha), true);
        int slats = scale < 0.45f ? 3 : 6;
        float step = inner.Size.Y / (slats + 1f);
        for (int i = 0; i < slats; i++)
        {
            float y = inner.Position.Y + step * (i + 0.55f);
            float barH = Mathf.Max(2f, 5f * scale);
            DrawRect(new Rect2(inner.Position.X + 8f * scale, y,
                    inner.Size.X - 16f * scale, barH),
                new Color(0.22f, 0.25f, 0.28f, 0.98f * alpha), true);
            DrawRect(new Rect2(inner.Position.X + 8f * scale, y + barH,
                    inner.Size.X - 16f * scale, Mathf.Max(1.5f, 4f * scale)),
                new Color(0.03f, 0.03f, 0.03f, alpha), true);
        }
        if (scale > 0.6f)
        {
            var screw = new Color(0.4f, 0.44f, 0.48f, 0.9f * alpha);
            float r = 3f * scale;
            float ix = 7f * scale;
            DrawCircle(rect.Position + new Vector2(ix, ix), r, screw);
            DrawCircle(rect.Position + new Vector2(rect.Size.X - ix, ix), r, screw);
            DrawCircle(rect.Position + new Vector2(ix, rect.Size.Y - ix), r, screw);
            DrawCircle(rect.Position + new Vector2(rect.Size.X - ix, rect.Size.Y - ix), r, screw);
        }
    }

    private void DrawTapeDamage()
    {
        DrawRect(new Rect2(Vector2.Zero, ScreenSize), new Color(0, 0, 0, 0.08f), true);
        if (_reduceMotion)
            return;

        // The tracking bar, climbing the way tape climbs when it is
        // being dragged backwards through the machine.
        float barY = ScreenSize.Y - (_elapsed * 300f) % (ScreenSize.Y + 48f);
        DrawRect(new Rect2(0, barY, ScreenSize.X, 18f), new Color(1, 1, 1, 0.08f), true);

        int frame = (int)(_elapsed * 18f);
        for (int i = 0; i < 5; i++)
        {
            float y = (frame * 73 + i * 149) % 648;
            float h = 3f + (i % 3) * 3f;
            DrawRect(new Rect2(0, y, ScreenSize.X, h), i % 2 == 0
                ? new Color(1, 1, 1, 0.05f)
                : new Color(0, 0, 0, 0.12f), true);
        }
    }
}
