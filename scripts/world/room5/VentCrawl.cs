using System.Threading.Tasks;
using Godot;

/// <summary>
/// The way under the house, at elbow height.
///
/// A first-person crawl through the Room 5 vent: holding W moves
/// Mara forward at elbow height, releasing it stops her where she
/// is, and the flashlight follows the mouse directly with no click or
/// hold. Three short beats stop the crawl for her voice; the faraway
/// lit grille eventually fills the dark. It is latched from the other
/// side, the way her father fitted them — so it does not open. It is
/// kicked: three true kicks, timed as a ring closes on the gold,
/// each one bending the grille further off its frame. A first crawl
/// takes its time. Later crawls remember the route and are brief.
///
/// Stepping back is handled by the ordinary contextual navigation
/// (SceneManager's "Back Out" to Room 5), so this scene only owns the
/// forward journey and the final grille click.
/// </summary>
public partial class VentCrawl : Node2D
{
    private const string BackgroundPath = "res://assets/images/bgs/Vent_Crawl_BG.png";
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string Room5ScenePath = "res://scenes/world/room_5.tscn";
    private const string BasementScenePath = "res://scenes/world/basement.tscn";

    private enum CrawlStage
    {
        Intro,
        Crawling,
        Speaking,
        AtGrate,
        Kicking,
        Leaving,
    }

    private static readonly Vector2 ScreenCenter = new(576, 324);
    private static readonly Rect2 GrateClickRect = new(446, 214, 260, 220);
    private static readonly float[] FirstBeatThresholds = [0.2f, 0.5f, 0.8f];
    private static readonly string[] FirstBeatLines =
    [
        "Dust. Cobwebs. One of my old hair ribbons, somehow. The house keeps its small lost things in the walls.",
        "That dripping — pipes, probably. Probably is doing a lot of work in this house.",
        "Cold air, and under it— apples. Stored apples and stone. There's a room down here nobody ever took me to on purpose.",
    ];

    private Texture2D _background;
    private FontFile _font;
    private Label _promptLabel;
    private CrawlStage _stage = CrawlStage.Intro;
    private bool _repeat;
    private float _duration = 14f;
    private float _elapsed;
    private float _progress;
    private float _crawlSoundTimer;
    private float _gratePromptTimer;
    private float _fade;
    private int _beatIndex;
    private bool _left;
    private Vector2 _beamPosition = ScreenCenter;
    private bool _beamReady;
    private bool _crawlMoving;
    // True while a mid-crawl beat is speaking: her voice rides along
    // now — elbows keep working under the words instead of the duct
    // courteously stopping dead every time she thinks aloud.
    private bool _beatRiding;

    private readonly RandomNumberGenerator _rng = new();
    private int _kicksLanded;
    private float _ringRadius = 168f;
    private float _ringDir = -1f;
    private float _grateShake;
    private int _kickMisses;

    public override void _Ready()
    {
        _background = GD.Load<Texture2D>(BackgroundPath);
        _font = GD.Load<FontFile>(FontPath);
        _repeat = GameState.Instance?.HasFlag("basement_entered") ?? false;
        _duration = _repeat ? 6f : 14f;
        _rng.Randomize();
        BuildPrompt();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void BuildPrompt()
    {
        _promptLabel = new Label
        {
            Text = "Hold W to crawl · mouse aims the light",
            Position = new Vector2(296, 468),
            Size = new Vector2(560, 38),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            ZIndex = 100,
        };
        if (_font != null)
            _promptLabel.AddThemeFontOverride("font", _font);
        _promptLabel.AddThemeFontSizeOverride("font_size", 24);
        _promptLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.91f, 0.78f, 0.96f));
        _promptLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _promptLabel.AddThemeConstantOverride("shadow_offset_x", 2);
        _promptLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(_promptLabel);
    }

    private async Task PlayIntroAsync()
    {
        Dialogue.BeginInputBlock();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!StillHere())
                return;

            var gs = GameState.Instance;
            if (gs != null && !gs.HasFlag("item_flashlight"))
            {
                await Say("No. Not blind. Not in the walls of this house.");
                if (StillHere())
                    SceneManager.ChangeScene(Room5ScenePath);
                return;
            }

            AudioManager.Play(Sfx.FlashlightClick, -5f);
            if (_repeat)
            {
                await Say("Elbows. Knees. Dignity left at the entrance, as before.");
            }
            else
            {
                await SaySequence(
                    "Okay. Elbows. Knees. The glamorous way to travel in one's own childhood home.",
                    "Dad said houses have service ways the way people have veins. You're not meant to travel in either. He'd have opinions about this.");
            }
            if (!StillHere())
                return;

            _stage = CrawlStage.Crawling;
            _crawlSoundTimer = 0.1f;
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_stage != CrawlStage.Kicking || GetTree().Paused || Dialogue.IsShowing ||
            Dialogue.IsInputBlocked)
            return;

        bool kick = false;
        if (@event is InputEventMouseButton mouse && mouse.Pressed &&
            mouse.ButtonIndex == MouseButton.Left)
            kick = true;
        else if (@event is InputEventKey key && key.Pressed && !key.Echo &&
            key.PhysicalKeycode == Key.Space)
            kick = true;

        if (!kick)
            return;
        GetViewport().SetInputAsHandled();
        TryKick();
    }

    // Each kick's gold ring sits at a different diameter — wide,
    // narrower, narrowest — and the travelling ring breathes in and
    // out between its stops. The boot lands when the two meet.
    private float TargetRadius => _kicksLanded switch
    {
        0 => 114f,
        1 => 90f,
        _ => 68f,
    };

    private float KickTolerance => _kicksLanded >= 2 ? 9f : 11f;

    private void TryKick()
    {
        if (Mathf.Abs(_ringRadius - TargetRadius) <= KickTolerance)
        {
            _kicksLanded++;
            _grateShake = 0.35f;
            _ringRadius = 168f;
            _ringDir = -1f;
            AudioManager.Play(Sfx.VentKick, -3f, 0.95f + _kicksLanded * 0.06f);
            if (_kicksLanded >= 3)
            {
                _ = BreakGrateAsync();
            }
            else
            {
                GameState.Instance?.Toast(_kicksLanded == 1
                    ? "\"Once—\" The frame gives a finger's width."
                    : "\"Again—\" Metal shrieks, politely, in the dark.");
            }
        }
        else
        {
            _kickMisses++;
            _grateShake = Mathf.Max(_grateShake, 0.12f);
            AudioManager.Play(Sfx.VentKick, -10f, 0.6f);
            if (_kicksLanded > 0)
            {
                // One bad kick and the frame springs back square:
                // the whole sequence starts again from the first.
                _kicksLanded = 0;
                _ringRadius = 168f;
                _ringDir = -1f;
                GameState.Instance?.Toast(
                    "Off the slats — and the frame springs back square in its housing. From the first kick, then. All three or nothing.");
            }
            else
            {
                GameState.Instance?.Toast(_kickMisses == 1
                    ? "A glance off the slats. Kick as the ring closes on the gold."
                    : "Off the slats again. Wait for the gold.");
            }
        }
        QueueRedraw();
    }

    private async Task PlayGrateBeatAsync()
    {
        _stage = CrawlStage.Speaking;
        Dialogue.BeginInputBlock();
        try
        {
            // A grille that's already been kicked off its frame
            // once never asks the whole performance again: it hangs
            // by the thread she left it, and one shove remembers
            // the way down for both of them.
            if (_repeat)
            {
                await Say("The far grille, hanging by the thread I left it. One shove, old friend. You remember how this goes.");
                if (!StillHere())
                    return;
                AudioManager.Play(Sfx.VentKick, -5f, 0.9f);
                _grateShake = 0.35f;
                _kicksLanded = 3;
                _stage = CrawlStage.Kicking;
                _ = BreakGrateAsync();
                return;
            }

            await Say("The far grille — and its latch is on the other side, of course it is. He fitted them to be worked from the room, not from inside his own walls.");
            if (!StillHere())
                return;
            await Say("No tools for this end, then. Just boots. Sorry, Dad. You'd have had opinions. You'd also have held the light.");
            if (!StillHere())
                return;
            _stage = CrawlStage.Kicking;
            _kicksLanded = 0;
            _ringRadius = 168f;
            _ringDir = -1f;
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task BreakGrateAsync()
    {
        if (_stage != CrawlStage.Kicking)
            return;
        _stage = CrawlStage.Leaving;
        AudioManager.Play(Sfx.VentBreak, -2f);
        AudioManager.Play(Sfx.DrawerOpen, -8f, 0.6f);
        Dialogue.BeginInputBlock();
        try
        {
            await Say(_repeat
                ? "Down again. Mind the last elbow of it."
                : "It gives — all at once, the way ice does. Stone smell, apple smell — and a dark much bigger than the duct.");
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateBeam(dt);

        bool hardBlocked = GetTree().Paused || Dialogue.IsInputBlocked ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());
        bool blocked = hardBlocked || Dialogue.IsShowing;
        bool crawlingish = _stage == CrawlStage.Crawling ||
            (_stage == CrawlStage.Speaking && _beatRiding);
        // Her voice no longer stops her elbows: a beat can be spoken
        // over a held W, and the duct keeps passing under the words.
        _crawlMoving = crawlingish && !hardBlocked &&
            Input.IsPhysicalKeyPressed(Key.W);

        if (crawlingish && !hardBlocked)
        {
            if (_crawlMoving)
            {
                _elapsed += dt;
                _progress = Mathf.Clamp(_elapsed / _duration, 0f, 1f);

                _crawlSoundTimer -= dt;
                if (_crawlSoundTimer <= 0f)
                {
                    _crawlSoundTimer = 3.8f;
                    AudioManager.PlayVaried(Sfx.VentCrawl, -8f, 0.98f, 0.05f);
                }

                if (_stage == CrawlStage.Crawling && ShouldPlayBeat())
                {
                    _stage = CrawlStage.Speaking;
                    _beatRiding = true;
                    _ = PlayBeatAsync();
                }
                else if (!_beatRiding && _progress >= 1f)
                {
                    _gratePromptTimer = 0.35f;
                    AudioManager.Play(Sfx.LockpickTick, -9f, 0.7f);
                    _ = PlayGrateBeatAsync();
                }
            }
            else
            {
                // Releasing W is a full stop: no elapsed drift, no
                // scrape of elbows, and the next press answers at once.
                _crawlSoundTimer = Mathf.Min(_crawlSoundTimer, 0.18f);
            }
        }
        else if (_stage == CrawlStage.AtGrate)
        {
            _gratePromptTimer -= dt;
        }
        else if (_stage == CrawlStage.Kicking && !blocked)
        {
            float speed = 105f + 38f * _kicksLanded;
            _ringRadius += _ringDir * speed * dt;
            if (_ringRadius >= 168f) { _ringRadius = 168f; _ringDir = -1f; }
            else if (_ringRadius <= 38f) { _ringRadius = 38f; _ringDir = 1f; }
            _gratePromptTimer -= dt;
        }
        else if (_stage == CrawlStage.Leaving && !Dialogue.IsShowing && !_left)
        {
            _fade = Mathf.Min(1f, _fade + dt * 1.15f);
            if (_fade >= 1f)
            {
                _left = true;
                GameState.Instance?.SetFlag("basement_entered");
                SceneManager.ChangeScene(BasementScenePath);
            }
        }
        _grateShake = Mathf.Max(0f, _grateShake - dt);

        UpdatePrompt(dt);
        QueueRedraw();
    }

    private void UpdateBeam(float dt)
    {
        Vector2 mouse = GetGlobalMousePosition();
        mouse = new Vector2(
            Mathf.Clamp(mouse.X, 0f, 1152f),
            Mathf.Clamp(mouse.Y, 0f, 648f));
        if (!_beamReady)
        {
            _beamPosition = mouse;
            _beamReady = true;
            return;
        }

        // Fast enough to read as Mara hand on the handle, with just
        // enough travel that the beam sweeps instead of teleporting.
        float weight = 1f - Mathf.Exp(-20f * Mathf.Max(dt, 0f));
        _beamPosition = _beamPosition.Lerp(mouse, Mathf.Clamp(weight, 0f, 1f));
    }

    private void UpdatePrompt(float dt)
    {
        if (_promptLabel == null)
            return;

        string text = _stage == CrawlStage.Kicking
            ? "SPACE / CLICK — KICK as the ring meets the gold"
            : _stage == CrawlStage.AtGrate
                ? "Latched from the other side"
                : "Hold W to crawl · mouse aims the light";
        if (_promptLabel.Text != text)
            _promptLabel.Text = text;

        float target = _stage switch
        {
            CrawlStage.Crawling => _crawlMoving ? 0.38f : 0.95f,
            CrawlStage.AtGrate => _gratePromptTimer <= 0f ? 1f : 0f,
            CrawlStage.Kicking => _gratePromptTimer <= 0f ? 0.95f : 0f,
            _ => 0f,
        };
        float alpha = Mathf.MoveToward(_promptLabel.Modulate.A, target, dt * 2.4f);
        _promptLabel.Modulate = new Color(1, 1, 1, alpha);
    }

    private bool ShouldPlayBeat()
    {
        if (_repeat)
            return _beatIndex == 0 && _progress >= 0.5f;
        return _beatIndex < FirstBeatThresholds.Length &&
            _progress >= FirstBeatThresholds[_beatIndex];
    }

    private async Task PlayBeatAsync()
    {
        // No input block here, on purpose: the words are said over
        // the crawling, not instead of it. Advancing her line by
        // click still works; so does holding W through it.
        _beatRiding = true;
        try
        {
            if (_repeat)
            {
                _beatIndex++;
                await Say("Shorter in memory. It is not shorter in knees.");
            }
            else
            {
                string line = FirstBeatLines[Mathf.Min(_beatIndex, FirstBeatLines.Length - 1)];
                _beatIndex++;
                await Say(line);
            }

            if (!StillHere())
                return;
            _stage = CrawlStage.Crawling;
            _crawlSoundTimer = Mathf.Min(_crawlSoundTimer, 0.25f);
            if (_progress >= 1f)
            {
                _gratePromptTimer = 0.35f;
                _ = PlayGrateBeatAsync();
            }
        }
        finally
        {
            _beatRiding = false;
        }
    }

    private async Task Say(string line)
    {
        await Dialogue.ShowText(this, line, 5.5f, 576f, 324f, fontSize: 24);
    }

    private async Task SaySequence(params string[] lines)
    {
        foreach (string line in lines)
        {
            if (!StillHere())
                return;
            await Say(line);
        }
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    public override void _Draw()
    {
        DrawCrawlView();
        DrawFlashlightDarkness();
        // The far grille is the whole point of the dark: a small lit
        // square from the very first elbow, growing as she nears it.
        DrawFarGrate();
        if (_fade > 0f)
            DrawRect(new Rect2(0, 0, 1152, 648), new Color(0, 0, 0, _fade), true);
    }

    private void DrawCrawlView()
    {
        DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.015f, 0.015f, 0.018f), true);
        if (_background == null)
            return;

        float eased = _progress * _progress * (3f - 2f * _progress);
        float zoom = Mathf.Lerp(1.14f, 2.68f, eased);
        Vector2 textureSize = _background.GetSize();
        float coverScale = Mathf.Max(1152f / textureSize.X, 648f / textureSize.Y);
        Vector2 drawnSize = textureSize * coverScale * zoom;

        Vector2 sway = Vector2.Zero;
        if (!Settings.ReduceMotionOrDefault() && _crawlMoving)
        {
            sway = new Vector2(
                (float)System.Math.Sin(_elapsed * 0.72f) * 0.009f,
                (float)System.Math.Cos(_elapsed * 0.58f) * 0.007f) *
                (1f - _progress * 0.45f);
        }
        Vector2 focus = new(0.5f + sway.X, 0.5f + sway.Y);
        Vector2 position = ScreenCenter - focus * drawnSize;
        DrawTextureRect(_background, new Rect2(position, drawnSize), false);
    }

    private void DrawFlashlightDarkness()
    {
        Vector2 beam = _beamPosition;
        float radius = GameState.Instance?.HasFlag("basement_battery_taken") == true ? 205f : 172f;
        DrawDarkOutside(beam, radius + 58f, new Color(0.004f, 0.004f, 0.006f, 0.62f));
        DrawDarkOutside(beam, radius, new Color(0.003f, 0.003f, 0.005f, 0.78f));
        // Warm falloff, kept to square pixel rings rather than a shader.
        DrawArc(beam, radius * 0.96f, 0f, Mathf.Tau, 56,
            new Color(1f, 0.84f, 0.48f, 0.08f), 12f, true);
        DrawArc(beam, radius * 0.72f, 0f, Mathf.Tau, 48,
            new Color(1f, 0.88f, 0.56f, 0.045f), 18f, true);
    }

    private void DrawDarkOutside(Vector2 beam, float radius, Color dark)
    {

        float top = Mathf.Clamp(beam.Y - radius, 0f, 648f);
        float bottom = Mathf.Clamp(beam.Y + radius, 0f, 648f);
        if (top > 0f)
            DrawRect(new Rect2(0, 0, 1152, top), dark, true);
        if (bottom < 648f)
            DrawRect(new Rect2(0, bottom, 1152, 648f - bottom), dark, true);

        const float stripHeight = 9f;
        for (float y = top; y < bottom; y += stripHeight)
        {
            float height = Mathf.Min(stripHeight + 1f, bottom - y);
            float centreY = y + height * 0.5f;
            float dy = centreY - beam.Y;
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float leftEdge = Mathf.Clamp(beam.X - half, 0f, 1152f);
            float rightEdge = Mathf.Clamp(beam.X + half, 0f, 1152f);
            if (leftEdge > 0f)
                DrawRect(new Rect2(0, y, leftEdge, height), dark, true);
            if (rightEdge < 1152f)
                DrawRect(new Rect2(rightEdge, y, 1152f - rightEdge, height), dark, true);
        }

    }

    private void DrawFarGrate()
    {
        // Full size at her boots; a lit thumbnail at the duct's far
        // end while she's still crawling toward it.
        bool atGrate = _stage == CrawlStage.AtGrate || _stage == CrawlStage.Kicking ||
            _stage == CrawlStage.Leaving || _progress >= 1f;
        float eased = _progress * _progress * (3f - 2f * _progress);
        float scale = atGrate ? 1f : Mathf.Lerp(0.2f, 1f, eased);
        Vector2 size = new Vector2(166, 156) * scale;
        var rect = new Rect2(ScreenCenter - size * 0.5f, size);
        // Every true kick bends it further off its frame.
        rect.Position += new Vector2(7f * _kicksLanded, 4f * _kicksLanded) * scale;
        if (_grateShake > 0.01f && !Settings.ReduceMotionOrDefault())
        {
            float s = 3.4f * (_grateShake / 0.35f);
            rect.Position += new Vector2(
                _rng.RandfRange(-s, s), _rng.RandfRange(-s * 0.6f, s * 0.6f));
        }
        if (_stage == CrawlStage.Leaving)
            rect.Position += new Vector2(28, 12);
        // A halo of the cellar's thin light around it, so the far
        // end is findable even where her own beam isn't pointing.
        DrawRect(rect.Grow(7f), new Color(1f, 0.84f, 0.5f, 0.1f), true);
        DrawRect(rect, new Color(0.1f, 0.09f, 0.07f, 0.98f), true);
        DrawRect(rect, new Color(0.78f, 0.66f, 0.42f, 0.95f), false, Mathf.Max(2f, 4f * scale));
        var inner = rect.Grow(-13f * scale);
        DrawRect(inner, new Color(0.23f, 0.2f, 0.15f, 1f), true);
        int slats = scale < 0.45f ? 3 : 6;
        float step = inner.Size.Y / (slats + 1f);
        for (int i = 0; i < slats; i++)
        {
            float y = inner.Position.Y + step * (i + 0.55f);
            float barH = Mathf.Max(2f, 5f * scale);
            DrawRect(new Rect2(inner.Position.X + 8f * scale, y,
                    inner.Size.X - 16f * scale, barH),
                new Color(0.86f, 0.72f, 0.42f, 0.95f), true);
            DrawRect(new Rect2(inner.Position.X + 8f * scale, y + barH,
                    inner.Size.X - 16f * scale, Mathf.Max(1.5f, 4f * scale)),
                new Color(0.03f, 0.03f, 0.03f, 1f), true);
        }

        if (_stage == CrawlStage.Kicking)
        {
            // The closing ring and the gold it has to meet.
            Vector2 center = new(576, 324);
            DrawArc(center, TargetRadius, 0f, Mathf.Tau, 56,
                new Color(1f, 0.84f, 0.45f, 0.85f), 3f, true);
            DrawArc(center, _ringRadius, 0f, Mathf.Tau, 56,
                new Color(0.88f, 0.86f, 0.8f, 0.9f), 3f, true);
            for (int i = 0; i < 3; i++)
            {
                Vector2 pip = center + new Vector2((i - 1) * 26f, 176f);
                DrawCircle(pip, 6f, i < _kicksLanded
                    ? new Color(1f, 0.84f, 0.45f, 0.98f)
                    : new Color(0.9f, 0.84f, 0.68f, 0.3f));
            }
        }
    }
}
