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
/// a code-drawn, chunky first-person hedge maze with real WASD
/// movement, running, crouching, jumping and looking behind her.
/// The south break returns to the bridge and the far north-west
/// break continues to the last room, so exploring it can never
/// strand a run while its future puzzles are still being thought
/// about.
/// </summary>
public partial class HedgeMaze : Node2D
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string BridgeScenePath = "res://scenes/world/bridge_repair.tscn";
    private const string Room7ScenePath = "res://scenes/world/room_7.tscn";

    public const string IntroPendingFlag = "hedge_maze_intro_pending";
    public const string AwakenedFlag = "hedge_maze_awakened";
    public const string FarSideReachedFlag = "hedge_maze_far_side_reached";

    private const float ScreenWidth = 1152f;
    private const float ScreenHeight = 648f;
    private const float ColumnWidth = 12f;
    private const int ColumnCount = 96;
    private const float FieldOfView = 1.08f;
    private const float PlayerRadius = 0.23f;
    private const float WalkSpeed = 2.35f;
    private const float RunSpeed = 4f;
    private const float CrouchSpeed = 1.25f;
    private const float ArrowTurnSpeed = 2.35f;

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

    private static bool _pendingFromNorth;

    /// <summary>SceneManager arms the physical end Mara is entering
    /// from immediately before a scene change. Consumed in _Ready.</summary>
    public static void ArmEntry(bool fromNorth) => _pendingFromNorth = fromNorth;

    private FontFile _font;
    private Label _titleLabel;
    private Label _promptLabel;
    private Label _exitLabel;

    private Vector2 _playerPosition = new(13.5f, 17.5f);
    private Vector2 _velocity = Vector2.Zero;
    private float _yaw = -Mathf.Pi / 2f;
    private float _pendingMouseTurn;
    private bool _lookingBack;

    private float _crouchAmount;
    private float _jumpHeight;
    private float _verticalVelocity;
    private bool _grounded = true;
    private float _bobPhase;
    private float _bobAmount;
    private float _footstepTimer;
    private bool _footstepAlternate;

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
    private string _exitHintCache = string.Empty;

    public override void _Ready()
    {
        _font = GD.Load<FontFile>(FontPath);
        bool fromNorth = _pendingFromNorth;
        _pendingFromNorth = false;

        FindSpecialCells();
        PlaceAtEntry(fromNorth);

        var gs = GameState.Instance;
        _playWakeIntro = gs?.HasFlag(IntroPendingFlag) ?? false;
        _hasFlashlight = gs?.HasFlag("item_flashlight") ?? false;
        _flashlightOn = !_playWakeIntro && _hasFlashlight;
        _eyeOpen = _playWakeIntro ? 0f : 1f;

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
        _titleLabel = MakeLabel("Inside the green walls",
            new Vector2(326, 18), new Vector2(500, 40), 28,
            new Color(0.88f, 0.94f, 0.82f, 0.95f));
        _exitLabel = MakeLabel(string.Empty,
            new Vector2(276, 88), new Vector2(600, 36), 21,
            new Color(0.96f, 0.9f, 0.72f, 0.95f));
        _promptLabel = MakeLabel(
            "WASD move · mouse or ←/→ turn · SHIFT run\nCAPS LOCK look behind · C crouch · SPACE jump",
            new Vector2(276, 584), new Vector2(600, 52), 19,
            new Color(0.92f, 0.89f, 0.78f, 0.92f));

        bool labelsVisible = !_playWakeIntro;
        _titleLabel.Visible = labelsVisible;
        _exitLabel.Visible = labelsVisible;
        _promptLabel.Visible = labelsVisible;
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
            ZIndex = 100,
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

                if (_titleLabel != null)
                    _titleLabel.Visible = true;
                if (_exitLabel != null)
                    _exitLabel.Visible = true;
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
                    QueueRedraw();
                    await WaitSeconds(0.3f);
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

    // ------------------------------------------------------------ input

    public override void _Input(InputEvent @event)
    {
        // Do not steal WASD/C/Space from the pause menu, Bag,
        // Journal notes or any other panel opened over the maze.
        if (GetTree().Paused ||
            (GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen()))
            return;

        if (@event is InputEventKey keyEvent)
        {
            bool capsLock = keyEvent.PhysicalKeycode == Key.Capslock ||
                keyEvent.Keycode == Key.Capslock;
            bool shift = keyEvent.PhysicalKeycode == Key.Shift ||
                keyEvent.Keycode == Key.Shift;
            bool movementKey = capsLock || shift ||
                keyEvent.PhysicalKeycode is
                    Key.W or Key.A or Key.S or Key.D or Key.C or Key.Space or
                    Key.Left or Key.Right or Key.Up or Key.Down;

            if (movementKey)
            {
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

                // Space still belongs to a line of dialogue while one
                // is actually on screen. Every other movement key is
                // claimed here so A cannot arm Anchor and C cannot
                // toggle Clarity underneath Mara's feet.
                if (!(keyEvent.PhysicalKeycode == Key.Space && Dialogue.IsShowing))
                    GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion && InputFree())
            _pendingMouseTurn += motion.Relative.X * 0.00165f;
    }

    private bool InputFree() =>
        _exploring && !_leaving && !GetTree().Paused && !SceneManager.IsChanging &&
        !Dialogue.IsShowing && !Dialogue.IsInputBlocked &&
        !(GameUI.Instance != null && GameUI.Instance.IsBlockingOverlayOpen());

    private void TryJump()
    {
        if (!_grounded)
            return;
        _grounded = false;
        _verticalVelocity = 4.7f;
        AudioManager.PlayVaried(Sfx.FootstepDirtB, -16f, 0.82f, 0.03f);
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);

        UpdateCrouch(dt);
        UpdateVertical(dt);
        if (InputFree())
        {
            UpdateMovement(dt);
            CheckExit();
        }
        else
        {
            _pendingMouseTurn = 0f;
            _velocity = _velocity.Lerp(Vector2.Zero,
                Mathf.Clamp(1f - Mathf.Exp(-12f * dt), 0f, 1f));
            _bobAmount = Mathf.MoveToward(_bobAmount, 0f, dt * 5f);
        }

        UpdateExitHint();
        QueueRedraw();
    }

    private void UpdateCrouch(float dt)
    {
        bool wantsCrouch = InputFree() && Input.IsPhysicalKeyPressed(Key.C);
        _crouchAmount = Mathf.MoveToward(_crouchAmount, wantsCrouch ? 1f : 0f, dt * 7f);
    }

    private void UpdateVertical(float dt)
    {
        if (_grounded || GetTree().Paused)
            return;

        _verticalVelocity -= 11.5f * dt;
        _jumpHeight += _verticalVelocity * dt;
        if (_jumpHeight <= 0f)
        {
            _jumpHeight = 0f;
            _verticalVelocity = 0f;
            _grounded = true;
            AudioManager.PlayVaried(Sfx.FootstepDirtA, -14f, 0.88f, 0.03f);
        }
    }

    private void UpdateMovement(float dt)
    {
        if (Input.IsPhysicalKeyPressed(Key.Left))
            _yaw -= ArrowTurnSpeed * dt;
        if (Input.IsPhysicalKeyPressed(Key.Right))
            _yaw += ArrowTurnSpeed * dt;
        _yaw = NormalizeAngle(_yaw + _pendingMouseTurn);
        _pendingMouseTurn = 0f;

        Vector2 mouse = GetGlobalMousePosition();
        if (mouse.Y > 130f)
        {
            float edge = (mouse.X - ScreenWidth * 0.5f) / (ScreenWidth * 0.5f);
            float deadZone = 0.62f;
            if (Mathf.Abs(edge) > deadZone)
            {
                float strength = (Mathf.Abs(edge) - deadZone) / (1f - deadZone);
                float direction = edge > 0f ? 1f : -1f;
                _yaw = NormalizeAngle(_yaw + direction * strength * 1.8f * dt);
            }
        }

        float forwardInput = 0f;
        float strafeInput = 0f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            forwardInput += 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            forwardInput -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D))
            strafeInput += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A))
            strafeInput -= 1f;

        Vector2 forward = new(Mathf.Cos(_yaw), Mathf.Sin(_yaw));
        Vector2 right = new(-forward.Y, forward.X);
        Vector2 desired = forward * forwardInput + right * strafeInput;
        if (desired.LengthSquared() > 1f)
            desired = desired.Normalized();

        bool running = Input.IsKeyPressed(Key.Shift) && _crouchAmount < 0.45f;
        float speed = _crouchAmount >= 0.45f ? CrouchSpeed : running ? RunSpeed : WalkSpeed;
        if (_lookingBack)
            speed *= 0.82f;

        float responsiveness = _grounded ? 11f : 3.5f;
        _velocity = _velocity.Lerp(desired * speed,
            Mathf.Clamp(1f - Mathf.Exp(-responsiveness * dt), 0f, 1f));

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

        float horizontalSpeed = _velocity.Length();
        if (_grounded && horizontalSpeed > 0.22f)
        {
            if (!Settings.ReduceMotionOrDefault())
                _bobPhase += dt * horizontalSpeed * 3.4f;
            _bobAmount = Mathf.MoveToward(_bobAmount, 1f, dt * 5f);
            _footstepTimer -= dt * horizontalSpeed;
            if (_footstepTimer <= 0f)
            {
                _footstepTimer = running ? 1.55f : 1.35f;
                _footstepAlternate = !_footstepAlternate;
                AudioManager.PlayVaried(
                    _footstepAlternate ? Sfx.FootstepDirtA : Sfx.FootstepDirtB,
                    _crouchAmount >= 0.45f ? -19f : running ? -11f : -14f,
                    _crouchAmount >= 0.45f ? 0.82f : running ? 1.08f : 0.96f,
                    0.04f);
            }
        }
        else
        {
            _bobAmount = Mathf.MoveToward(_bobAmount, 0f, dt * 5f);
            _footstepTimer = Mathf.Min(_footstepTimer, 0.2f);
        }
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

    private (float Distance, Vector2I Cell, char Tile, int Side, float WallX) CastRay(float angle)
    {
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
            return (Mathf.Max(distance, 0.001f), cell, tile, side, Mathf.Clamp(wallX, 0f, 0.999f));
        }

        return (40f, new Vector2I(mapX, mapY), '#', side, 0f);
    }

    private float Horizon(Vector2 mouse)
    {
        float bob = Settings.ReduceMotionOrDefault()
            ? 0f
            : Mathf.Sin(_bobPhase * Mathf.Tau) * 4f * _bobAmount;
        float pitch = Mathf.Clamp((mouse.Y - ScreenHeight * 0.5f) * 0.07f, -26f, 26f);
        return ScreenHeight * 0.5f + _crouchAmount * 28f - _jumpHeight * 34f + bob + pitch;
    }

    private float ViewYaw => NormalizeAngle(_yaw + (_lookingBack ? Mathf.Pi : 0f));

    private Vector2 BeamAim(Vector2 mouse, float horizon)
    {
        float x = ScreenWidth * 0.5f + Mathf.Clamp(mouse.X - ScreenWidth * 0.5f, -360f, 360f) * 0.28f;
        float y = horizon + 14f + Mathf.Clamp(mouse.Y - ScreenHeight * 0.5f, -180f, 180f) * 0.14f;
        return new Vector2(x, y);
    }

    private Vector2 TorchLensPosition()
    {
        float bob = Settings.ReduceMotionOrDefault()
            ? 0f
            : Mathf.Sin(_bobPhase * Mathf.Tau + 0.7f) * 3f * _bobAmount;
        return new Vector2(694f, 548f + _crouchAmount * 18f - _jumpHeight * 10f + bob);
    }

    public override void _Draw()
    {
        Vector2 mouse = GetGlobalMousePosition();
        float viewYaw = ViewYaw;
        float horizon = Horizon(mouse);
        Vector2 aim = BeamAim(mouse, horizon);

        DrawSky(viewYaw, horizon);
        DrawGround(viewYaw, horizon, aim);
        DrawWalls(viewYaw, horizon, aim);
        DrawFlashlightGlow(aim);
        DrawTorch();
        DrawVignette();
        DrawEyelids();

        DrawRect(new Rect2(0, 0, ScreenWidth, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 626, ScreenWidth, 22), new Color(0, 0, 0, 0.28f), true);
        DrawRect(new Rect2(0, 0, 24, ScreenHeight), new Color(0, 0, 0, 0.22f), true);
        DrawRect(new Rect2(1128, 0, 24, ScreenHeight), new Color(0, 0, 0, 0.22f), true);
    }

    private void DrawSky(float viewYaw, float horizon)
    {
        for (float y = 0f; y < horizon; y += 16f)
        {
            float t = Mathf.Clamp(y / Mathf.Max(horizon, 1f), 0f, 1f);
            var color = new Color(
                Mathf.Lerp(0.004f, 0.025f, t),
                Mathf.Lerp(0.008f, 0.045f, t),
                Mathf.Lerp(0.02f, 0.085f, t));
            DrawRect(new Rect2(0, y, ScreenWidth, Mathf.Min(16f, horizon - y)), color, true);
        }

        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        for (int i = 0; i < 105; i++)
        {
            float starYaw = i * 2.399963f;
            float relative = NormalizeAngle(starYaw - viewYaw);
            if (Mathf.Abs(relative) > FieldOfView * 0.56f)
                continue;
            float x = ScreenWidth * 0.5f + Mathf.Tan(relative) / tanHalfFov * ScreenWidth * 0.5f;
            float y = 18f + Hash01(i, 17, 91) * Mathf.Max(30f, horizon - 70f);
            float alpha = 0.25f + 0.55f * Hash01(i, 43, 7);
            DrawRect(new Rect2(x, y, 2f, 2f), new Color(0.85f, 0.9f, 1f, alpha), true);
        }

        float moonRelative = NormalizeAngle(2.45f - viewYaw);
        if (Mathf.Abs(moonRelative) < FieldOfView * 0.55f)
        {
            float moonX = ScreenWidth * 0.5f +
                Mathf.Tan(moonRelative) / tanHalfFov * ScreenWidth * 0.5f;
            var moon = new Vector2(moonX, 88f);
            DrawCircle(moon, 31f, new Color(0.72f, 0.8f, 0.92f, 0.13f));
            DrawCircle(moon, 19f, new Color(0.78f, 0.84f, 0.92f, 0.72f));
            DrawCircle(moon + new Vector2(7, -3), 15f, new Color(0.018f, 0.03f, 0.055f, 0.82f));
        }
    }

    private void DrawGround(float viewYaw, float horizon, Vector2 aim)
    {
        for (float y = horizon; y < ScreenHeight; y += 12f)
        {
            float t = Mathf.Clamp((y - horizon) / Mathf.Max(ScreenHeight - horizon, 1f), 0f, 1f);
            var color = new Color(
                Mathf.Lerp(0.008f, 0.035f, t),
                Mathf.Lerp(0.018f, 0.065f, t),
                Mathf.Lerp(0.012f, 0.035f, t));
            DrawRect(new Rect2(0, y, ScreenWidth, Mathf.Min(12f, ScreenHeight - y)), color, true);
        }

        Vector2 dir = new(Mathf.Cos(viewYaw), Mathf.Sin(viewYaw));
        Vector2 plane = new(-dir.Y, dir.X);
        plane *= Mathf.Tan(FieldOfView * 0.5f);

        for (float y = horizon + 18f; y < ScreenHeight; y += 18f)
        {
            float rowDistance = 190f / Mathf.Max(y - horizon, 1f);
            Vector2 left = _playerPosition + (dir - plane) * rowDistance;
            Vector2 right = _playerPosition + (dir + plane) * rowDistance;

            for (int column = 0; column < ColumnCount; column += 3)
            {
                Vector2 sample = left.Lerp(right, (column + 0.5f) / ColumnCount);
                var cell = new Vector2I(Mathf.FloorToInt(sample.X), Mathf.FloorToInt(sample.Y));
                if (IsSolid(CellAt(cell)))
                    continue;

                int subX = Mathf.FloorToInt(sample.X * 3f);
                int subY = Mathf.FloorToInt(sample.Y * 3f);
                float hash = Hash01(subX, subY, 71);
                if (hash < 0.78f)
                    continue;

                float screenX = column * ColumnWidth + Hash01(subX, subY, 13) * 22f;
                float size = Mathf.Clamp(10f / Mathf.Max(rowDistance, 0.35f), 2f, 10f);
                float light = GroundLight(screenX, rowDistance, aim);
                Color detail = hash > 0.92f
                    ? ScaleColor(new Color(0.32f, 0.3f, 0.25f), light)
                    : ScaleColor(new Color(0.08f, 0.24f, 0.09f), light);
                DrawRect(new Rect2(screenX, y + Hash01(subX, subY, 29) * 7f, size, Mathf.Max(2f, size * 0.55f)),
                    detail, true);
            }
        }
    }

    private float GroundLight(float screenX, float distance, Vector2 aim)
    {
        float moon = 0.18f + 0.12f * Mathf.Clamp(1f - distance / 14f, 0f, 1f);
        if (!_flashlightOn)
            return moon;
        float beam = Mathf.Clamp(1.08f - Mathf.Abs(screenX - aim.X) / 360f, 0f, 1f);
        float reach = Mathf.Clamp(1.2f - distance / 10.5f, 0f, 1f);
        return Mathf.Clamp(moon + beam * reach * 0.95f, 0f, 1.15f);
    }

    private void DrawWalls(float viewYaw, float horizon, Vector2 aim)
    {
        float tanHalfFov = Mathf.Tan(FieldOfView * 0.5f);
        float flicker = Settings.ReduceMotionOrDefault()
            ? 1f
            : 0.96f + 0.04f * Mathf.Sin(Time.GetTicksMsec() / 61f);

        for (int column = 0; column < ColumnCount; column++)
        {
            float screenX = column * ColumnWidth;
            float centerX = screenX + ColumnWidth * 0.5f;
            float cameraX = 2f * centerX / ScreenWidth - 1f;
            float rayAngle = viewYaw + Mathf.Atan(cameraX * tanHalfFov);
            var hit = CastRay(rayAngle);
            float correctedDistance = Mathf.Max(
                hit.Distance * Mathf.Cos(rayAngle - viewYaw), 0.05f);

            float heightFactor = hit.Tile switch
            {
                'F' => 0.48f,
                'P' => 1.08f,
                'T' => 1.42f,
                _ => 1.28f,
            };
            float groundLineHeight = 680f / correctedDistance;
            float lineHeight = groundLineHeight * heightFactor;
            float bottom = horizon + groundLineHeight * 0.37f;
            float top = bottom - lineHeight * 1.06f;

            float beam = _flashlightOn
                ? Mathf.Clamp(1.12f - Mathf.Abs(centerX - aim.X) / 370f, 0f, 1f) * flicker
                : 0f;
            float reach = Mathf.Clamp(1.24f - correctedDistance / 10.5f, 0f, 1f);
            float moon = hit.Side == 0 ? 0.2f : 0.15f;
            float light = Mathf.Clamp(moon + beam * reach, 0.08f, 1.12f);

            Color baseColor = hit.Tile switch
            {
                'P' => new Color(0.34f, 0.35f, 0.34f),
                'F' => new Color(0.28f, 0.36f, 0.4f),
                'T' => new Color(0.035f, 0.24f, 0.07f),
                _ => hit.Side == 0
                    ? new Color(0.045f, 0.27f, 0.075f)
                    : new Color(0.03f, 0.2f, 0.055f),
            };
            Color wall = ScaleColor(baseColor, light);
            DrawRect(new Rect2(screenX, top, ColumnWidth + 1f, bottom - top), wall, true);

            if (hit.Tile is '#' or 'T')
                DrawLeafDetail(screenX, top, bottom, hit.Cell, hit.WallX, light, hit.Tile);
            else if (hit.Tile == 'P')
                DrawStoneDetail(screenX, top, bottom, hit.Cell, light);
            else if (hit.Tile == 'F')
            {
                float waterY = top + Mathf.Min(18f, lineHeight * 0.14f);
                DrawRect(new Rect2(screenX, waterY, ColumnWidth + 1f, 4f),
                    ScaleColor(new Color(0.35f, 0.55f, 0.68f), light), true);
            }
        }
    }

    private void DrawLeafDetail(float screenX, float top, float bottom, Vector2I cell,
        float wallX, float light, char tile)
    {
        float height = bottom - top;
        if (height <= 8f)
            return;

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

    private void DrawFlashlightGlow(Vector2 aim)
    {
        if (!_flashlightOn)
            return;

        Vector2 lens = TorchLensPosition();
        var warm = new Color(1f, 0.88f, 0.6f);
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            Vector2 pos = lens.Lerp(aim, t);
            float radius = Mathf.Lerp(7f, 92f, t * t * (3f - 2f * t));
            DrawCircle(pos, radius, new Color(warm.R, warm.G, warm.B, Mathf.Lerp(0.1f, 0.025f, t)));
        }

        DrawSetTransform(aim, 0f, new Vector2(1f, 0.78f));
        DrawCircle(Vector2.Zero, 190f, new Color(warm.R, warm.G, warm.B, 0.055f));
        DrawCircle(Vector2.Zero, 145f, new Color(warm.R, warm.G, warm.B, 0.07f));
        DrawCircle(Vector2.Zero, 100f, new Color(warm.R, warm.G, warm.B, 0.085f));
        DrawCircle(Vector2.Zero, 56f, new Color(1f, 0.93f, 0.72f, 0.1f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawTorch()
    {
        if (!_hasFlashlight)
            return;

        Vector2 lens = TorchLensPosition();
        DrawSetTransform(lens, -0.42f, Vector2.One);
        var body = new Rect2(-15, 0, 30, 132);
        var head = new Rect2(-22, -25, 44, 29);
        DrawRect(body, new Color(0.035f, 0.035f, 0.04f, 0.98f), true);
        DrawRect(body, new Color(0.32f, 0.32f, 0.3f, 0.9f), false, 2f);
        DrawRect(head, new Color(0.11f, 0.11f, 0.12f, 0.99f), true);
        DrawRect(head, new Color(0.45f, 0.43f, 0.38f, 0.95f), false, 2f);
        DrawRect(new Rect2(-17, -21, 34, 15),
            _flashlightOn
                ? new Color(1f, 0.9f, 0.62f, 0.98f)
                : new Color(0.16f, 0.18f, 0.2f, 0.98f), true);
        DrawRect(new Rect2(-15, 38, 30, 7), new Color(0.38f, 0.3f, 0.18f, 0.9f), true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawVignette()
    {
        float edgeAlpha = _flashlightOn ? 0.34f : 0.42f;
        var edge = new Color(0, 0, 0, edgeAlpha);
        DrawRect(new Rect2(0, 0, ScreenWidth, 58), edge, true);
        DrawRect(new Rect2(0, 590, ScreenWidth, 58), edge, true);
        DrawRect(new Rect2(0, 0, 72, ScreenHeight), edge, true);
        DrawRect(new Rect2(1080, 0, 72, ScreenHeight), edge, true);
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
