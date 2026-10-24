using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The whole overlay layer of the game (autoload, CanvasLayer 90, Always):
///  * HUD — current objective, clarity pips, memory-integrity meter, shards.
///  * Corruption screen overlay (shader) driven by GameState corruption.
///  * Pause menu, Settings panel, Memory Journal (fragments + echoes), toasts.
///  * Global input: pause/journal/hint/clarity keys, held-Tab hotspot reveal,
///    and dialogue advance forwarding (Dialogue lives in a SubViewport whose
///    _Input is not guaranteed to receive root input, so we forward clicks).
/// Everything is built in code so no .tscn surgery was needed. Context-aware:
/// HUD only inside the GameManager scene, a lone Settings button on the main
/// menu, nothing over the intro cinematic.
/// </summary>
public partial class GameUI : CanvasLayer
{
    public static GameUI Instance { get; private set; }

    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const string CorruptionShaderPath = "res://assets/shaders/corruption.gdshader";
    private const string MainMenuScenePath = "res://scenes/ui/main_menu.tscn";

    private Font _font;

    // HUD
    private Control _hud;
    private Label _objectiveLabel;
    private Label _integrityLabel;
    private ProgressBar _integrityBar;
    private HBoxContainer _clarityPips;
    private Label _shardLabel;
    private Label _toastLabel;
    private Tween _toastTween;
    private double _toastUntilMsec;

    // Overlay
    private ColorRect _corruptionRect;
    private ShaderMaterial _corruptionMaterial;

    // Panels
    private Control _pausePanel;
    private Control _settingsPanel;
    private Control _journalPanel;
    private Control _menuSettingsButton;
    private ConfirmationDialog _confirmDialog;
    private Action _confirmAction;

    // Journal widgets
    private VBoxContainer _journalList;
    private RichTextLabel _journalBody;
    private Label _journalStats;
    private bool _journalShowingEchoes;

    // Settings widgets (refreshed on open)
    private readonly List<(HSlider slider, Action<float> apply)> _settingsRefreshers = new();

    private bool _hotspotsHeld;
    private double _contextCheckTimer;
    private enum UiContext { Unknown, Intro, Menu, Game }
    private UiContext _context = UiContext.Unknown;

    // =============================================================== lifecycle

    public override void _Ready()
    {
        Instance = this;
        Layer = 90;
        ProcessMode = ProcessModeEnum.Always;
        _font = GD.Load<FontFile>(FontPath);

        BuildCorruptionOverlay();
        BuildHud();
        BuildPausePanel();
        BuildSettingsPanel();
        BuildJournalPanel();
        BuildMenuButton();

        _confirmDialog = new ConfirmationDialog
        {
            ProcessMode = ProcessModeEnum.Always,
            OkButtonText = "Yes, let it go",
            CancelButtonText = "Not yet",
        };
        _confirmDialog.Confirmed += () => _confirmAction?.Invoke();
        AddChild(_confirmDialog);

        if (GameState.Instance != null)
        {
            var gs = GameState.Instance;
            gs.CorruptionChanged += (_, _) => RefreshHud();
            gs.ClarityChanged += _ => RefreshHud();
            gs.ShardsChanged += RefreshHud;
            gs.ObjectiveChanged += RefreshHud;
            gs.ToastRequested += ShowToast;
            gs.Saved += () => ShowToast("Memory preserved.", quiet: true);
        }
        RoomExamineTracker.ExamineRecorded += (_, _) => RefreshHud();
        SceneManager.SceneChanged += _ =>
        {
            Hotspots.HideAll();
            ClaritySystem.RefreshToggleTiles();
            RefreshHud();
        };
        if (Settings.Instance != null)
            Settings.Instance.Changed += RefreshCorruptionShader;

        RefreshHud();
        RefreshCorruptionShader();
        UpdateContext(force: true);
        Logger.Info("GameUI ready.");
    }

    public override void _Process(double delta)
    {
        _contextCheckTimer -= delta;
        if (_contextCheckTimer <= 0)
        {
            _contextCheckTimer = 0.3;
            UpdateContext();
        }

        // Corruption overlay follows the live value, easing toward it.
        if (_corruptionMaterial != null && _corruptionRect.Visible)
        {
            float current = (float)_corruptionMaterial.GetShaderParameter("intensity").AsDouble();
            float target = TargetCorruptionIntensity();
            float next = Mathf.MoveToward(current, target, (float)delta * 0.25f);
            if (Mathf.Abs(next - current) > 0.0005f)
                _corruptionMaterial.SetShaderParameter("intensity", next);
        }

        // Held-Tab hotspot reveal.
        bool wantHotspots = _context == UiContext.Game
                            && !GetTree().Paused
                            && !Dialogue.IsShowing
                            && Settings.HotspotHintsOrDefault()
                            && Input.IsActionPressed(InputSetup.RevealHotspots)
                            && !FocusIsTextEditing();
        if (wantHotspots && !_hotspotsHeld)
            Hotspots.RevealAll();
        else if (!wantHotspots && _hotspotsHeld)
            Hotspots.HideAll();
        _hotspotsHeld = wantHotspots;
        Hotspots.ValidateScene();

        if (_toastLabel.Visible && Time.GetTicksMsec() > (ulong)_toastUntilMsec)
            FadeToastOut();
    }

    private float TargetCorruptionIntensity()
    {
        if (_context != UiContext.Game || GameState.Instance == null)
            return 0f;

        float v = GameState.Instance.Corruption01 * Settings.CorruptionIntensityOrDefault();
        if (Settings.ReduceMotionOrDefault())
            v *= 0.45f;
        return Mathf.Clamp(v, 0f, 1f);
    }

    // =================================================================== input

    public override void _Input(InputEvent @event)
    {
        // Forward dialogue advance/skip (guaranteed path; see Dialogue docs).
        if (Dialogue.IsShowing)
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
                && !AnyPanelOpen() && !GetTree().Paused)
                Dialogue.NotifyAdvancePressed();
            else if (@event.IsActionPressed(InputSetup.AdvanceDialogue) && !GetTree().Paused)
                Dialogue.NotifyAdvancePressed();
            else if (@event.IsActionPressed(InputSetup.SkipDialogue) && !GetTree().Paused)
                Dialogue.NotifySkipPressed();
        }

        // Right-click releases Clarity from anywhere (ExamineHandler also does
        // this over tiles; this covers clicks over empty space/UI).
        if (_context == UiContext.Game && ClaritySystem.IsArmed
            && @event is InputEventMouseButton rmb && rmb.Pressed && rmb.ButtonIndex == MouseButton.Right)
        {
            ClaritySystem.Disarm();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_context != UiContext.Game)
            return;

        if (@event.IsActionPressed(InputSetup.PauseGame))
        {
            HandleEscape();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (GetTree().Paused)
            return;

        if (@event.IsActionPressed(InputSetup.OpenJournal) && !FocusIsTextEditing())
        {
            OpenJournal();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.ToggleClarity) && !FocusIsTextEditing())
        {
            if (!Dialogue.IsShowing)
                ClaritySystem.Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputSetup.RequestHint) && !FocusIsTextEditing())
        {
            RequestHint();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleEscape()
    {
        if (_settingsPanel.Visible)
        {
            CloseSettings(backToPause: _pausePanel.Visible);
            return;
        }
        if (_journalPanel.Visible)
        {
            CloseJournal(backToPause: false);
            if (_openedJournalFromPause)
            {
                _pausePanel.Visible = true;
                _openedJournalFromPause = false;
            }
            return;
        }
        if (ClaritySystem.IsArmed && !GetTree().Paused)
        {
            // First Esc releases Clarity; it takes a second Esc to pause.
            ClaritySystem.Disarm();
            return;
        }

        if (GetTree().Paused)
            ResumeGame();
        else
            PauseGame();
    }

    private bool _openedJournalFromPause;
    private bool _openedSettingsFromPause;

    private bool FocusIsTextEditing()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        return focus is LineEdit or TextEdit or SpinBox;
    }

    private bool AnyPanelOpen()
    {
        return (_pausePanel?.Visible ?? false) || (_settingsPanel?.Visible ?? false)
               || (_journalPanel?.Visible ?? false);
    }

    private void RequestHint()
    {
        if (GameState.Instance == null || Dialogue.IsShowing)
            return;

        var (text, objectToPulse) = GameState.Instance.RequestHint();
        AudioManager.Play(Sfx.PageTurn, -9f);
        ShowToast(text, seconds: 5.5f);
        if (!string.IsNullOrEmpty(objectToPulse))
            Hotspots.RevealObject(objectToPulse);
    }

    // ================================================================= context

    private void UpdateContext(bool force = false)
    {
        string sceneFile = GetTree().CurrentScene?.SceneFilePath ?? string.Empty;
        UiContext next = sceneFile switch
        {
            "res://scenes/ui/main_menu.tscn" => UiContext.Menu,
            "res://scenes/managers/game_manager.tscn" => UiContext.Game,
            _ => UiContext.Intro,
        };

        if (!force && next == _context)
            return;
        _context = next;

        _hud.Visible = next == UiContext.Game;
        _corruptionRect.Visible = next == UiContext.Game;
        _menuSettingsButton.Visible = next == UiContext.Menu;

        if (next != UiContext.Game)
        {
            Hotspots.HideAll();
            if (GetTree().Paused)
                ResumeGame();
            _pausePanel.Visible = false;
            _journalPanel.Visible = false;
            if (next != UiContext.Menu)
                _settingsPanel.Visible = false;
        }

        RefreshHud();
    }

    // ================================================================ pause UI

    public void PauseGame()
    {
        if (_context != UiContext.Game)
            return;

        GameState.Instance?.SaveGame("pause");
        GetTree().Paused = true;
        _pausePanel.Visible = true;
        AudioManager.Play(Sfx.UiClick, -6f);
        AudioManager.Instance?.SetPausedDuck(true);
        (_pausePanel.GetNodeOrNull<BaseButton>("Center/Panel/VBox/ResumeButton"))?.GrabFocus();
    }

    public void ResumeGame()
    {
        GetTree().Paused = false;
        _pausePanel.Visible = false;
        _journalPanel.Visible = false;
        _settingsPanel.Visible = false;
        AudioManager.Instance?.SetPausedDuck(false);
    }

    private void QuitToMenu()
    {
        GameState.Instance?.SaveGame("quit to menu");
        ClaritySystem.Disarm(silent: true);
        ResumeGame();
        AudioManager.Instance?.StopAmbience();
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    // ============================================================== settings UI

    public void OpenSettings()
    {
        _openedSettingsFromPause = _pausePanel.Visible;
        if (_pausePanel.Visible)
            _pausePanel.Visible = false;

        foreach (var (slider, _) in _settingsRefreshers)
            slider.SetValueNoSignal(slider.GetMeta("refresh").AsDouble());

        _settingsPanel.Visible = true;
        AudioManager.Play(Sfx.UiClick, -6f);
    }

    private void CloseSettings(bool backToPause)
    {
        _settingsPanel.Visible = false;
        Settings.Instance?.Save();
        AudioManager.Play(Sfx.UiBack, -6f);
        if (backToPause)
            _pausePanel.Visible = true;
        _openedSettingsFromPause = false;
    }

    // =============================================================== journal UI

    public void OpenJournal(bool fromPause = false)
    {
        _openedJournalFromPause = fromPause;
        if (fromPause)
            _pausePanel.Visible = false;

        if (!fromPause && _context == UiContext.Game)
            GetTree().Paused = true;

        _journalPanel.Visible = true;
        AudioManager.Play(Sfx.PageTurn, -7f);
        RefreshJournal();
    }

    private void CloseJournal(bool backToPause)
    {
        _journalPanel.Visible = false;
        AudioManager.Play(Sfx.UiBack, -6f);
        if (!backToPause && !_pausePanel.Visible && _context == UiContext.Game)
            GetTree().Paused = false;
    }

    private void RefreshJournal()
    {
        foreach (var child in _journalList.GetChildren())
            child.QueueFree();

        var gs = GameState.Instance;
        _journalStats.Text = gs?.StatsSummary() ?? string.Empty;

        if (!_journalShowingEchoes)
        {
            for (int i = 0; i < GameState.MaxShards; i++)
            {
                int index = i;
                bool recovered = gs != null && (Globals.SHARDS_COLLECTED[index]
                                                || Array.IndexOf(gs.PlacedShardBySlot, index) >= 0);
                string title = GameState.ShardTitles[index];
                var btn = MakeButton(recovered ? title : "· · ·");
                btn.Disabled = !recovered;
                btn.Pressed += () =>
                {
                    AudioManager.Play(Sfx.PageTurn, -10f);
                    string body = gs != null && !string.IsNullOrWhiteSpace(gs.ShardFragments[index])
                        ? gs.ShardFragments[index]
                        : "Recovered, but its words are still surfacing.";
                    string placed = gs != null && Array.IndexOf(gs.PlacedShardBySlot, index) >= 0
                        ? "\n\n[ Returned to the house. ]"
                        : "\n\n[ Carried. ]";
                    _journalBody.Text = $"[b]{title}[/b]\n\n{body}{placed}";
                };
                _journalList.AddChild(btn);
            }
            _journalBody.Text = "[i]Choose a fragment. What was recovered can be re-read; " +
                                "what is still missing keeps its silence.[/i]";
        }
        else
        {
            var btnAll = MakeButton("All echoes");
            btnAll.Pressed += () => ShowEchoes(gs, null);
            _journalList.AddChild(btnAll);
            ShowEchoes(gs, null);
        }
    }

    private void ShowEchoes(GameState gs, int? _)
    {
        if (gs == null || gs.EchoLog.Count == 0)
        {
            _journalBody.Text = "[i]Nothing has been said in this house yet.[/i]";
            return;
        }

        var lines = new List<string>();
        for (int i = gs.EchoLog.Count - 1; i >= 0; i--)
            lines.Add("— " + gs.EchoLog[i]);
        _journalBody.Text = string.Join("\n\n", lines);
        _journalBody.ScrollToLine(0);
    }

    // ================================================================== toasts

    public void ShowToast(string text) => ShowToast(text, 3.4f, false);

    public void ShowToast(string text, float seconds = 3.4f, bool quiet = false)
    {
        if (_toastLabel == null || string.IsNullOrWhiteSpace(text) || _context != UiContext.Game)
            return;

        _toastLabel.Text = text;
        _toastLabel.Visible = true;
        var m = _toastLabel.Modulate;
        m.A = quiet ? 0.55f : 0.95f;
        _toastLabel.Modulate = m;
        _toastUntilMsec = Time.GetTicksMsec() + (double)(seconds * 1000);
        _toastTween?.Kill();
    }

    private void FadeToastOut()
    {
        _toastTween?.Kill();
        _toastTween = CreateTween();
        _toastTween.TweenProperty(_toastLabel, "modulate:a", 0f, 0.5f);
        _toastTween.TweenCallback(Callable.From(() => _toastLabel.Visible = false));
    }

    // ================================================================= confirm

    public void ShowConfirm(string text, Action onConfirm)
    {
        _confirmDialog.DialogText = text;
        _confirmAction = onConfirm;
        _confirmDialog.PopupCentered();
    }

    // ===================================================================== HUD

    private void RefreshHud()
    {
        if (_objectiveLabel == null)
            return;

        var gs = GameState.Instance;
        _objectiveLabel.Text = (gs?.GetCurrentObjective() ?? string.Empty);
        _objectiveLabel.GetParent<Control>().Visible = !string.IsNullOrWhiteSpace(_objectiveLabel.Text);

        // Clarity pips (ColorRects — no font-glyph roulette).
        int clarity = gs?.ClarityCount ?? 0;
        for (int i = 0; i < _clarityPips.GetChildCount(); i++)
        {
            if (_clarityPips.GetChild(i) is ColorRect pip)
                pip.Color = i < clarity
                    ? new Color(0.95f, 0.87f, 0.65f, 0.95f)
                    : new Color(0.4f, 0.36f, 0.3f, 0.5f);
        }

        var tier = gs?.CurrentTier ?? CorruptionTier.Calm;
        _integrityLabel.Text = "MEMORY — " + GameState.TierDisplayName(tier).ToUpperInvariant();
        _integrityBar.Value = 1f - (gs?.Corruption01 ?? 0f);

        _shardLabel.Text = gs != null
            ? $"Fragments  {gs.CountRecovered()}/{GameState.MaxShards}" +
              (gs.CountPlaced() > 0 ? $"   ·   Returned {gs.CountPlaced()}" : "")
            : string.Empty;
    }

    private void RefreshCorruptionShader()
    {
        if (_corruptionMaterial != null)
            _corruptionMaterial.SetShaderParameter("frozen", Settings.ReduceMotionOrDefault() ? 1f : 0f);
    }

    // ============================================================ construction

    private Label MakeLabel(string text, int size = 20, Color? color = null)
    {
        var label = new Label { Text = text };
        if (_font != null)
            label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? new Color(0.93f, 0.89f, 0.82f, 0.95f));
        return label;
    }

    private Button MakeButton(string text, int size = 24)
    {
        var b = new Button { Text = text };
        if (_font != null)
            b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.96f, 0.85f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.38f));

        var flat = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.06f, 0.0f),
        };
        flat.SetContentMarginAll(6);
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.75f, 0.55f, 0.14f),
            BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.35f),
        };
        hover.SetContentMarginAll(6);
        hover.SetBorderWidthAll(1);
        b.AddThemeStyleboxOverride("normal", flat);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", hover);

        b.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -14f);
        b.Pressed += () => AudioManager.Play(Sfx.UiClick, -7f);
        return b;
    }

    private static PanelContainer MakePanel()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.92f),
            BorderColor = new Color(0.72f, 0.62f, 0.45f, 0.55f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginLeft = 28;
        sb.ContentMarginRight = 28;
        sb.ContentMarginTop = 22;
        sb.ContentMarginBottom = 22;

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", sb);
        return panel;
    }

    private static Control DimBackdrop()
    {
        var rect = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }

    private void BuildCorruptionOverlay()
    {
        _corruptionRect = new ColorRect
        {
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _corruptionRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var shader = GD.Load<Shader>(CorruptionShaderPath);
        if (shader != null)
        {
            _corruptionMaterial = new ShaderMaterial { Shader = shader };
            _corruptionMaterial.SetShaderParameter("intensity", 0f);
            _corruptionMaterial.SetShaderParameter("frozen", 0f);
            _corruptionRect.Material = _corruptionMaterial;
        }
        else
        {
            Logger.Warning($"GameUI: corruption shader missing at {CorruptionShaderPath}");
        }

        AddChild(_corruptionRect);
    }

    private void BuildHud()
    {
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_hud);

        // Objective (top-left, parchment-dark chip).
        var objectivePanel = MakePanel();
        objectivePanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        objectivePanel.OffsetLeft = 16;
        objectivePanel.OffsetTop = 14;
        objectivePanel.OffsetRight = 560;
        objectivePanel.OffsetBottom = 66;
        objectivePanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _objectiveLabel = MakeLabel("", 21);
        _objectiveLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        objectivePanel.AddChild(_objectiveLabel);
        _hud.AddChild(objectivePanel);

        // Integrity + clarity (top-right).
        var statusPanel = MakePanel();
        statusPanel.AnchorLeft = 1;
        statusPanel.AnchorRight = 1;
        statusPanel.OffsetLeft = -316;
        statusPanel.OffsetRight = -16;
        statusPanel.OffsetTop = 14;
        statusPanel.OffsetBottom = 132;
        statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var statusBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        statusBox.AddThemeConstantOverride("separation", 5);
        _integrityLabel = MakeLabel("MEMORY — INTACT", 19);
        statusBox.AddChild(_integrityLabel);

        _integrityBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(236, 7),
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var barBg = new StyleBoxFlat { BgColor = new Color(0.25f, 0.22f, 0.18f, 0.8f) };
        var barFill = new StyleBoxFlat { BgColor = new Color(0.82f, 0.71f, 0.5f, 0.95f) };
        _integrityBar.AddThemeStyleboxOverride("background", barBg);
        _integrityBar.AddThemeStyleboxOverride("fill", barFill);
        statusBox.AddChild(_integrityBar);

        var clarityRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        clarityRow.AddThemeConstantOverride("separation", 8);
        clarityRow.AddChild(MakeLabel("Clarity", 19));
        _clarityPips = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _clarityPips.AddThemeConstantOverride("separation", 5);
        for (int i = 0; i < Globals.MaxClarity; i++)
        {
            _clarityPips.AddChild(new ColorRect
            {
                CustomMinimumSize = new Vector2(13, 13),
                Color = new Color(0.95f, 0.87f, 0.65f, 0.95f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        clarityRow.AddChild(_clarityPips);
        statusBox.AddChild(clarityRow);

        _shardLabel = MakeLabel("", 19);
        statusBox.AddChild(_shardLabel);
        statusPanel.AddChild(statusBox);
        _hud.AddChild(statusPanel);

        // Toast (top-centre, under nothing else — dialogue owns the bottom).
        _toastLabel = MakeLabel("", 23, new Color(0.96f, 0.9f, 0.78f, 0.95f));
        _toastLabel.AnchorLeft = 0.5f;
        _toastLabel.AnchorRight = 0.5f;
        _toastLabel.OffsetLeft = -420;
        _toastLabel.OffsetRight = 420;
        _toastLabel.OffsetTop = 148;
        _toastLabel.OffsetBottom = 196;
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toastLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _toastLabel.Visible = false;
        _toastLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hud.AddChild(_toastLabel);

        // Controls reminder (bottom-left, quiet).
        var controls = MakeLabel("Hold Tab — look      C — clarity      H — hint      J — journal      Esc — pause",
            16, new Color(0.85f, 0.8f, 0.72f, 0.5f));
        controls.AnchorTop = 1;
        controls.AnchorBottom = 1;
        controls.OffsetLeft = 16;
        controls.OffsetTop = -30;
        controls.OffsetBottom = -8;
        controls.OffsetRight = 700;
        controls.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hud.AddChild(controls);
    }

    private void BuildPausePanel()
    {
        _pausePanel = new Control { Visible = false };
        _pausePanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pausePanel.AddChild(DimBackdrop());

        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pausePanel.AddChild(center);

        var panel = MakePanel();
        panel.Name = "Panel";
        panel.CustomMinimumSize = new Vector2(380, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = MakeLabel("The house holds its breath.", 30);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        var resume = MakeButton("Resume");
        resume.Name = "ResumeButton";
        resume.Pressed += ResumeGame;
        vbox.AddChild(resume);

        var journal = MakeButton("Memory Journal");
        journal.Pressed += () => OpenJournal(fromPause: true);
        vbox.AddChild(journal);

        var settings = MakeButton("Settings");
        settings.Pressed += OpenSettings;
        vbox.AddChild(settings);

        var save = MakeButton("Preserve Memory (Save)");
        save.Pressed += () =>
        {
            GameState.Instance?.SaveGame("manual");
            ShowToast("Memory preserved.");
        };
        vbox.AddChild(save);

        var quit = MakeButton("Leave to Main Menu");
        quit.Pressed += QuitToMenu;
        vbox.AddChild(quit);

        AddChild(_pausePanel);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = new Control { Visible = false };
        _settingsPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _settingsPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _settingsPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(540, 0);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 9);
        panel.AddChild(vbox);

        var title = MakeLabel("Settings", 32);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        AddVolumeSlider(vbox, "Master", () => Settings.Instance.MasterVolume, v => Settings.Instance.MasterVolume = v);
        AddVolumeSlider(vbox, "Music", () => Settings.Instance.MusicVolume, v => Settings.Instance.MusicVolume = v);
        AddVolumeSlider(vbox, "Sounds", () => Settings.Instance.SfxVolume, v => Settings.Instance.SfxVolume = v);
        AddVolumeSlider(vbox, "Ambience", () => Settings.Instance.AmbienceVolume, v => Settings.Instance.AmbienceVolume = v);

        vbox.AddChild(new HSeparator());

        AddSliderRow(vbox, "Text speed", 0, 160, 5,
            () => Settings.Instance.TextSpeedCps,
            v => Settings.Instance.TextSpeedCps = v,
            v => v <= 0 ? "instant" : $"{(int)v} chars/s");
        AddToggle(vbox, "Auto-advance dialogue",
            () => Settings.Instance.AutoAdvanceDialogue,
            v => Settings.Instance.AutoAdvanceDialogue = v);

        vbox.AddChild(new HSeparator());

        AddToggle(vbox, "Fullscreen",
            () => Settings.Instance.Fullscreen,
            v => Settings.Instance.Fullscreen = v);
        AddToggle(vbox, "Reduce motion / flashing",
            () => Settings.Instance.ReduceMotion,
            v => Settings.Instance.ReduceMotion = v);
        AddSliderRow(vbox, "Corruption effects", 0, 1, 0.05,
            () => Settings.Instance.CorruptionIntensity,
            v => Settings.Instance.CorruptionIntensity = v,
            v => $"{(int)(v * 100)}%");
        AddToggle(vbox, "Hotspot reveal (hold Tab)",
            () => Settings.Instance.HotspotHints,
            v => Settings.Instance.HotspotHints = v);

        vbox.AddChild(new HSeparator());
        var close = MakeButton("Done");
        close.Pressed += () => CloseSettings(backToPause: _openedSettingsFromPause);
        vbox.AddChild(close);

        AddChild(_settingsPanel);
    }

    private void AddVolumeSlider(Container parent, string label, Func<float> get, Action<float> set)
    {
        AddSliderRow(parent, label, 0, 1, 0.01, get, set, v => $"{(int)(v * 100)}%");
    }

    private void AddSliderRow(Container parent, string label, double min, double max, double step,
        Func<float> get, Action<float> set, Func<double, string> format)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var l = MakeLabel(label, 21);
        l.CustomMinimumSize = new Vector2(190, 0);
        row.AddChild(l);

        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        slider.CustomMinimumSize = new Vector2(200, 0);
        var valueLabel = MakeLabel("", 19);
        valueLabel.CustomMinimumSize = new Vector2(90, 0);

        slider.ValueChanged += v =>
        {
            if (Settings.Instance == null)
                return;
            set((float)v);
            slider.SetMeta("refresh", v);
            valueLabel.Text = format(v);
            Settings.Instance.NotifyChanged();
        };

        if (Settings.Instance != null)
        {
            slider.Value = get();
            slider.SetMeta("refresh", (double)get());
            valueLabel.Text = format(slider.Value);
        }

        row.AddChild(slider);
        row.AddChild(valueLabel);
        _settingsRefreshers.Add((slider, null));
    }

    private void AddToggle(Container parent, string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Text = label };
        if (_font != null)
            check.AddThemeFontOverride("font", _font);
        check.AddThemeFontSizeOverride("font_size", 21);
        check.AddThemeColorOverride("font_color", new Color(0.93f, 0.89f, 0.82f));

        if (Settings.Instance != null)
            check.ButtonPressed = get();

        check.Toggled += v =>
        {
            if (Settings.Instance == null)
                return;
            set(v);
            Settings.Instance.NotifyChanged();
            AudioManager.Play(Sfx.UiClick, -8f);
        };
        parent.AddChild(check);
    }

    private void BuildJournalPanel()
    {
        _journalPanel = new Control { Visible = false };
        _journalPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _journalPanel.AddChild(DimBackdrop());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _journalPanel.AddChild(center);

        var panel = MakePanel();
        panel.CustomMinimumSize = new Vector2(880, 520);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        var title = MakeLabel("Memory Journal", 34);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        _journalStats = MakeLabel("", 18, new Color(0.8f, 0.75f, 0.68f, 0.85f));
        _journalStats.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_journalStats);
        vbox.AddChild(new HSeparator());

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        var fragTab = MakeButton("Fragments", 22);
        fragTab.Pressed += () => { _journalShowingEchoes = false; RefreshJournal(); };
        var echoTab = MakeButton("Echoes (recent words)", 22);
        echoTab.Pressed += () => { _journalShowingEchoes = true; RefreshJournal(); };
        tabs.AddChild(fragTab);
        tabs.AddChild(echoTab);
        vbox.AddChild(tabs);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 16);
        split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vbox.AddChild(split);

        _journalList = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) };
        _journalList.AddThemeConstantOverride("separation", 6);
        split.AddChild(_journalList);

        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(scroll);

        _journalBody = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (_font != null)
        {
            _journalBody.AddThemeFontOverride("normal_font", _font);
            _journalBody.AddThemeFontOverride("bold_font", _font);
            _journalBody.AddThemeFontOverride("italics_font", _font);
        }
        _journalBody.AddThemeFontSizeOverride("normal_font_size", 21);
        _journalBody.AddThemeColorOverride("default_color", new Color(0.93f, 0.89f, 0.82f));
        scroll.AddChild(_journalBody);

        vbox.AddChild(new HSeparator());
        var close = MakeButton("Close the journal");
        close.Pressed += () =>
        {
            bool back = _openedJournalFromPause;
            CloseJournal(backToPause: back);
            if (back)
            {
                _pausePanel.Visible = true;
                _openedJournalFromPause = false;
            }
        };
        vbox.AddChild(close);

        AddChild(_journalPanel);
    }

    private void BuildMenuButton()
    {
        var b = MakeButton("Settings", 22);
        b.AnchorLeft = 1;
        b.AnchorTop = 1;
        b.AnchorRight = 1;
        b.AnchorBottom = 1;
        b.OffsetLeft = -170;
        b.OffsetTop = -62;
        b.OffsetRight = -26;
        b.OffsetBottom = -22;
        b.Visible = false;
        b.Pressed += OpenSettings;
        _menuSettingsButton = b;
        AddChild(b);
    }
}
