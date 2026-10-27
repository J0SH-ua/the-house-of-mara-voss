using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Beyond the mended bridge, the memory runs out of road.
///
/// The first crossing ends with the light going and Mara's mind
/// following it. She wakes at night inside box-hedge walls too
/// high to climb, with her father's flashlight still in the Bag
/// and no idea which green corridor was meant to be a road.
///
/// This first version is deliberately only the explorable place:
/// a chunky first-person hedge maze. Her body keeps the house's
/// own manners. W and S walk at the same unhurried pace as every
/// walked journey (footfalls 0.92 s apart on the dirt road); A and
/// D turn her at one steady rate that stops the instant the key is
/// let go, so the view can never drift. The mouse is her wrist on
/// Father's flashlight: the same circle of light, with the same
/// chunky dark around it, that the vent, the basement and the
/// blackout use.
///
/// The torch is a sprite of its own now - both hands wrapped round
/// the cloth-bound head of it, drawn in the same pixel art as
/// every other thing in this game (assets/images/core/
/// hand_flashlight.png) - held low in the bottom-left corner with
/// the sleeve of her jumper running off the edge, sliding left and
/// right along the bottom as she points, but never turning in her
/// hands. The world it lights is painted too: the night sky, the
/// grass she walks on, the box-hedge walls and the stone pillars
/// are all projected from the same tiles the garden outside is
/// drawn from (assets/images/hedge/), and the torchlight falls on
/// them in flat bands of light and shade, the way the rest of the
/// house is painted, so the maze finally looks like the house it
/// belongs to instead of like coloured rectangles.
///
/// Crouching and jumping are hers again, and the camera goes with
/// them: a crouch drops her eye, so the hedge tops lean in and
/// less ground shows under her boots; a jump lifts her, so the
/// whole maze falls away and comes back. Looking behind her is a
/// thing she *holds*, not a thing she taps: keep Caps Lock down
/// and the view turns all the way round at a calm, even rate,
/// and lets go and it turns back. It is only her *view* that
/// turns: her body keeps its heading, so she walks on the way she
/// was going while she looks over her shoulder.
///
/// The south break returns to the bridge and the far north-west
/// break continues to the last room, so exploring it can never
/// strand a run while its future puzzles are still being thought
/// about.
/// </summary>
public partial class HedgeMaze : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    // The torch she actually carries in here: both hands round the
    // cloth-bound head, as one sprite, in the house's own pixel art.
    private const string TorchHandsTexturePath = "res://assets/images/core/hand_flashlight.png";
    private const string HedgeWallTexturePath = "res://assets/images/hedge/hedge_wall.png";
    private const string HedgeBaseTexturePath = "res://assets/images/hedge/hedge_base.png";
    private const string NightSkyTexturePath = "res://assets/images/hedge/night_sky.png";
    private const string GrassTexturePath = "res://assets/images/hedge/grass_ground.png";
    private const string StoneTexturePath = "res://assets/images/hedge/stone_tiles.png";
    private const string BridgeScenePath = "res://scenes/world/bridge_repair.tscn";
    private const string Room7ScenePath = "res://scenes/world/room_7.tscn";

    public const string IntroPendingFlag = "hedge_maze_intro_pending";
    public const string AwakenedFlag = "hedge_maze_awakened";
    public const string FarSideReachedFlag = "hedge_maze_far_side_reached";

    private const float ScreenWidth = 1152f;
    private const float ScreenHeight = 648f;

    // The horizon sits still, but Mara's eye does not: a crouch
    // drops it and a jump lifts it, so the maze leans in over her
    // and falls away under her. Heights are in the same world units
    // the maze grid uses.
    private const float Horizon = 324f;
    private const float ColumnWidth = 8f;
    private const int ColumnCount = 144;
    private const float FieldOfView = 1.08f;
    private const float ProjectionScale = 680f;
    private const float BaseEyeHeight = 0.37f;
    private const float CrouchEyeDrop = 0.16f;
    private const float LandingEyeDip = 0.055f;
    private const float EyeHeightResponse = 15f;
    private const float SkyBand = 24f;
    private const float PlayerRadius = 0.23f;

    // How tall each kind of wall is, measured from the path. A wall
    // is drawn as everything that rises above her eye, so dropping
    // her eye makes the hedges lean in and lifting it lets them
    // fall away again.
    private const float HedgeWallHeight = 1.65f;
    private const float PillarHeight = 1.45f;
    private const float FountainHeight = 0.85f;
    private const float TopiaryHeight = 1.79f;

    // How much of each painted tile covers one world unit. The tiles
    // are mirrored 2x2 by tools/build_hedge_art.py, so they repeat
    // forever without a seam.
    private const float HedgeTexScale = 150f;
    private const float StoneTexScale = 165f;
    private const float GrassTexScale = 132f;
    private const float SkyPixelsPerRadian = 118f;
    private const float HedgeBaseHeight = 0.16f;

    // Walking pace. Every walked journey sets its footfalls about
    // 0.92 s apart on the dirt road; the maze uses that cadence
    // and a slow ground speed to match it.
    private const float WalkSpeed = 0.55f;
    private const float RunSpeed = 1.25f;
    private const float CrouchSpeed = 0.28f;
    private const float BackwardFactor = 0.7f;
    private const float LookingBackFactor = 0.82f;
    private const float MovementResponse = 14f;
    private const float WalkStepSeconds = 0.92f;
    private const float RunStepSeconds = 0.5f;
    private const float CrouchStepSeconds = 1.25f;

    // Turning is a constant angular speed with no inertia: holding
    // A/D turns, releasing stops on that frame.
    private const float TurnSpeed = 1.3f;

    // Looking behind her is a hold, and it turns at one even rate:
    // a whole half-turn takes a little over half a second.
    private const float LookBackTurnRate = 1.7f;

    // Jump and crouch are Mara's body, shown through her hand and
    // through the camera that follows it. A jump lifts the eye by a
    // little less than she actually rises, so the maze falls away
    // without the whole world turning into a floor.
    private const float JumpVelocity = 3.4f;
    private const float JumpGravity = 11f;
    private const float JumpEyeScale = 0.55f;

    // The flashlight circle, exactly the basement's (the stronger
    // battery from older saves widens it the same way).
    private const float BeamRadius = 158f;
    private const float StrongBeamRadius = 208f;

    // The torch she carries in here is one sprite: both hands round
    // the cloth-bound head of it, with the sleeve of her jumper
    // running off the bottom-left corner of the frame. The ember of
    // the torch and the place her hands hold it are fixed points
    // inside that sprite (measured by tools/build_hedge_art.py).
    // It is drawn as big as the house draws its held hand elsewhere,
    // and it does not turn: it slides along the bottom of the frame,
    // left and right, with where she is pointing.
    private const float TorchSpriteWidth = 320f;
    private const float TorchRestLeft = 0f;
    private const float TorchSlideRange = 44f;
    private const float TorchRiseDistance = 430f;
    private static readonly Vector2 TorchSpriteGrip = new(0.4349f, 0.4884f);
    private static readonly Vector2 TorchSpriteLens = new(0.8368f, 0.1681f);

    // # hedge, P stone pillar, F dry fountain, T topiary,
    // S south waking place, B bridge opening, X far opening.
    private static readonly string[] MapRows =
    [
        "###X#####################",
        "#.......................#",
        "#####.###P#####.#.#####.#",
        "#...#.......#...........#",
        "#.#.#.#####.#.##.#...##.#",
        "#.#.#.........#....T..#.#",
        "#.#.#.#.##..#.#.#.....#.#",
        "#.#...#.#...#.#.....#.#.#",
        "#.###.#.#.....#.###.#.###",
        "#...#...#..F....#.#.....#",
        "#.#.######...####.P.#.#.#",
        "#.........#.#.....#.#.#.#",
        "#.##...##.#.#.#.#.#.###.#",
        "#....T....#.....#.#.#...#",
        "#.#.....###.#.#.###.#P#.#",
        "#...#.#...#.....#.......#",
        "#.#P#.###.#####.#.#####.#",
        "#............S#.........#",
        "#############B###########",
    ];

    private struct RayHit
    {
        public bool Hit;
        public float Distance;
        public Vector2I Cell;
        public char Tile;
        public int Side;
        public float WallX;
    }

    private static bool _pendingFromNorth;

    /// <summary>SceneManager arms the physical end Mara is entering
    /// from immediately before a scene change. Consumed in _Ready.</summary>
    public static void ArmEntry(bool fromNorth) => _pendingFromNorth = fromNorth;

    private FontFile _font;
    private Texture2D _torchHandsTexture;
    private Texture2D _hedgeTexture;
    private Texture2D _hedgeBaseTexture;
    private Texture2D _skyTexture;
    private Texture2D _grassTexture;
    private Texture2D _stoneTexture;
    private float _torchSpriteHeight = TorchSpriteWidth;
    private Label _titleLabel;
    private Label _promptLabel;
    private Label _exitLabel;

    private Vector2 _playerPosition = new(13.5f, 17.5f);
    private Vector2 _velocity = Vector2.Zero;
    private float _yaw = Mathf.Pi;
    private float _lookBackAmount;
    private bool _lookBackHeld;
    private bool _moving;
    private bool _running;

    private float _crouchAmount;
    private float _jumpHeight;
    private float _verticalVelocity;
    private bool _grounded = true;
    private float _landDip;
    private float _eyeHeight = BaseEyeHeight;
    private float _stepPhase;
    private bool _stepping;
    private float _bobAmount;
    private float _bobAmpX;
    private float _bobAmpY;
    private bool _footstepAlternate;

    private Vector2 _beamPosition = new(576f, 324f);
    private bool _beamReady;
    private float _handRise = 1f;
    private Tween _handTween;

    private float _eyeOpen = 1f;
    private Tween _eyeTween;
    private bool _exploring;
    private bool _leaving;
    private bool _exitArmed = true;
    private bool _playWakeIntro;
    private bool _hasFlashlight;
    private bool _flashlightOn;

    private Vector2I _startCell = new(13, 17);
    private Vector2I _southExitCell = new(13, 18);
    private Vector2I _northExitCell = new(3, 0);
    private readonly float[] _depthByColumn = new float[ColumnCount];
    private readonly float[] _shortDepthByColumn = new float[ColumnCount];
    private readonly float[] _skyLimitByColumn = new float[ColumnCount];
    private readonly float[] _rayAngleByColumn = new float[ColumnCount];
    private float _drawViewYaw;

    /// <summary>How wide the world is at a depth of one: the number
    /// of pixels one world unit covers across the screen.</summary>
    private static readonly float FocalLength = ScreenWidth * 0.5f / Mathf.Tan(FieldOfView * 0.5f);
    private string _exitHintCache = string.Empty;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        _torchHandsTexture = GD.Load<Texture2D>(TorchHandsTexturePath);
        _hedgeTexture = GD.Load<Texture2D>(HedgeWallTexturePath);
        _hedgeBaseTexture = GD.Load<Texture2D>(HedgeBaseTexturePath);
        _skyTexture = GD.Load<Texture2D>(NightSkyTexturePath);
        _grassTexture = GD.Load<Texture2D>(GrassTexturePath);
        _stoneTexture = GD.Load<Texture2D>(StoneTexturePath);
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        // The painted tiles are mirrored 2x2 and repeat across the
        // maze, so they have to be allowed to run past their edges.
        TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled;

        if (_torchHandsTexture != null && _torchHandsTexture.GetWidth() > 0)
        {
            _torchSpriteHeight = TorchSpriteWidth *
                ((float)_torchHandsTexture.GetHeight() / _torchHandsTexture.GetWidth());
        }

        bool fromNorth = _pendingFromNorth;
        _pendingFromNorth = false;

        FindSpecialCells();
        PlaceAtEntry(fromNorth);

        var gs = GameState.Instance;
        _playWakeIntro = gs?.HasFlag(IntroPendingFlag) ?? false;
        _hasFlashlight = gs?.HasFlag("item_flashlight") ?? false;
        _flashlightOn = !_playWakeIntro && _hasFlashlight;
        _eyeOpen = _playWakeIntro ? 0f : 1f;
        _handRise = 1f;

        for (int i = 0; i < ColumnCount; i++)
        {
            _depthByColumn[i] = 40f;
            _shortDepthByColumn[i] = float.MaxValue;
            _skyLimitByColumn[i] = Horizon;
        }

        BuildLabels();
        QueueRedraw();
        _ = PlayIntroAsync();
    }

    private void FindSpecialCells()
    {
        for (int y = 0; y < MapRows.Length; y++)
        {
            string row = MapRows[y];
            for (int x = 0; x < row.Length; x++)
            {
                char tile = row[x];
                if (tile == 'S')
                    _startCell = new Vector2I(x, y);
                else if (tile == 'B')
                    _southExitCell = new Vector2I(x, y);
                else if (tile == 'X')
                    _northExitCell = new Vector2I(x, y);
            }
        }
    }

    private void PlaceAtEntry(bool fromNorth)
    {
        if (fromNorth)
        {
            _playerPosition = new Vector2(_northExitCell.X + 0.5f, _northExitCell.Y + 1.5f);
            _yaw = 0f; // east, along the open northern gallery
        }
        else
        {
            _playerPosition = new Vector2(_startCell.X + 0.5f, _startCell.Y + 0.5f);
            _yaw = Mathf.Pi; // west, along the corridor she wakes beside
        }
        _velocity = Vector2.Zero;
        _lookBackAmount = 0f;
        _lookBackHeld = false;
        _exitArmed = true;
    }

    private void BuildLabels()
    {
        // Bottom-right: her left hand owns the bottom-left corner, and
        // the HUD's objective and status chips own the top.
        _exitLabel = MakeLabel(string.Empty,
            new Vector2(560, 498), new Vector2(568, 30), 20,
            new Color(0.96f, 0.9f, 0.72f, 0.95f), HorizontalAlignment.Right);
        _titleLabel = MakeLabel("Inside the green walls",
            new Vector2(560, 530), new Vector2(568, 34), 25,
            new Color(0.88f, 0.94f, 0.82f, 0.95f), HorizontalAlignment.Right);
        _promptLabel = MakeLabel(
            "W/S walk · A/D turn · mouse aims the torch\nSHIFT run · C crouch · SPACE jump · HOLD CAPS LOCK to look back",
            new Vector2(480, 566), new Vector2(648, 56), 17,
            new Color(0.92f, 0.89f, 0.78f, 0.92f), HorizontalAlignment.Right);

        bool labelsVisible = !_playWakeIntro;
        _exitLabel.Visible = labelsVisible;
        _titleLabel.Visible = labelsVisible;
        _promptLabel.Visible = labelsVisible;
    }

    private Label MakeLabel(string text, Vector2 position, Vector2 size, int fontSize,
        Color color, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            Size = size,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,
            // The maze node draws its pixel-art hand and torch with
            // nearest filtering; the words keep ordinary filtering.
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(label);
        return label;
    }

    private async Task PlayIntroAsync()
    {
        Dialogue.BeginInputBlock();
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!StillHere())
                return;

            if (_playWakeIntro)
            {
                AudioManager.Play(Sfx.BreathSlow, -13f, 0.9f);
                await WakeEyelidsAsync();
                if (!StillHere())
                    return;

                if (_exitLabel != null)
                    _exitLabel.Visible = true;
                if (_titleLabel != null)
                    _titleLabel.Visible = true;
                if (_promptLabel != null)
                    _promptLabel.Visible = true;

                var gs = GameState.Instance;
                if (gs != null)
                {
                    gs.ClearFlag(IntroPendingFlag, save: false);
                    gs.SetFlag(AwakenedFlag, save: false);
                    gs.SaveGame("Mara woke inside the hedge walls");
                }

                await Say("—cold. Grass. My cheek is wet. How long was I down?");
                if (!StillHere())
                    return;
                await Say("Night. Not evening on its way. Night, already here.");
                if (!StillHere())
                    return;

                if (_hasFlashlight)
                {
                    AudioManager.Play(Sfx.FlashlightClick, -5f);
                    _flashlightOn = true;
                    RaiseHand();
                    QueueRedraw();
                    await WaitSeconds(0.45f);
                    if (!StillHere())
                        return;
                    await Say("Dad's flashlight. Still with me. Good. At least the dark has to share.");
                    if (!StillHere())
                        return;
                }
                else
                {
                    await Say("No flashlight in my bag. Moonlight, then. And whatever my eyes can learn from it.");
                    if (!StillHere())
                        return;
                }

                await Say("Walls. Green walls, cut square. Box hedge, higher than my hands can reach. Where did the road go?");
                if (!StillHere())
                    return;
                await Say("I don't know where I am. So I walk. I learn it the way I've learned every room in that house.");
                if (!StillHere())
                    return;
            }
            else if (_hasFlashlight)
            {
                AudioManager.Play(Sfx.FlashlightClick, -7f);
                RaiseHand();
            }

            _exploring = true;
            QueueRedraw();
        }
        finally
        {
            Dialogue.EndInputBlock();
        }
    }

    private async Task WakeEyelidsAsync()
    {
        if (Settings.ReduceMotionOrDefault())
        {
            await BlinkToAsync(1f, 0.9f);
            return;
        }

        await WaitSeconds(0.7f);
        if (!StillHere())
            return;
        await BlinkToAsync(0.25f, 0.28f);
        await WaitSeconds(0.18f);
        if (!StillHere())
            return;
        await BlinkToAsync(0.02f, 0.22f);
        await WaitSeconds(0.25f);
        if (!StillHere())
            return;
        await BlinkToAsync(0.55f, 0.3f);
        await WaitSeconds(0.2f);
        if (!StillHere())
            return;
        await BlinkToAsync(0.1f, 0.2f);
        await WaitSeconds(0.3f);
        if (!StillHere())
            return;
        await BlinkToAsync(1f, 0.45f);
    }

    private async Task BlinkToAsync(float openFraction, float seconds)
    {
        if (!StillHere())
            return;
        float target = Mathf.Clamp(openFraction, 0f, 1f);
        _eyeTween?.Kill();
        _eyeTween = CreateTween();
        _eyeTween.TweenMethod(Callable.From<float>(SetEyeOpen), _eyeOpen, target, seconds);
        await ToSignal(_eyeTween, Tween.SignalName.Finished);
    }

    private void SetEyeOpen(float openFraction)
    {
        _eyeOpen = Mathf.Clamp(openFraction, 0f, 1f);
        QueueRedraw();
    }

    /// <summary>The same entrance the held-item hand makes everywhere
    /// else: it rises into frame from below over 0.65 s, eased out.</summary>
    private void RaiseHand()
    {
        if (!_hasFlashlight)
            return;
        _handTween?.Kill();
        if (Settings.ReduceMotionOrDefault())
        {
            SetHandRise(0f);
            return;
        }
        _handTween = CreateTween();
        _handTween.TweenMethod(Callable.From<float>(SetHandRise), _handRise, 0f, 0.65f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    private void SetHandRise(float value)
    {
        _handRise = Mathf.Clamp(value, 0f, 1f);
        QueueRedraw();
    }

    // ------------------------------------------------------------ input

    public override void _Input(InputEvent @event)
    {
        // Do not steal WASD/C/Space from the pause menu, Bag,
        // Journal notes or any other panel opened over the maze.
        if (GetTree().Paused ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen()))
            return;
        if (@event is not InputEventKey keyEvent)
            return;

        bool capsLock = keyEvent.PhysicalKeycode == Key.Capslock ||
            keyEvent.Keycode == Key.Capslock;
        bool shift = keyEvent.PhysicalKeycode == Key.Shift ||
            keyEvent.Keycode == Key.Shift;
        bool movementKey = capsLock || shift ||
            keyEvent.PhysicalKeycode is
                Key.W or Key.A or Key.S or Key.D or Key.C or Key.Space or
                Key.Left or Key.Right or Key.Up or Key.Down;
        if (!movementKey)
            return;

        // Looking behind her is a hold, not a tap, so there is nothing
        // to do on the press of Caps Lock: the key is claimed here and
        // the turn itself is read every frame in UpdateLookBack.
        if (keyEvent.PhysicalKeycode == Key.Space && keyEvent.Pressed &&
            !keyEvent.Echo && InputFree())
        {
            TryJump();
        }

        // Space still belongs to a line of dialogue while one is on
        // screen. Every other movement key is claimed here so A
        // cannot arm Anchor and C cannot toggle Clarity under her.
        if (!(keyEvent.PhysicalKeycode == Key.Space && Dialogue.IsShowing))
            GetViewport().SetInputAsHandled();
    }

    private bool InputFree() =>
        _exploring && !_leaving && !GetTree().Paused && !SceneManager.IsChanging &&
        !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

    private void TryJump()
    {
        if (!_grounded || _crouchAmount > 0.5f)
            return;
        _grounded = false;
        _verticalVelocity = JumpVelocity;
        AudioManager.PlayVaried(Sfx.FootstepDirtB, -15f, 0.84f, 0.03f);
    }

    // ------------------------------------------------------------ simulation

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);
        bool free = InputFree();

        UpdateBeam(dt);
        UpdateCrouch(dt, free);
        UpdateJump(dt);
        UpdateLookBack(dt, free);
        UpdateEyeHeight(dt);
        if (free)
        {
            UpdateTurning(dt);
            UpdateMovement(dt);
            CheckExit();
        }
        else
        {
            // A dialogue line, a panel or a scene change is a full stop.
            _velocity = Vector2.Zero;
            _moving = false;
            _running = false;
        }

        UpdateGait(dt);
        UpdateLabels(dt);
        QueueRedraw();
    }

    /// <summary>The beam is Mara's hand on the torch: it follows the
    /// mouse directly, smoothed exactly as the basement, the vent and
    /// the blackout smooth theirs.</summary>
    private void UpdateBeam(float dt)
    {
        Vector2 mouse = GetGlobalMousePosition();
        mouse = new Vector2(
            Mathf.Clamp(mouse.X, 0f, ScreenWidth),
            Mathf.Clamp(mouse.Y, 0f, ScreenHeight));
        if (!_beamReady)
        {
            _beamPosition = mouse;
            _beamReady = true;
            return;
        }

        float weight = 1f - Mathf.Exp(-20f * Mathf.Max(dt, 0f));
        _beamPosition = _beamPosition.Lerp(mouse, Mathf.Clamp(weight, 0f, 1f));
    }

    private void UpdateCrouch(float dt, bool free)
    {
        bool wantsCrouch = free && Input.IsPhysicalKeyPressed(Key.C);
        _crouchAmount = Mathf.MoveToward(_crouchAmount, wantsCrouch ? 1f : 0f, dt * 6f);
    }

    /// <summary>Looking behind her is something she holds, not
    /// something she taps. Keep the key down and the view turns all
    /// the way round at one calm rate; let it go and the view comes
    /// back to the way she is facing.</summary>
    private void UpdateLookBack(float dt, bool free)
    {
        bool held = free && Input.IsPhysicalKeyPressed(Key.Capslock);
        if (held && !_lookBackHeld)
            AudioManager.Play(Sfx.PageTurn, -15f, 0.82f);
        _lookBackHeld = held;

        float target = held ? 1f : 0f;
        _lookBackAmount = Mathf.MoveToward(_lookBackAmount, target, dt * LookBackTurnRate);
    }

    /// <summary>The camera goes with her body. A crouch drops her
    /// eye, so the hedges lean in over her and the path comes up to
    /// meet her; a jump lifts her, so the whole maze falls away and
    /// settles back when she lands.</summary>
    private void UpdateEyeHeight(float dt)
    {
        float target = BaseEyeHeight
            - _crouchAmount * CrouchEyeDrop
            + _jumpHeight * JumpEyeScale
            + _landDip * LandingEyeDip;
        float weight = Mathf.Clamp(1f - Mathf.Exp(-EyeHeightResponse * dt), 0f, 1f);
        _eyeHeight = Mathf.Lerp(_eyeHeight, target, weight);
    }

    private void UpdateJump(float dt)
    {
        if (!_grounded)
        {
            _verticalVelocity -= JumpGravity * dt;
            _jumpHeight += _verticalVelocity * dt;
            if (_jumpHeight <= 0f)
            {
                _jumpHeight = 0f;
                _verticalVelocity = 0f;
                _grounded = true;
                _landDip = 1f;
                AudioManager.PlayVaried(Sfx.FootstepDirtA, -12f, 0.8f, 0.03f);
            }
        }
        _landDip = Mathf.MoveToward(_landDip, 0f, dt / 0.3f);
    }

    private void UpdateTurning(float dt)
    {
        float turn = 0f;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))
            turn -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right))
            turn += 1f;
        if (turn != 0f)
            _yaw = NormalizeAngle(_yaw + turn * TurnSpeed * dt);
    }

    private void UpdateMovement(float dt)
    {
        float forward = 0f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            forward += 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            forward -= 1f;

        bool crouching = _crouchAmount >= 0.45f;
        _running = !crouching && forward > 0f && Input.IsKeyPressed(Key.Shift);
        float speed = crouching ? CrouchSpeed : _running ? RunSpeed : WalkSpeed;
        if (forward < 0f)
            speed *= BackwardFactor;
        if (_lookBackAmount > 0.5f)
            speed *= LookingBackFactor;

        // She keeps walking the way she is facing. Turning to look
        // behind her does not turn her body: the view swings round
        // and she carries on forwards, which is the whole point of
        // looking back.
        float moveYaw = _yaw;
        Vector2 facing = new(Mathf.Cos(moveYaw), Mathf.Sin(moveYaw));
        Vector2 desired = facing * forward * speed;
        float weight = Mathf.Clamp(1f - Mathf.Exp(-MovementResponse * dt), 0f, 1f);
        _velocity = _velocity.Lerp(desired, weight);
        if (forward == 0f && _velocity.LengthSquared() < 0.0004f)
            _velocity = Vector2.Zero;

        Vector2 candidateX = _playerPosition + new Vector2(_velocity.X * dt, 0f);
        if (CanStand(candidateX))
            _playerPosition.X = candidateX.X;
        else
            _velocity.X = 0f;

        Vector2 candidateY = _playerPosition + new Vector2(0f, _velocity.Y * dt);
        if (CanStand(candidateY))
            _playerPosition.Y = candidateY.Y;
        else
            _velocity.Y = 0f;

        _moving = forward != 0f && _velocity.Length() > 0.05f;
    }

    /// <summary>Footfalls on the journeys' cadence, and the hand's
    /// sway riding the same steps.</summary>
    private void UpdateGait(float dt)
    {
        bool crouching = _crouchAmount >= 0.45f;
        float stepSeconds = crouching ? CrouchStepSeconds : _running ? RunStepSeconds : WalkStepSeconds;
        float targetAmpY = crouching ? 2.5f : _running ? 9f : 5f;
        float targetAmpX = crouching ? 2f : _running ? 7f : 4f;
        _bobAmpY = Mathf.MoveToward(_bobAmpY, targetAmpY, dt * 24f);
        _bobAmpX = Mathf.MoveToward(_bobAmpX, targetAmpX, dt * 24f);

        if (_moving && _grounded)
        {
            if (!_stepping)
            {
                _stepping = true;
                if (_bobAmount < 0.2f)
                    _stepPhase = Mathf.Floor(_stepPhase) + 0.55f;
            }

            float before = _stepPhase;
            _stepPhase += dt / stepSeconds;
            if (Mathf.Floor(_stepPhase) > Mathf.Floor(before))
                PlayFootstep(crouching);
            _bobAmount = Mathf.MoveToward(_bobAmount, 1f, dt * 4f);
        }
        else
        {
            _stepping = false;
            _bobAmount = Mathf.MoveToward(_bobAmount, 0f, dt * 3f);
        }
    }

    private void PlayFootstep(bool crouching)
    {
        _footstepAlternate = !_footstepAlternate;
        AudioManager.PlayVaried(
            _footstepAlternate ? Sfx.FootstepDirtA : Sfx.FootstepDirtB,
            crouching ? -19f : _running ? -9.5f : -12f,
            crouching ? 0.86f : _running ? 1.06f : 0.97f,
            0.035f);
    }

    private void UpdateLabels(float dt)
    {
        UpdateExitHint();
        if (_promptLabel == null)
            return;

        // The same courtesy the walked journeys give "Hold W to walk":
        // dim while she is moving, back to full while she stands.
        float target = _moving ? 0.4f : 0.95f;
        var modulate = _promptLabel.Modulate;
        modulate.A = Mathf.MoveToward(modulate.A, target, dt * 2.2f);
        _promptLabel.Modulate = modulate;
    }

    private bool CanStand(Vector2 position)
    {
        for (int ix = -1; ix <= 1; ix += 2)
        {
            for (int iy = -1; iy <= 1; iy += 2)
            {
                Vector2 corner = position + new Vector2(ix * PlayerRadius, iy * PlayerRadius);
                var cell = new Vector2I(Mathf.FloorToInt(corner.X), Mathf.FloorToInt(corner.Y));
                if (IsSolid(CellAt(cell)))
                    return false;
            }
        }
        return true;
    }

    private void CheckExit()
    {
        var cell = new Vector2I(
            Mathf.FloorToInt(_playerPosition.X),
            Mathf.FloorToInt(_playerPosition.Y));
        char tile = CellAt(cell);
        if (tile != 'B' && tile != 'X')
        {
            _exitArmed = true;
            return;
        }
        if (!_exitArmed || _leaving)
            return;

        _exitArmed = false;
        if (tile == 'X')
        {
            var gs = GameState.Instance;
            if (gs != null && !gs.HasFlag(FarSideReachedFlag))
            {
                gs.SetFlag(FarSideReachedFlag, save: false);
                gs.SaveGame("Mara reached the far side of the hedge walls");
            }
            _ = LeaveToAsync(Room7ScenePath);
        }
        else
        {
            _ = LeaveToAsync(BridgeScenePath);
        }
    }

    private async Task LeaveToAsync(string scenePath)
    {
        if (_leaving)
            return;
        _leaving = true;
        _exploring = false;
        AudioManager.PlayVaried(Sfx.FootstepDirtA, -11f, 0.94f, 0.03f);
        SceneManager.ChangeScene(scenePath);

        await WaitSeconds(0.85f);
        if (StillHere() && _leaving && !SceneManager.IsChanging)
        {
            // A deferred change can be answered "not yet" by the
            // house's ordinary leave prompt. The opening stays
            // disarmed until Mara steps off it and returns, so
            // declining that prompt cannot reopen it every frame.
            _leaving = false;
            _exploring = true;
        }
    }

    private void UpdateExitHint()
    {
        if (_exitLabel == null)
            return;

        Vector2 south = new(_southExitCell.X + 0.5f, _southExitCell.Y + 0.5f);
        Vector2 north = new(_northExitCell.X + 0.5f, _northExitCell.Y + 0.5f);
        string text = string.Empty;
        if (_playerPosition.DistanceTo(south) < 2.25f)
            text = "The opening by the waking place — back toward the bridge";
        else if (_playerPosition.DistanceTo(north) < 2.45f)
            text = "A break in the hedge — the far side";
        else if (_lookBackAmount > 0.5f)
            text = "Looking behind her";
        else if (_crouchAmount >= 0.45f)
            text = "Crouching";

        if (text != _exitHintCache)
        {
            _exitHintCache = text;
            _exitLabel.Text = text;
        }
    }

    // ------------------------------------------------------------ world

    private static char CellAt(Vector2I cell)
    {
        if (cell.Y < 0 || cell.Y >= MapRows.Length)
            return '#';
        string row = MapRows[cell.Y];
        if (cell.X < 0 || cell.X >= row.Length)
            return '#';
        return row[cell.X];
    }

    private static bool IsSolid(char tile) => tile is '#' or 'P' or 'F' or 'T';

    /// <summary>One grid ray. The low dry fountain blocks her feet but
    /// not her eyes: it is reported separately and the ray carries on
    /// to the hedge behind it.</summary>
    private RayHit CastRay(float angle, out RayHit shortHit)
    {
        shortHit = default;
        Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
        int mapX = Mathf.FloorToInt(_playerPosition.X);
        int mapY = Mathf.FloorToInt(_playerPosition.Y);

        float deltaX = Mathf.Abs(dir.X) < 0.00001f ? 100000f : Mathf.Abs(1f / dir.X);
        float deltaY = Mathf.Abs(dir.Y) < 0.00001f ? 100000f : Mathf.Abs(1f / dir.Y);
        int stepX = dir.X < 0f ? -1 : 1;
        int stepY = dir.Y < 0f ? -1 : 1;
        float sideX = stepX < 0
            ? (_playerPosition.X - mapX) * deltaX
            : (mapX + 1f - _playerPosition.X) * deltaX;
        float sideY = stepY < 0
            ? (_playerPosition.Y - mapY) * deltaY
            : (mapY + 1f - _playerPosition.Y) * deltaY;

        float distance = 0f;
        int side = 0;
        for (int i = 0; i < 72; i++)
        {
            if (sideX < sideY)
            {
                distance = sideX;
                sideX += deltaX;
                mapX += stepX;
                side = 0;
            }
            else
            {
                distance = sideY;
                sideY += deltaY;
                mapY += stepY;
                side = 1;
            }

            var cell = new Vector2I(mapX, mapY);
            char tile = CellAt(cell);
            if (!IsSolid(tile))
                continue;

            Vector2 hitPoint = _playerPosition + dir * distance;
            float wallX = side == 0
                ? hitPoint.Y - Mathf.Floor(hitPoint.Y)
                : hitPoint.X - Mathf.Floor(hitPoint.X);
            var hit = new RayHit
            {
                Hit = true,
                Distance = Mathf.Max(distance, 0.001f),
                Cell = cell,
                Tile = tile,
                Side = side,
                WallX = Mathf.Clamp(wallX, 0f, 0.999f),
            };

            if (tile == 'F')
            {
                if (!shortHit.Hit)
                    shortHit = hit;
                continue;
            }
            return hit;
        }

        return new RayHit
        {
            Hit = true,
            Distance = 40f,
            Cell = new Vector2I(mapX, mapY),
            Tile = '#',
            Side = side,
            WallX = 0f,
        };
    }

    /// <summary>Where she is looking. The turn behind her is a held
    /// half-turn, so the view eases round and settles again instead
    /// of snapping the way a tap would.</summary>
    private float ViewYaw => NormalizeAngle(_yaw + _lookBackAmount * Mathf.Pi);

    private float BeamFlicker() => Settings.ReduceMotionOrDefault()
        ? 1f
        : 0.95f + 0.05f * Mathf.Sin(Time.GetTicksMsec() / 57f);

    private float BeamRadiusNow() =>
        GameState.Instance?.HasFlag("basement_battery_taken") == true ? StrongBeamRadius : BeamRadius;

    /// <summary>How much of Father's torch reaches something at this
    /// depth. The circle itself decides where on screen it lands.</summary>
    private float TorchReach(float depth)
    {
        if (!_flashlightOn)
            return 0f;
        return Mathf.Clamp(1.15f - depth / 7f, 0f, 1f) * BeamFlicker();
    }

    /// <summary>How much of Father's torch reaches something at this
    /// depth. The circle itself decides where on screen it lands.
    ///
    /// The light is stepped, not smoothed: the rest of the house is
    /// painted in flat bands of light and shade, and a smooth
    /// falloff is what makes a drawn scene look like a rendered
    /// one. So the hedges are lit in a handful of steps, the way the
    /// rooms are.</summary>
    private float SurfaceLight(float depth) =>
        Posterize(Mathf.Clamp(0.12f + 0.95f * TorchReach(depth), 0.06f, 1.05f));

    /// <summary>Round a light level onto the house's five steps of
    /// paint.</summary>
    private static float Posterize(float light) =>
        Mathf.Clamp(Mathf.Round(light * 4f) * 0.25f, 0.125f, 1f);

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        float viewYaw = ViewYaw;

        DrawRect(new Rect2(0, 0, ScreenWidth, Horizon), SkyColorAt(Horizon * 0.5f), true);
        DrawGroundBase(viewYaw);
        DrawWalls(viewYaw);
        DrawGroundTufts(viewYaw);

        DrawTorchDarkness();
        DrawSkyAboveWalls(viewYaw);
        DrawTorchRings();

        DrawBeamShaft();
        DrawHeldFlashlight();

        DrawRect(new Rect2(0, 0, ScreenWidth, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, ScreenWidth, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, ScreenHeight), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, ScreenHeight), new Color(0, 0, 0, 0.22f), true);
        DrawEyelids();
    }

    private static Color SkyColorAt(float y)
    {
        float t = Mathf.Clamp(y / Horizon, 0f, 1f);
        return new Color(
            Mathf.Lerp(0.01f, 0.04f, t),
            Mathf.Lerp(0.016f, 0.062f, t),
            Mathf.Lerp(0.036f, 0.105f, t));
    }

    /// <summary>The grass she walks on, painted in the same tiles as
    /// the garden outside and projected with the walls' own eye
    /// height, so it flows past at exactly their speed. Each band of
    /// the floor is one flat quad, so the whole floor costs a couple
    /// of dozen draws rather than a column of them.</summary>
    private void DrawGroundBase(float viewYaw)
    {
        if (_grassTexture == null)
        {
            DrawFlatGround();
            return;
        }

        Vector2 dir = new(Mathf.Cos(viewYaw), Mathf.Sin(viewYaw));
        Vector2 right = new(-dir.Y, dir.X);
        float focal = ScreenWidth * 0.5f / Mathf.Tan(FieldOfView * 0.5f);
        float halfWidth = ScreenWidth * 0.5f / focal;
        float eyePixels = ProjectionScale * _eyeHeight;
        Vector2 textureSize = new(_grassTexture.GetWidth(), _grassTexture.GetHeight());

        const int bands = 20;
        float bandHeight = (ScreenHeight - Horizon) / bands;
        for (int band = 0; band < bands; band++)
        {
            float y0 = Horizon + band * bandHeight;
            float y1 = Mathf.Min(y0 + bandHeight, ScreenHeight);
            float depthFar = Mathf.Clamp(eyePixels / Mathf.Max(y0 - Horizon, 1f), 0.25f, 30f);
            float depthNear = Mathf.Clamp(eyePixels / Mathf.Max(y1 - Horizon, 1f), 0.25f, 30f);

            Vector2 farLeft = _playerPosition + dir * depthFar - right * (halfWidth * depthFar);
            Vector2 farRight = _playerPosition + dir * depthFar + right * (halfWidth * depthFar);
            Vector2 nearRight = _playerPosition + dir * depthNear + right * (halfWidth * depthNear);
            Vector2 nearLeft = _playerPosition + dir * depthNear - right * (halfWidth * depthNear);

            var points = new Vector2[]
            {
                new Vector2(0f, y0),
                new Vector2(ScreenWidth, y0),
                new Vector2(ScreenWidth, y1),
                new Vector2(0f, y1),
            };
            var uvs = new Vector2[]
            {
                Norm(new Vector2(farLeft.X * GrassTexScale, farLeft.Y * GrassTexScale), textureSize),
                Norm(new Vector2(farRight.X * GrassTexScale, farRight.Y * GrassTexScale), textureSize),
                Norm(new Vector2(nearRight.X * GrassTexScale, nearRight.Y * GrassTexScale), textureSize),
                Norm(new Vector2(nearLeft.X * GrassTexScale, nearLeft.Y * GrassTexScale), textureSize),
            };
            float lightFar = SurfaceLight(depthFar);
            float lightNear = SurfaceLight(depthNear);
            var colors = new Color[]
            {
                ScaleColor(Colors.White, lightFar),
                ScaleColor(Colors.White, lightFar),
                ScaleColor(Colors.White, lightNear),
                ScaleColor(Colors.White, lightNear),
            };
            DrawPolygon(points, colors, uvs, _grassTexture);
        }
    }

    /// <summary>The flat green the floor used to be, kept for the
    /// moment before the painted grass is available.</summary>
    private void DrawFlatGround()
    {
        for (float y = Horizon; y < ScreenHeight; y += 12f)
        {
            float height = Mathf.Min(12f, ScreenHeight - y);
            float centre = y + height * 0.5f;
            float depth = ProjectionScale * _eyeHeight / Mathf.Max(centre - Horizon, 1f);
            float light = SurfaceLight(depth);
            DrawRect(new Rect2(0, y, ScreenWidth, height),
                ScaleColor(new Color(0.055f, 0.15f, 0.06f), light), true);
        }
    }

    private void DrawWalls(float viewYaw)
    {
        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        _drawViewYaw = viewYaw;

        for (int column = 0; column < ColumnCount; column++)
        {
            float screenX = column * ColumnWidth;
            float centerX = screenX + ColumnWidth * 0.5f;
            float cameraX = 2f * centerX / ScreenWidth - 1f;
            float rayAngle = viewYaw + Mathf.Atan(cameraX * tanHalfFov);
            _rayAngleByColumn[column] = rayAngle;
            float fisheye = Mathf.Cos(rayAngle - viewYaw);
            RayHit hit = CastRay(rayAngle, out RayHit shortHit);

            float depth = Mathf.Max(hit.Distance * fisheye, 0.05f);
            _depthByColumn[column] = depth;
            float top = DrawWallSlice(screenX, depth, hit, out float crown);
            float skyLimit = top - crown;

            if (shortHit.Hit && shortHit.Distance < hit.Distance)
            {
                float shortDepth = Mathf.Max(shortHit.Distance * fisheye, 0.05f);
                _shortDepthByColumn[column] = shortDepth;
                // Close enough, the low fountain can stand taller on
                // screen than a far hedge; the sky must not clip it.
                float shortTop = DrawWallSlice(screenX, shortDepth, shortHit, out _);
                skyLimit = Mathf.Min(skyLimit, shortTop);
            }
            else
            {
                _shortDepthByColumn[column] = float.MaxValue;
            }
            _skyLimitByColumn[column] = Mathf.Clamp(skyLimit, 0f, Horizon);
        }
    }

    /// <summary>How tall a wall of this kind stands, in world units
    /// measured from the path she walks on.</summary>
    private static float WallTopUnits(char tile) => tile switch
    {
        'F' => FountainHeight,
        'P' => PillarHeight,
        'T' => TopiaryHeight,
        _ => HedgeWallHeight,
    };

    private Texture2D WallTextureFor(char tile) => tile switch
    {
        'P' => _stoneTexture,
        'F' => _stoneTexture,
        _ => _hedgeTexture,
    };

    /// <summary>Draw one projected slice; returns its top edge.</summary>
    private float DrawWallSlice(float screenX, float depth, RayHit hit, out float crown)
    {
        crown = 0f;
        float wallTop = WallTopUnits(hit.Tile);
        float groundLineHeight = ProjectionScale / depth;
        float bottom = Horizon + groundLineHeight * _eyeHeight;
        float top = bottom - groundLineHeight * (wallTop - _eyeHeight) * 1.06f;

        float light = SurfaceLight(depth) * (hit.Side == 0 ? 1f : 0.84f);
        Texture2D texture = WallTextureFor(hit.Tile);
        if (texture == null)
        {
            DrawFlatWallSlice(screenX, top, bottom, hit, light);
            return top;
        }

        float texScale = hit.Tile is 'P' or 'F' ? StoneTexScale : HedgeTexScale;

        // u runs along the face of the wall: the world coordinate
        // that runs along it (Y for a wall that faces along X, X for
        // a wall that faces along Y). It is worked out separately for
        // each end of the column by asking where the ray at that end
        // of the column would meet the face, so neighbouring columns
        // show one unbroken stretch of hedge instead of eight little
        // pictures of one.
        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        float phiLeft = Mathf.Atan((2f * screenX / ScreenWidth - 1f) * tanHalfFov);
        float phiRight = Mathf.Atan((2f * (screenX + ColumnWidth) / ScreenWidth - 1f) * tanHalfFov);
        float phiCentre = Mathf.Atan((2f * (screenX + ColumnWidth * 0.5f) / ScreenWidth - 1f) * tanHalfFov);
        Vector2 dirLeft = new(Mathf.Cos(_drawViewYaw + phiLeft), Mathf.Sin(_drawViewYaw + phiLeft));
        Vector2 dirRight = new(Mathf.Cos(_drawViewYaw + phiRight), Mathf.Sin(_drawViewYaw + phiRight));
        Vector2 dirCentre = new(Mathf.Cos(_drawViewYaw + phiCentre), Mathf.Sin(_drawViewYaw + phiCentre));

        float uLeft;
        float uRight;
        if (hit.Side == 0)
        {
            float faceX = dirCentre.X > 0f ? hit.Cell.X : hit.Cell.X + 1f;
            float across = faceX - _playerPosition.X;
            float yLeft = _playerPosition.Y + across * (dirLeft.Y / dirLeft.X);
            float yRight = _playerPosition.Y + across * (dirRight.Y / dirRight.X);
            uLeft = yLeft * texScale;
            uRight = yRight * texScale;
        }
        else
        {
            float faceY = dirCentre.Y > 0f ? hit.Cell.Y : hit.Cell.Y + 1f;
            float across = faceY - _playerPosition.Y;
            float xLeft = _playerPosition.X + across * (dirLeft.X / dirLeft.Y);
            float xRight = _playerPosition.X + across * (dirRight.X / dirRight.Y);
            uLeft = xLeft * texScale;
            uRight = xRight * texScale;
        }

        // A wall seen almost edge-on would otherwise smear the whole
        // tile into one column; it is held to a sane stretch instead.
        float uSpread = Mathf.Abs(uRight - uLeft);
        float maxSpread = ColumnWidth / FocalLength * depth * texScale * 4f;
        if (uSpread > maxSpread && uSpread > 0.001f)
        {
            float centre = (uLeft + uRight) * 0.5f;
            uLeft = centre - maxSpread * 0.5f;
            uRight = centre + maxSpread * 0.5f;
        }

        Color shade = ScaleColor(Colors.White, Mathf.Clamp(light, 0f, 1.05f));
        if (hit.Tile == 'F')
            shade = new Color(shade.R * 0.72f, shade.G * 0.86f, shade.B, shade.A);

        float vTop = 0f;
        float vBottom = wallTop * texScale;
        Vector2 textureSize = new(texture.GetWidth(), texture.GetHeight());
        var points = new Vector2[]
        {
            new Vector2(screenX, top),
            new Vector2(screenX + ColumnWidth + 1f, top),
            new Vector2(screenX + ColumnWidth + 1f, bottom),
            new Vector2(screenX, bottom),
        };
        var uvs = new Vector2[]
        {
            Norm(new Vector2(uLeft, vTop), textureSize),
            Norm(new Vector2(uRight, vTop), textureSize),
            Norm(new Vector2(uRight, vBottom), textureSize),
            Norm(new Vector2(uLeft, vBottom), textureSize),
        };
        DrawPolygon(points, new Color[] { shade, shade, shade, shade }, uvs, texture);

        if (hit.Tile is '#' or 'T')
        {
            crown = DrawHedgeCrown(screenX, top, bottom, hit, uLeft, uRight, groundLineHeight, shade);
            DrawHedgeBase(screenX, bottom, groundLineHeight, uLeft, uRight, shade);
        }
        else if (hit.Tile == 'F')
        {
            float rimY = top + Mathf.Min(18f, (bottom - top) * 0.14f);
            DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, Mathf.Max(2f, (bottom - top) * 0.05f)),
                ScaleColor(new Color(0.5f, 0.54f, 0.55f), light), true);
            DrawRect(new Rect2(screenX, rimY, ColumnWidth + 1f, 4f),
                ScaleColor(new Color(0.35f, 0.55f, 0.68f), light), true);
        }
        return top;
    }

    /// <summary>The flat colours the walls were before they were
    /// painted, kept for the moment before the tiles are available.</summary>
    private void DrawFlatWallSlice(float screenX, float top, float bottom, RayHit hit, float light)
    {
        Color baseColor = hit.Tile switch
        {
            'P' => new Color(0.34f, 0.35f, 0.34f),
            'F' => new Color(0.28f, 0.36f, 0.4f),
            'T' => new Color(0.035f, 0.24f, 0.07f),
            _ => new Color(0.045f, 0.27f, 0.075f),
        };
        DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, bottom - top),
            ScaleColor(baseColor, light), true);
    }

    /// <summary>A hand's width of leaves over the top of the hedge,
    /// so the skyline is cut and not shaved.</summary>
    private float DrawHedgeCrown(float screenX, float top, float bottom, RayHit hit,
        float uLeft, float uRight, float groundLineHeight, Color shade)
    {
        float height = bottom - top;
        // Only the near hedges are tall enough on screen for a fringe
        // of leaves over the top to be worth the draw.
        if (height <= 46f || _hedgeTexture == null)
            return 0f;

        float crown = Hash01(hit.Cell.X * 9, hit.Cell.Y * 5, 31) * Mathf.Min(12f, height * 0.08f);
        if (crown < 1.5f)
            return 0f;

        float v = crown / groundLineHeight * HedgeTexScale;
        Vector2 textureSize = new(_hedgeTexture.GetWidth(), _hedgeTexture.GetHeight());
        var points = new Vector2[]
        {
            new Vector2(screenX, top - crown),
            new Vector2(screenX + ColumnWidth + 1f, top - crown),
            new Vector2(screenX + ColumnWidth + 1f, top + 1f),
            new Vector2(screenX, top + 1f),
        };
        var uvs = new Vector2[]
        {
            Norm(new Vector2(uLeft, 0f), textureSize),
            Norm(new Vector2(uRight, 0f), textureSize),
            Norm(new Vector2(uRight, v), textureSize),
            Norm(new Vector2(uLeft, v), textureSize),
        };
        Color crownShade = shade;
        if (hit.Tile == 'T')
            crownShade = new Color(shade.R * 0.72f, shade.G * 0.92f, shade.B, shade.A);
        DrawPolygon(points, new Color[] { crownShade, crownShade, crownShade, crownShade }, uvs,
            _hedgeTexture);
        return crown;
    }

    /// <summary>The roots and rough grass at the foot of the hedge,
    /// so it is planted in the ground instead of standing on it.</summary>
    private void DrawHedgeBase(float screenX, float bottom, float groundLineHeight,
        float uLeft, float uRight, Color shade)
    {
        if (_hedgeBaseTexture == null)
            return;

        float bandHeight = HedgeBaseHeight * groundLineHeight;
        if (bandHeight < 2f)
            return;

        float baseScale = _hedgeBaseTexture.GetWidth() / 1.78f;
        Vector2 textureSize = new(_hedgeBaseTexture.GetWidth(), _hedgeBaseTexture.GetHeight());
        var points = new Vector2[]
        {
            new Vector2(screenX, bottom - bandHeight),
            new Vector2(screenX + ColumnWidth + 1f, bottom - bandHeight),
            new Vector2(screenX + ColumnWidth + 1f, bottom + 1f),
            new Vector2(screenX, bottom + 1f),
        };
        var uvs = new Vector2[]
        {
            Norm(new Vector2(uLeft / HedgeTexScale * baseScale, 0f), textureSize),
            Norm(new Vector2(uRight / HedgeTexScale * baseScale, 0f), textureSize),
            Norm(new Vector2(uRight / HedgeTexScale * baseScale, textureSize.Y), textureSize),
            Norm(new Vector2(uLeft / HedgeTexScale * baseScale, textureSize.Y), textureSize),
        };
        DrawPolygon(points, new Color[] { shade, shade, shade, shade }, uvs, _hedgeBaseTexture);
    }

    /// <summary>Grass tufts and pebbles fixed in the world and
    /// projected with the walls' own eye height, so the ground flows
    /// past at exactly the speed the hedge bases do.</summary>
    private void DrawGroundTufts(float viewYaw)
    {
        Vector2 dir = new(Mathf.Cos(viewYaw), Mathf.Sin(viewYaw));
        Vector2 right = new(-dir.Y, dir.X);
        float focal = ScreenWidth * 0.5f / Mathf.Tan(FieldOfView * 0.5f);
        int px = Mathf.FloorToInt(_playerPosition.X);
        int py = Mathf.FloorToInt(_playerPosition.Y);

        for (int cy = py - 8; cy <= py + 8; cy++)
        {
            for (int cx = px - 8; cx <= px + 8; cx++)
            {
                if (IsSolid(CellAt(new Vector2I(cx, cy))))
                    continue;

                for (int k = 0; k < 4; k++)
                {
                    var world = new Vector2(
                        cx + 0.08f + 0.84f * Hash01(cx, cy, 11 + k * 7),
                        cy + 0.08f + 0.84f * Hash01(cx, cy, 23 + k * 13));
                    Vector2 rel = world - _playerPosition;
                    float depth = rel.Dot(dir);
                    if (depth < 0.18f || depth > 8.5f)
                        continue;

                    float screenX = ScreenWidth * 0.5f + rel.Dot(right) / depth * focal;
                    if (screenX < -12f || screenX > ScreenWidth + 12f)
                        continue;
                    int column = Mathf.Clamp(Mathf.FloorToInt(screenX / ColumnWidth), 0, ColumnCount - 1);
                    if (depth > _depthByColumn[column] || depth > _shortDepthByColumn[column])
                        continue;

                    float screenY = Horizon + ProjectionScale * _eyeHeight / depth;
                    if (screenY > ScreenHeight + 4f)
                        continue;

                    float hash = Hash01(cx * 3 + k, cy * 5 - k, 71);
                    float size = Mathf.Clamp(9f / depth, 2f, 12f);
                    float light = SurfaceLight(depth);
                    Color color = hash > 0.86f
                        ? ScaleColor(new Color(0.34f, 0.32f, 0.27f), light)
                        : ScaleColor(new Color(0.11f, 0.33f, 0.11f), light);
                    DrawRect(new Rect2(screenX - size * 0.5f, screenY - size * 0.6f,
                        size, Mathf.Max(2f, size * 0.6f)), color, true);
                }
            }
        }
    }

    /// <summary>The flashlight circle, drawn exactly as the basement
    /// draws it: two square-edged darkness passes in chunky strips.</summary>
    private void DrawTorchDarkness()
    {
        if (!_hasFlashlight || !_flashlightOn)
        {
            // Moonlit silhouettes only.
            DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight),
                new Color(0.002f, 0.002f, 0.005f, 0.8f), true);
            return;
        }

        float radius = BeamRadiusNow();
        DrawDarkOutside(_beamPosition, radius + 58f, new Color(0.002f, 0.002f, 0.004f, 0.42f));
        DrawDarkOutside(_beamPosition, radius, new Color(0.001f, 0.001f, 0.003f, 0.74f));
    }

    private void DrawTorchRings()
    {
        if (!_hasFlashlight || !_flashlightOn)
            return;

        float radius = BeamRadiusNow();
        DrawArc(_beamPosition, radius * 0.97f, 0f, Mathf.Tau, 56,
            new Color(1f, 0.83f, 0.45f, 0.09f), 13f, true);
        DrawArc(_beamPosition, radius * 0.68f, 0f, Mathf.Tau, 48,
            new Color(1f, 0.88f, 0.55f, 0.05f), 20f, true);
        DrawCircle(_beamPosition, 4f, new Color(1f, 0.9f, 0.62f, 0.28f));
    }

    private void DrawDarkOutside(Vector2 center, float radius, Color dark)
    {
        float top = Mathf.Clamp(center.Y - radius, 0f, ScreenHeight);
        float bottom = Mathf.Clamp(center.Y + radius, 0f, ScreenHeight);
        if (top > 0f)
            DrawRect(new Rect2(0, 0, ScreenWidth, top), dark, true);
        if (bottom < ScreenHeight)
            DrawRect(new Rect2(0, bottom, ScreenWidth, ScreenHeight - bottom), dark, true);

        const float stripHeight = 9f;
        for (float y = top; y < bottom; y += stripHeight)
        {
            float height = Mathf.Min(stripHeight + 1f, bottom - y);
            float centreY = y + height * 0.5f;
            float dy = centreY - center.Y;
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float leftEdge = Mathf.Clamp(center.X - half, 0f, ScreenWidth);
            float rightEdge = Mathf.Clamp(center.X + half, 0f, ScreenWidth);
            if (leftEdge > 0f)
                DrawRect(new Rect2(0, y, leftEdge, height), dark, true);
            if (rightEdge < ScreenWidth)
                DrawRect(new Rect2(rightEdge, y, ScreenWidth - rightEdge, height), dark, true);
        }
    }

    /// <summary>The torch lights hedges, not the sky. After the dark
    /// is laid down, the night sky is put back wherever it shows
    /// above the hedge tops, so the walls keep a moonlit outline.</summary>
    private void DrawSkyAboveWalls(float viewYaw)
    {
        if (_skyTexture != null)
            DrawPaintedSky(viewYaw);
        else
            DrawFlatSky();

        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        float moonRelative = NormalizeAngle(2.45f - viewYaw);
        if (Mathf.Abs(moonRelative) < FieldOfView * 0.55f)
        {
            float moonX = ScreenWidth * 0.5f +
                Mathf.Tan(moonRelative) / tanHalfFov * ScreenWidth * 0.5f;
            var moon = new Vector2(moonX, 88f);
            DrawSkyDisc(moon, 31f, new Color(0.72f, 0.8f, 0.92f, 0.13f));
            DrawSkyDisc(moon, 19f, new Color(0.78f, 0.84f, 0.92f, 0.72f));
            DrawSkyDisc(moon + new Vector2(7, -3), 15f, new Color(0.018f, 0.03f, 0.055f, 0.82f));
        }
    }

    /// <summary>The painted night sky, put back in above the hedge
    /// tops once the torch's dark has been laid down, so the walls
    /// keep a moonlit outline and the clouds drift as she turns.</summary>
    private void DrawPaintedSky(float viewYaw)
    {
        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        float vScale = _skyTexture.GetHeight() / Horizon;
        Vector2 textureSize = new(_skyTexture.GetWidth(), _skyTexture.GetHeight());
        Color tint = new Color(0.92f, 0.96f, 1f, 1f);

        for (int column = 0; column < ColumnCount; column++)
        {
            float limit = _skyLimitByColumn[column];
            if (limit <= 0.5f)
                continue;

            float x0 = column * ColumnWidth;
            float x1 = x0 + ColumnWidth;
            float u0 = (viewYaw + Mathf.Atan((2f * x0 / ScreenWidth - 1f) * tanHalfFov)) * SkyPixelsPerRadian;
            float u1 = (viewYaw + Mathf.Atan((2f * x1 / ScreenWidth - 1f) * tanHalfFov)) * SkyPixelsPerRadian;
            // The sky is anchored to the screen, not to the wall tops,
            // so crouching clips more of it instead of sliding it.
            float vTop = 0f;
            float vBottom = limit * vScale;

            var points = new Vector2[]
            {
                new Vector2(x0, 0f),
                new Vector2(x1, 0f),
                new Vector2(x1, limit),
                new Vector2(x0, limit),
            };
            var uvs = new Vector2[]
            {
                Norm(new Vector2(u0, vTop), textureSize),
                Norm(new Vector2(u1, vTop), textureSize),
                Norm(new Vector2(u1, vBottom), textureSize),
                Norm(new Vector2(u0, vBottom), textureSize),
            };
            DrawPolygon(points, new Color[] { tint, tint, tint, tint }, uvs, _skyTexture);
        }
    }

    /// <summary>Godot's polygons want their texture coordinates as a
    /// fraction of the texture, not as a pixel in it.</summary>
    private static Vector2 Norm(Vector2 pixelUv, Vector2 textureSize) => new(
        pixelUv.X / Mathf.Max(textureSize.X, 1f),
        pixelUv.Y / Mathf.Max(textureSize.Y, 1f));

    /// <summary>The flat night the sky was before it was painted.</summary>
    private void DrawFlatSky()
    {
        for (float y = 0f; y < Horizon; y += SkyBand)
        {
            float bandBottom = Mathf.Min(y + SkyBand, Horizon);
            Color color = SkyColorAt((y + bandBottom) * 0.5f);
            int runStart = -1;
            for (int column = 0; column <= ColumnCount; column++)
            {
                bool full = column < ColumnCount && _skyLimitByColumn[column] >= bandBottom;
                if (full)
                {
                    if (runStart < 0)
                        runStart = column;
                    continue;
                }

                if (runStart >= 0)
                {
                    DrawRect(new Rect2(runStart * ColumnWidth, y,
                        (column - runStart) * ColumnWidth, bandBottom - y), color, true);
                    runStart = -1;
                }
                if (column < ColumnCount && _skyLimitByColumn[column] > y)
                {
                    DrawRect(new Rect2(column * ColumnWidth, y,
                        ColumnWidth, _skyLimitByColumn[column] - y), color, true);
                }
            }
        }
    }

    private void DrawSkyDisc(Vector2 centre, float radius, Color color)
    {
        const float row = 3f;
        for (float y = centre.Y - radius; y < centre.Y + radius; y += row)
        {
            float dy = y + row * 0.5f - centre.Y;
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            if (half > 0.5f)
                DrawSkyMaskedRun(centre.X - half, centre.X + half, y, row, color);
        }
    }

    private void DrawSkyMaskedRun(float x0, float x1, float y, float height, Color color)
    {
        x0 = Mathf.Max(0f, x0);
        x1 = Mathf.Min(ScreenWidth, x1);
        if (x1 <= x0)
            return;

        int firstColumn = Mathf.Clamp((int)(x0 / ColumnWidth), 0, ColumnCount - 1);
        int lastColumn = Mathf.Clamp((int)((x1 - 0.001f) / ColumnWidth), 0, ColumnCount - 1);
        float runStart = -1f;
        for (int column = firstColumn; column <= lastColumn + 1; column++)
        {
            bool open = column <= lastColumn && _skyLimitByColumn[column] >= y + height;
            if (open)
            {
                if (runStart < 0f)
                    runStart = Mathf.Max(x0, column * ColumnWidth);
                continue;
            }
            if (runStart >= 0f)
            {
                float runEnd = Mathf.Min(x1, column * ColumnWidth);
                if (runEnd > runStart)
                    DrawRect(new Rect2(runStart, y, runEnd - runStart, height), color, true);
                runStart = -1f;
            }
        }
    }

    // ------------------------------------------------------------ Mara's hand

    /// <summary>Where her hands hold the torch this frame. The steps
    /// sway it and the landing dips it, and it rises into frame when
    /// the torch comes on. The camera carries the crouch and the jump
    /// now, so the hands in front of her eyes do not have to.
    ///
    /// The torch never turns in her hands: it is held low at the
    /// bottom-left of the frame with the sleeve running off the
    /// corner, and it slides left and right along the bottom with
    /// where she is pointing.</summary>
    private void GetHandPose(out Vector2 grip, out float torchAngle, out Vector2 lens)
    {
        Vector2 offset = Vector2.Zero;
        if (!Settings.ReduceMotionOrDefault())
        {
            offset.Y += Mathf.Cos(_stepPhase * Mathf.Tau) * _bobAmpY * _bobAmount;
            offset.X += Mathf.Sin(_stepPhase * Mathf.Pi) * _bobAmpX * _bobAmount;
        }
        offset.Y += _landDip * _landDip * 16f;
        offset.Y += _handRise * TorchRiseDistance;

        torchAngle = 0f;
        float left = TorchRestLeft + TorchSlide() + offset.X;
        float top = ScreenHeight - _torchSpriteHeight + offset.Y;
        Vector2 pivot = new(TorchSpriteGrip.X * TorchSpriteWidth, TorchSpriteGrip.Y * _torchSpriteHeight);
        grip = new Vector2(left + pivot.X, top + pivot.Y);
        lens = grip + TorchLensOffset;
    }

    /// <summary>How far along the bottom of the frame the torch has
    /// slid to follow where she is pointing. It moves left and
    /// right, and no other way.</summary>
    private float TorchSlide()
    {
        if (!_flashlightOn)
            return 0f;
        float t = Mathf.Clamp((_beamPosition.X - 180f) / 620f, 0f, 1f);
        return t * TorchSlideRange;
    }

    /// <summary>Where the ember of the torch sits, measured from the
    /// place her hands hold it.</summary>
    private Vector2 TorchLensOffset => new(
        (TorchSpriteLens.X - TorchSpriteGrip.X) * TorchSpriteWidth,
        (TorchSpriteLens.Y - TorchSpriteGrip.Y) * _torchSpriteHeight);


    /// <summary>GameUI's held-item light rule: the hand is only as
    /// bright as the torch light actually landing near it, with a
    /// little back-spill because this time the torch is in it. The
    /// back-spill is generous: this is a *lit* torch, and the wool of
    /// her sleeve has to be there to be seen at all.</summary>
    private float HandLightLevel(Vector2 grip)
    {
        if (!_flashlightOn)
            return 0.34f;
        float distance = _beamPosition.DistanceTo(grip);
        float falloff = 1f - Mathf.Clamp((distance - 110f) / 330f, 0f, 1f);
        return Mathf.Clamp((0.72f + 0.28f * falloff) * BeamFlicker(), 0f, 1f);
    }

    private static Color TorchTint(float level) =>
        new(level, level * 0.96f, level * 0.84f, 1f);

    /// <summary>The light itself, thrown from the ember in her hand
    /// to the circle she has put on the hedges. The circle is the
    /// house's own chunky pool of torchlight; the ray is only a
    /// breath of white between the two, so the light has a direction
    /// without becoming a second sun.</summary>
    private void DrawBeamShaft()
    {
        if (!_hasFlashlight || !_flashlightOn || _handRise >= 0.995f)
            return;

        GetHandPose(out _, out _, out Vector2 lens);
        float strength = BeamFlicker() * (1f - _handRise);
        var warm = new Color(1f, 0.88f, 0.6f);

        // The ray: a faint, transparent wedge of white from the
        // ember out towards the circle, fading away before it gets
        // there. Three thin wedges, so the edges of the light are
        // soft rather than cut.
        Vector2 toPool = _beamPosition - lens;
        float length = toPool.Length();
        if (length > 12f)
        {
            Vector2 dir = toPool / length;
            Vector2 side = new(-dir.Y, dir.X);
            Vector2 far = lens + dir * length;
            for (int i = 0; i < 3; i++)
            {
                float spread = Mathf.Lerp(30f, 12f, i / 2f);
                float alpha = Mathf.Lerp(0.05f, 0.025f, i / 2f) * strength;
                float width = Mathf.Tan(Mathf.DegToRad(spread)) * length;
                var points = new Vector2[]
                {
                    lens,
                    far + side * width,
                    far - side * width,
                };
                var colors = new Color[]
                {
                    new Color(1f, 1f, 1f, alpha),
                    new Color(1f, 1f, 1f, 0f),
                    new Color(1f, 1f, 1f, 0f),
                };
                DrawPolygon(points, colors);
            }
        }

        // The pool it lands in: a stepped, slightly squashed pool of
        // light, hottest at the middle, the way his big torch burned
        // through every bulb they owned. It sits inside the circle of
        // light, so the chunky dark around it stays chunky.
        DrawSetTransform(_beamPosition, 0f, new Vector2(1f, 0.74f));
        DrawCircle(Vector2.Zero, 138f, new Color(warm.R, warm.G, warm.B, 0.08f * strength));
        DrawCircle(Vector2.Zero, 100f, new Color(warm.R, warm.G, warm.B, 0.10f * strength));
        DrawCircle(Vector2.Zero, 64f, new Color(warm.R, warm.G, warm.B, 0.12f * strength));
        DrawCircle(Vector2.Zero, 34f, new Color(1f, 0.93f, 0.72f, 0.16f * strength));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // The lamp itself, glinting in her hand.
        DrawCircle(lens, 17f, new Color(1f, 0.94f, 0.74f, 0.4f * strength));
        DrawCircle(lens, 8f, new Color(1f, 0.98f, 0.86f, 0.6f * strength));
    }

    /// <summary>Her hands round Father's torch: one sprite of its own,
    /// held low in the bottom-left corner and swung about her grip so
    /// the ember always points at the circle of light the mouse has
    /// put on the dark.</summary>
    private void DrawHeldFlashlight()
    {
        if (!_hasFlashlight || _torchHandsTexture == null || _handRise >= 0.995f)
            return;

        GetHandPose(out Vector2 grip, out float torchAngle, out Vector2 lens);
        float level = HandLightLevel(grip);

        DrawSetTransform(grip, torchAngle, Vector2.One);
        DrawTextureRect(_torchHandsTexture,
            new Rect2(-TorchSpriteGrip.X * TorchSpriteWidth,
                -TorchSpriteGrip.Y * _torchSpriteHeight,
                TorchSpriteWidth, _torchSpriteHeight),
            false, TorchTint(level));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (_flashlightOn)
        {
            float flicker = BeamFlicker();
            DrawCircle(lens, 13f, new Color(1f, 0.94f, 0.74f, 0.42f * flicker));
            DrawCircle(lens, 6f, new Color(1f, 0.98f, 0.86f, 0.7f * flicker));
        }
    }

    private void DrawEyelids()
    {
        float lid = (1f - _eyeOpen) * ScreenHeight * 0.5f;
        if (lid <= 0.5f)
            return;
        DrawRect(new Rect2(0, 0, ScreenWidth, lid), Colors.Black, true);
        DrawRect(new Rect2(0, ScreenHeight - lid, ScreenWidth, lid), Colors.Black, true);
    }

    // ------------------------------------------------------------ helpers

    private async Task Say(string line)
    {
        await Dialogue.ShowText(this, line, 5.8f, 576f, 324f, fontSize: 23);
    }

    private async Task WaitSeconds(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    private static float NormalizeAngle(float angle)
    {
        while (angle > Mathf.Pi)
            angle -= Mathf.Tau;
        while (angle < -Mathf.Pi)
            angle += Mathf.Tau;
        return angle;
    }

    private static Color ScaleColor(Color color, float scale) =>
        new(
            Mathf.Clamp(color.R * scale, 0f, 1f),
            Mathf.Clamp(color.G * scale, 0f, 1f),
            Mathf.Clamp(color.B * scale, 0f, 1f),
            color.A);

    private static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint hash = (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)salt * 974634211u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash % 1000u) / 999f;
        }
    }
}
