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
/// a code-drawn, chunky first-person hedge maze. Her body keeps
/// the house's own manners. W and S walk at the same unhurried
/// pace as every walked journey (footfalls 0.92 s apart on the
/// dirt road); A and D turn her at one steady rate that stops the
/// instant the key is let go, so the view can never drift. The
/// mouse is her wrist on Father's flashlight: the same circle of
/// light, with the same chunky dark around it, that the vent, the
/// basement and the blackout use. Her left hand holds the torch
/// in the same palm, drawn with the same art, as every item she
/// has held up in first person. Jumping and crouching belong to
/// Mara alone: the hand and torch rise, dip and lower, while the
/// world itself stays still.
///
/// The south break returns to the bridge and the far north-west
/// break continues to the last room, so exploring it can never
/// strand a run while its future puzzles are still being thought
/// about.
/// </summary>
public partial class HedgeMaze : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string HandTexturePath = "res://assets/images/core/hand_palm.png";
    private const string FlashlightTexturePath = "res://assets/images/core/items/flashlight.png";
    private const string BridgeScenePath = "res://scenes/world/bridge_repair.tscn";
    private const string Room7ScenePath = "res://scenes/world/room_7.tscn";

    public const string IntroPendingFlag = "hedge_maze_intro_pending";
    public const string AwakenedFlag = "hedge_maze_awakened";
    public const string FarSideReachedFlag = "hedge_maze_far_side_reached";

    private const float ScreenWidth = 1152f;
    private const float ScreenHeight = 648f;

    // The world is never lifted, lowered or bobbed. Jumps, crouches
    // and steps move Mara's hand instead of the horizon.
    private const float Horizon = 324f;
    private const float ColumnWidth = 8f;
    private const int ColumnCount = 144;
    private const float FieldOfView = 1.08f;
    private const float ProjectionScale = 680f;
    private const float EyeHeightPixels = ProjectionScale * 0.37f;
    private const float SkyBand = 24f;
    private const float PlayerRadius = 0.23f;

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

    // Jump and crouch are Mara's body, shown through her hand.
    private const float JumpVelocity = 3.4f;
    private const float JumpGravity = 11f;
    private const float HandJumpLift = 115f;

    // The flashlight circle, exactly the basement's (the stronger
    // battery from older saves widens it the same way).
    private const float BeamRadius = 158f;
    private const float StrongBeamRadius = 208f;

    // The held hand is GameUI's held-item hand: the same palm art,
    // aspect and palm anchor, sized down for walking.
    private const float HeldPalmX = 0.51f;
    private const float HeldPalmY = 0.36f;
    private const float HeldHandAspect = 1.332f;
    private const float HandWidth = 260f;
    private const float HandVisibleFraction = 0.78f;
    private const float HandRestCentreX = 194f;
    private const float TorchIconSize = 150f;

    // The lens in the flashlight item art (512 px: about x 420,
    // y 105), as a fraction of the icon from its centre.
    private static readonly Vector2 TorchLensLocal = new(0.32f, -0.295f);
    private static readonly float TorchLensBaseAngle = Mathf.Atan2(-0.295f, 0.32f);
    private const float TorchMinRotation = -1.66f;
    private const float TorchMaxRotation = 1.05f;

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
    private Texture2D _handTexture;
    private Texture2D _torchTexture;
    private Label _titleLabel;
    private Label _promptLabel;
    private Label _exitLabel;

    private Vector2 _playerPosition = new(13.5f, 17.5f);
    private Vector2 _velocity = Vector2.Zero;
    private float _yaw = Mathf.Pi;
    private bool _lookingBack;
    private bool _moving;
    private bool _running;

    private float _crouchAmount;
    private float _jumpHeight;
    private float _verticalVelocity;
    private bool _grounded = true;
    private float _landDip;
    private float _stepPhase;
    private bool _stepping;
    private float _bobAmount;
    private float _bobAmpX;
    private float _bobAmpY;
    private bool _footstepAlternate;

    private Vector2 _beamPosition = new(576f, 324f);
    private bool _beamReady;
    private float _torchRotation;
    private bool _torchRotationReady;
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
    private string _exitHintCache = string.Empty;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        _handTexture = GD.Load<Texture2D>(HandTexturePath);
        _torchTexture = GD.Load<Texture2D>(FlashlightTexturePath);
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

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
        _lookingBack = false;
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
            "W/S walk · A/D turn · mouse aims the torch\nSHIFT run · C crouch · SPACE jump · CAPS LOCK look back",
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

        if (capsLock && keyEvent.Pressed && !keyEvent.Echo && InputFree())
        {
            _lookingBack = !_lookingBack;
            AudioManager.Play(Sfx.PageTurn, -15f, _lookingBack ? 0.82f : 1.02f);
        }
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
        UpdateTorchAim(dt);
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
        if (_lookingBack)
            speed *= LookingBackFactor;

        Vector2 facing = new(Mathf.Cos(_yaw), Mathf.Sin(_yaw));
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

    /// <summary>The torch in her palm turns to face the circle the
    /// mouse has put on the dark, at the beam's own smoothing.</summary>
    private void UpdateTorchAim(float dt)
    {
        GetHandPose(out _, out _, out Vector2 palm);
        Vector2 toBeam = _beamPosition - palm;
        float target = _torchRotation;
        if (toBeam.LengthSquared() > 1f)
        {
            float diff = NormalizeAngle(Mathf.Atan2(toBeam.Y, toBeam.X) - TorchLensBaseAngle);
            if (diff < TorchMinRotation || diff > TorchMaxRotation)
            {
                float toMin = Mathf.Abs(NormalizeAngle(diff - TorchMinRotation));
                float toMax = Mathf.Abs(NormalizeAngle(diff - TorchMaxRotation));
                diff = toMin < toMax ? TorchMinRotation : TorchMaxRotation;
            }
            target = diff;
        }

        if (!_torchRotationReady)
        {
            _torchRotation = target;
            _torchRotationReady = true;
            return;
        }
        float weight = Mathf.Clamp(1f - Mathf.Exp(-20f * dt), 0f, 1f);
        _torchRotation = Mathf.Lerp(_torchRotation, target, weight);
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
        else if (_lookingBack)
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

    private float ViewYaw => NormalizeAngle(_yaw + (_lookingBack ? Mathf.Pi : 0f));

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

    private float SurfaceLight(float depth) =>
        Mathf.Clamp(0.12f + 0.95f * TorchReach(depth), 0.06f, 1.05f);

    // ------------------------------------------------------------ drawing

    public override void _Draw()
    {
        float viewYaw = ViewYaw;

        DrawRect(new Rect2(0, 0, ScreenWidth, Horizon), SkyColorAt(Horizon * 0.5f), true);
        DrawGroundBase();
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

    private void DrawGroundBase()
    {
        for (float y = Horizon; y < ScreenHeight; y += 12f)
        {
            float height = Mathf.Min(12f, ScreenHeight - y);
            float centre = y + height * 0.5f;
            float depth = EyeHeightPixels / Mathf.Max(centre - Horizon, 1f);
            float light = SurfaceLight(depth);
            DrawRect(new Rect2(0, y, ScreenWidth, height),
                ScaleColor(new Color(0.055f, 0.15f, 0.06f), light), true);
        }
    }

    private void DrawWalls(float viewYaw)
    {
        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);

        for (int column = 0; column < ColumnCount; column++)
        {
            float screenX = column * ColumnWidth;
            float centerX = screenX + ColumnWidth * 0.5f;
            float cameraX = 2f * centerX / ScreenWidth - 1f;
            float rayAngle = viewYaw + Mathf.Atan(cameraX * tanHalfFov);
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

    /// <summary>Draw one projected slice; returns its top edge.</summary>
    private float DrawWallSlice(float screenX, float depth, RayHit hit, out float crown)
    {
        crown = 0f;
        float heightFactor = hit.Tile switch
        {
            'F' => 0.48f,
            'P' => 1.08f,
            'T' => 1.42f,
            _ => 1.28f,
        };
        float groundLineHeight = ProjectionScale / depth;
        float lineHeight = groundLineHeight * heightFactor;
        float bottom = Horizon + groundLineHeight * 0.37f;
        float top = bottom - lineHeight * 1.06f;

        float light = SurfaceLight(depth) * (hit.Side == 0 ? 1f : 0.84f);
        Color baseColor = hit.Tile switch
        {
            'P' => new Color(0.34f, 0.35f, 0.34f),
            'F' => new Color(0.28f, 0.36f, 0.4f),
            'T' => new Color(0.035f, 0.24f, 0.07f),
            _ => new Color(0.045f, 0.27f, 0.075f),
        };
        DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, bottom - top),
            ScaleColor(baseColor, light), true);

        if (hit.Tile is '#' or 'T')
        {
            crown = DrawLeafDetail(screenX, top, bottom, hit.Cell, hit.WallX, light, hit.Tile);
        }
        else if (hit.Tile == 'P')
        {
            DrawStoneDetail(screenX, top, bottom, hit.Cell, light);
        }
        else if (hit.Tile == 'F')
        {
            float rimY = top + Mathf.Min(18f, lineHeight * 0.14f);
            DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, Mathf.Max(2f, lineHeight * 0.05f)),
                ScaleColor(new Color(0.5f, 0.54f, 0.55f), light), true);
            DrawRect(new Rect2(screenX, rimY, ColumnWidth + 1f, 4f),
                ScaleColor(new Color(0.35f, 0.55f, 0.68f), light), true);
        }
        return top;
    }

    private float DrawLeafDetail(float screenX, float top, float bottom, Vector2I cell,
        float wallX, float light, char tile)
    {
        float height = bottom - top;
        if (height <= 8f)
            return 0f;

        int textureColumn = Mathf.FloorToInt(wallX * 8f);
        float crown = Hash01(cell.X * 9 + textureColumn, cell.Y * 5, 31) *
            Mathf.Min(11f, height * 0.07f);
        DrawRect(new Rect2(screenX, top - crown, ColumnWidth + 1f, crown + 5f),
            ScaleColor(tile == 'T'
                ? new Color(0.05f, 0.3f, 0.09f)
                : new Color(0.07f, 0.35f, 0.1f), light), true);

        const int bands = 9;
        for (int band = 0; band < bands; band++)
        {
            float hash = Hash01(cell.X * 13 + textureColumn * 3, cell.Y * 17 + band, 47);
            if (hash < 0.42f)
                continue;
            float y = top + (band + 0.18f + hash * 0.45f) * height / bands;
            float size = Mathf.Clamp(height / bands * 0.42f, 3f, 13f);
            float x = screenX + Hash01(cell.X + band, cell.Y - textureColumn, 83) *
                Mathf.Max(1f, ColumnWidth - size * 0.55f);
            Color leaf = hash > 0.78f
                ? ScaleColor(new Color(0.13f, 0.48f, 0.16f), light)
                : ScaleColor(new Color(0.012f, 0.09f, 0.025f), Mathf.Clamp(light + 0.15f, 0f, 1f));
            DrawRect(new Rect2(x, y, size * 0.72f, size), leaf, true);
        }
        return crown;
    }

    private void DrawStoneDetail(float screenX, float top, float bottom, Vector2I cell, float light)
    {
        float height = bottom - top;
        if (height <= 10f)
            return;
        Color seam = ScaleColor(new Color(0.08f, 0.085f, 0.08f), Mathf.Clamp(light + 0.2f, 0f, 1f));
        for (int i = 1; i < 5; i++)
        {
            float y = top + height * i / 5f + Hash01(cell.X, cell.Y + i, 59) * 3f;
            DrawRect(new Rect2(screenX, y, ColumnWidth + 1f, 2f), seam, true);
        }
        DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, Mathf.Min(10f, height * 0.08f)),
            ScaleColor(new Color(0.5f, 0.51f, 0.47f), light), true);
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

                    float screenY = Horizon + EyeHeightPixels / depth;
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

        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        for (int i = 0; i < 105; i++)
        {
            float relative = NormalizeAngle(i * 2.399963f - viewYaw);
            if (Mathf.Abs(relative) > FieldOfView * 0.56f)
                continue;
            float x = ScreenWidth * 0.5f + Mathf.Tan(relative) / tanHalfFov * ScreenWidth * 0.5f;
            if (x < 0f || x >= ScreenWidth - 2f)
                continue;
            float y = 18f + Hash01(i, 17, 91) * (Horizon - 70f);
            int column = Mathf.Clamp(Mathf.FloorToInt(x / ColumnWidth), 0, ColumnCount - 1);
            if (y + 2f > _skyLimitByColumn[column])
                continue;
            float alpha = 0.25f + 0.55f * Hash01(i, 43, 7);
            DrawRect(new Rect2(x, y, 2f, 2f), new Color(0.85f, 0.9f, 1f, alpha), true);
        }

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

    /// <summary>Where her left hand is this frame. Steps sway it,
    /// a jump lifts it and the landing dips it, a crouch lowers and
    /// tilts it, and it rises into frame when the torch comes on —
    /// the world itself never moves for any of these.</summary>
    private void GetHandPose(out Vector2 wrist, out float handAngle, out Vector2 palm)
    {
        float handHeight = HandWidth * HeldHandAspect;
        Vector2 offset = Vector2.Zero;
        if (!Settings.ReduceMotionOrDefault())
        {
            offset.Y += Mathf.Cos(_stepPhase * Mathf.Tau) * _bobAmpY * _bobAmount;
            offset.X += Mathf.Sin(_stepPhase * Mathf.Pi) * _bobAmpX * _bobAmount;
        }
        offset.Y -= _jumpHeight * HandJumpLift;
        offset.Y += _landDip * _landDip * 22f;
        offset += new Vector2(16f, 62f) * _crouchAmount;
        offset.Y += _handRise * 320f;

        wrist = new Vector2(HandRestCentreX,
            ScreenHeight + handHeight * (1f - HandVisibleFraction)) + offset;
        handAngle = _torchRotation * 0.12f + 0.08f * _crouchAmount;
        var palmLocal = new Vector2(HandWidth * (HeldPalmX - 0.5f), -handHeight * (1f - HeldPalmY));
        palm = wrist + palmLocal.Rotated(handAngle);
    }

    private Vector2 TorchLensPosition(Vector2 palm) =>
        palm + (TorchLensLocal * TorchIconSize).Rotated(_torchRotation);

    /// <summary>GameUI's held-item light rule: the hand is only as
    /// bright as the torch light actually landing near it, with a
    /// little back-spill because this time the torch is in it.</summary>
    private float HandLightLevel(Vector2 palm)
    {
        if (!_flashlightOn)
            return 0.2f;
        float distance = _beamPosition.DistanceTo(palm);
        float falloff = 1f - Mathf.Clamp((distance - 110f) / 330f, 0f, 1f);
        return Mathf.Clamp((0.46f + 0.54f * falloff) * BeamFlicker(), 0f, 1f);
    }

    private static Color TorchTint(float level) =>
        new(level, level * 0.96f, level * 0.84f, 1f);

    /// <summary>The faint shaft of the held-torch beam, from the lens
    /// in her hand to the circle on the hedges.</summary>
    private void DrawBeamShaft()
    {
        if (!_hasFlashlight || !_flashlightOn || _handRise >= 0.995f)
            return;

        GetHandPose(out _, out _, out Vector2 palm);
        Vector2 lens = TorchLensPosition(palm);
        float strength = BeamFlicker() * (1f - _handRise);
        var warm = new Color(1f, 0.88f, 0.6f);
        const int discs = 18;
        for (int i = 1; i <= discs; i++)
        {
            float t = i / (float)discs;
            Vector2 position = lens.Lerp(_beamPosition, t);
            float radius = Mathf.Lerp(6f, 66f, t * t * (3f - 2f * t));
            float alpha = Mathf.Lerp(0.1f, 0.022f, t) * strength;
            DrawCircle(position, radius, new Color(warm.R, warm.G, warm.B, alpha));
        }
    }

    /// <summary>Her left hand and Father's flashlight: the same palm
    /// art and palm anchor as every held item, with the flashlight's
    /// own item art resting in it and turned toward the circle.</summary>
    private void DrawHeldFlashlight()
    {
        if (!_hasFlashlight || _handTexture == null || _torchTexture == null || _handRise >= 0.995f)
            return;

        GetHandPose(out Vector2 wrist, out float handAngle, out Vector2 palm);
        float handHeight = HandWidth * HeldHandAspect;
        float level = HandLightLevel(palm);

        DrawSetTransform(wrist, handAngle, Vector2.One);
        DrawTextureRect(_handTexture,
            new Rect2(-HandWidth * 0.5f, -handHeight, HandWidth, handHeight),
            false, TorchTint(level));
        DrawSetTransform(palm, _torchRotation, Vector2.One);
        DrawTextureRect(_torchTexture,
            new Rect2(-TorchIconSize * 0.5f, -TorchIconSize * 0.5f, TorchIconSize, TorchIconSize),
            false, TorchTint(Mathf.Min(1f, level + 0.12f)));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (_flashlightOn)
        {
            float flicker = BeamFlicker();
            Vector2 lens = TorchLensPosition(palm);
            DrawCircle(lens, 14f, new Color(1f, 0.94f, 0.74f, 0.42f * flicker));
            DrawCircle(lens, 7f, new Color(1f, 0.98f, 0.86f, 0.7f * flicker));
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
