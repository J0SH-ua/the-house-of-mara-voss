using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Bottom-of-screen dialogue bubble with a typewriter effect.
///
/// Interaction model (reworked):
///  * Click / Space / Enter once → complete the current line instantly.
///  * Click again → advance. Auto-advance is opt-in via Settings
///    (the legacy per-line `durationInSeconds` becomes the auto-advance hold).
///  * SkipDialogue action (End) → fast-forwards the whole sequence.
///  * Typewriter blips every few characters; at high corruption the text
///    itself occasionally glitches before settling.
///  * Every line is written to the Memory Journal (GameState echoes), so
///    text from freed examine layers can be re-read (press J).
///
/// The public ShowText signature is unchanged — all existing call sites keep
/// working. Calls serialize: a second ShowText waits for the first to finish
/// instead of stacking bubbles.
/// </summary>
public partial class Dialogue : Control
{
    private static int _inputBlockCount = 0;

    // --- sequencing / input state (static: Dialogue instances are ephemeral) ---
    private static int _activeCount;
    private static bool _advanceRequested;
    private static ulong _chainSkipUntilMsec;
    private static ulong _lineStartedMsec;
    private static bool _mouseReleasedSinceLineStart;

    public TextureRect _backgroundRect;
    public Label _dialogueLabel;
    private ColorRect _inputBlocker;
    private Label _advanceIndicator;
    private string _fullText;
    private float _typewriterSpeed = 0.01f; // legacy: seconds per char, only if caller overrides
    private Color _textColor = Colors.Black;
    private string _fontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private int _fontSize = 32;
    private bool _typingDone;

    public static bool IsInputBlocked => _inputBlockCount > 0;
    public static bool IsShowing => _activeCount > 0;

    public static void BeginInputBlock()
    {
        _inputBlockCount++;
    }

    public static void EndInputBlock()
    {
        if (_inputBlockCount > 0)
            _inputBlockCount--;
    }

    // ------------------------------------------------------------ input hooks

    /// <summary>Called by GameUI's root-level _Input (always reachable, unlike
    /// input inside the dialogue's own SubViewport).</summary>
    public static void NotifyAdvancePressed()
    {
        if (!IsShowing)
            return;

        // Ignore the very click that opened the line (same input event) and
        // require the mouse to have been released at least once since.
        if (Time.GetTicksMsec() - _lineStartedMsec < 220)
            return;
        if (!_mouseReleasedSinceLineStart && Input.IsMouseButtonPressed(MouseButton.Left))
            return;

        _advanceRequested = true;
    }

    public static void NotifySkipPressed()
    {
        if (!IsShowing)
            return;

        _advanceRequested = true;
        _chainSkipUntilMsec = Time.GetTicksMsec() + 1500;
    }

    private static bool ChainSkipActive => Time.GetTicksMsec() < _chainSkipUntilMsec;

    public override void _Input(InputEvent @event)
    {
        // Best-effort path when events do reach this SubViewport;
        // GameUI.NotifyAdvancePressed is the guaranteed one.
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            NotifyAdvancePressed();
        else if (@event.IsActionPressed(InputSetup.AdvanceDialogue))
            NotifyAdvancePressed();
        else if (@event.IsActionPressed(InputSetup.SkipDialogue))
            NotifySkipPressed();
    }

    public override void _Process(double delta)
    {
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
            _mouseReleasedSinceLineStart = true;

        if (_advanceIndicator != null)
        {
            _advanceIndicator.Visible = _typingDone;
            if (_typingDone)
            {
                float a = Settings.ReduceMotionOrDefault()
                    ? 0.75f
                    : 0.45f + 0.35f * (float)Math.Sin(Time.GetTicksMsec() / 280.0);
                var m = _advanceIndicator.Modulate;
                m.A = a;
                _advanceIndicator.Modulate = m;
            }
        }
    }

    // ------------------------------------------------------------- construction

    public void Initialize()
    {
        Name = "DialogueUI";
        // Pausable so the pause menu truly freezes mid-line; GameUI (Always)
        // owns all input while paused.
        ProcessMode = ProcessModeEnum.Pausable;
        ZIndex = 100;
        Visible = true;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _inputBlocker = new ColorRect
        {
            Name = "InputBlocker",
            Color = new Color(0, 0, 0, 0),
            MouseFilter = MouseFilterEnum.Stop,
            ProcessMode = ProcessModeEnum.Pausable,
        };
        _inputBlocker.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_inputBlocker);

        _backgroundRect = new TextureRect
        {
            Name = "Background",
            Texture = GD.Load<Texture2D>("res://assets/images/core/bubble_chat.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            AnchorLeft = 0,
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 50,
            OffsetTop = -223,
            OffsetRight = -45,
            OffsetBottom = -123,
            Modulate = new Color(1, 1, 1, 0),
            ProcessMode = ProcessModeEnum.Pausable,
        };
        AddChild(_backgroundRect);

        _dialogueLabel = new Label
        {
            Name = "DialogueText",
            AnchorLeft = 0,
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 50,
            OffsetTop = -233,
            OffsetRight = -27,
            OffsetBottom = -133,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            Modulate = new Color(1, 1, 1, 0),
            ProcessMode = ProcessModeEnum.Pausable,
        };
        AddChild(_dialogueLabel);

        _advanceIndicator = new Label
        {
            Name = "AdvanceIndicator",
            Text = "click to continue",
            AnchorLeft = 0,
            AnchorTop = 1,
            AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 50,
            OffsetTop = -150,
            OffsetRight = -80,
            OffsetBottom = -128,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Modulate = new Color(0.25f, 0.2f, 0.15f, 0),
            Visible = false,
            ProcessMode = ProcessModeEnum.Pausable,
        };
        AddChild(_advanceIndicator);

        var font = GD.Load<FontFile>(_fontPath);
        var theme = new Theme();
        if (font != null)
            theme.SetFont("font", "Label", font);
        theme.SetFontSize("font_size", "Label", _fontSize);
        theme.SetColor("font_color", "Label", _textColor);
        _dialogueLabel.Theme = theme;

        var indicatorTheme = new Theme();
        if (font != null)
            indicatorTheme.SetFont("font", "Label", font);
        indicatorTheme.SetFontSize("font_size", "Label", 17);
        indicatorTheme.SetColor("font_color", "Label", new Color(0.3f, 0.24f, 0.18f));
        _advanceIndicator.Theme = indicatorTheme;
    }

    public override void _Ready() { }

    // ---------------------------------------------------------------- playback

    public static async Task ShowText(
        Node caller,
        string text,
        float durationInSeconds,
        float x,
        float y,
        string fontPath = null,
        int? fontSize = null,
        Color? textColor = null,
        float? typewriterSpeed = null)
    {
        if (caller == null || !GodotObject.IsInstanceValid(caller) || string.IsNullOrWhiteSpace(text))
            return;

        var tree = caller.GetTree();
        var root = tree.Root;

        // Serialize: wait for any line already on screen (prevents stacked
        // bubbles when several handlers fire from the same click).
        while (_activeCount > 0)
        {
            if (!GodotObject.IsInstanceValid(caller))
                return;
            await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        _activeCount++;
        BeginInputBlock();

        var container = new SubViewportContainer
        {
            Name = "DialogueContainer",
            Stretch = true,
            ProcessMode = ProcessModeEnum.Pausable,
        };
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var subViewport = new SubViewport
        {
            Name = "DialogueViewport",
            Size = new Vector2I(1152, 648),
            TransparentBg = true,
        };
        subViewport.SetSize2DOverride(new Vector2I(1152, 648));
        subViewport.SetSize2DOverrideStretch(true);
        container.AddChild(subViewport);

        var dialogue = new Dialogue
        {
            _fullText = text
        };

        if (fontPath != null) dialogue._fontPath = fontPath;
        if (fontSize.HasValue) dialogue._fontSize = fontSize.Value;
        if (textColor.HasValue) dialogue._textColor = textColor.Value;
        if (typewriterSpeed.HasValue) dialogue._typewriterSpeed = typewriterSpeed.Value;

        dialogue.Initialize();
        subViewport.AddChild(dialogue);
        root.AddChild(container);

        _lineStartedMsec = Time.GetTicksMsec();
        _mouseReleasedSinceLineStart = false;
        _advanceRequested = false;
        bool chainSkipped = ChainSkipActive;
        if (chainSkipped)
            _chainSkipUntilMsec = Time.GetTicksMsec() + 1500; // keep the chain alive

        GameState.Instance?.LogEcho(text);

        try
        {
            await dialogue.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            await dialogue.PlayFadeInAnimation(chainSkipped ? 0.05f : 0.3f);
            await dialogue.PlayTypewriterAnimation(chainSkipped);
            await dialogue.WaitForAdvance(durationInSeconds, chainSkipped);
            await dialogue.PlayFadeOutAnimation(chainSkipped ? 0.05f : 0.22f);
        }
        catch (Exception e)
        {
            Logger.Error("[ Dialogue.ShowText ] Exception: " + e.Message);
        }
        finally
        {
            container.QueueFree();
            _activeCount = Math.Max(0, _activeCount - 1);
            EndInputBlock();
        }
    }

    private async Task NextFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        while (GetTree().Paused)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task PlayFadeInAnimation(float duration)
    {
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_backgroundRect, "modulate:a", 1.0f, duration);
        tween.TweenProperty(_dialogueLabel, "modulate:a", 1.0f, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task PlayTypewriterAnimation(bool instant)
    {
        _dialogueLabel.Text = "";
        _dialogueLabel.Visible = true;
        _dialogueLabel.SelfModulate = new Color(1, 1, 1, 1);
        _typingDone = false;

        float cps = Settings.TextSpeedOrDefault();
        if (instant || cps <= 0f)
        {
            _dialogueLabel.Text = _fullText;
            _typingDone = true;
            _advanceRequested = false;
            return;
        }

        double secondsPerChar = 1.0 / cps;
        double accumulator = 0;
        ulong lastMsec = Time.GetTicksMsec();
        int shown = 0;
        int blipCounter = 0;

        float glitchChance = 0f;
        var tier = GameState.Instance?.CurrentTier ?? CorruptionTier.Calm;
        if (tier >= CorruptionTier.Fractured && Settings.CorruptionIntensityOrDefault() > 0f)
            glitchChance = tier == CorruptionTier.Shattered ? 0.06f : 0.03f;
        const string glitchChars = "#%&@$?!";

        while (shown < _fullText.Length)
        {
            if (_advanceRequested)
            {
                // First click: complete the line (don't advance yet).
                _advanceRequested = false;
                _dialogueLabel.Text = _fullText;
                break;
            }

            await NextFrame();
            if (!GodotObject.IsInstanceValid(this))
                return;

            ulong now = Time.GetTicksMsec();
            accumulator += (now - lastMsec) / 1000.0;
            lastMsec = now;

            while (accumulator >= secondsPerChar && shown < _fullText.Length)
            {
                accumulator -= secondsPerChar;
                char c = _fullText[shown];

                if (glitchChance > 0f && !char.IsWhiteSpace(c) && GD.Randf() < glitchChance)
                {
                    // The memory writes it wrong for a beat, then corrects itself.
                    _dialogueLabel.Text += glitchChars[(int)(GD.Randi() % glitchChars.Length)];
                    await NextFrame();
                    if (!GodotObject.IsInstanceValid(this))
                        return;
                    _dialogueLabel.Text = _fullText.Substring(0, shown);
                }

                shown++;
                _dialogueLabel.Text = _fullText.Substring(0, shown);

                if (!char.IsWhiteSpace(c) && ++blipCounter >= 3)
                {
                    blipCounter = 0;
                    AudioManager.PlayVaried(Sfx.DialogueBlip, -19f, 1f, 0.09f);
                }
            }
        }

        _typingDone = true;
    }

    private async Task WaitForAdvance(float legacyHoldSeconds, bool chainSkipped)
    {
        if (chainSkipped)
        {
            await NextFrame();
            return;
        }

        bool auto = Settings.AutoAdvanceOrDefault();
        double hold = auto
            ? Mathf.Clamp(Mathf.Max(legacyHoldSeconds, 0.9f + _fullText.Length * 0.028f), 1.2f, legacyHoldSeconds + 3f)
            : double.PositiveInfinity;

        double waited = 0;
        ulong lastMsec = Time.GetTicksMsec();
        bool playedAdvanceCue = false;

        while (!_advanceRequested && waited < hold)
        {
            await NextFrame();
            if (!GodotObject.IsInstanceValid(this))
                return;

            ulong now = Time.GetTicksMsec();
            waited += (now - lastMsec) / 1000.0;
            lastMsec = now;
        }

        if (_advanceRequested && !playedAdvanceCue)
            AudioManager.Play(Sfx.DialogueAdvance, -9f);

        _advanceRequested = false;
    }

    private async Task PlayFadeOutAnimation(float duration)
    {
        if (!GodotObject.IsInstanceValid(this) || _backgroundRect == null)
            return;

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_backgroundRect, "modulate:a", 0.0f, duration);
        tween.TweenProperty(_dialogueLabel, "modulate:a", 0.0f, duration);
        tween.TweenProperty(_advanceIndicator, "modulate:a", 0.0f, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
    }
}
