using Godot;

public partial class MainMenu : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	public string StartScenePath { get; set; } = "res://scenes/managers/game_manager.tscn";

	[Export]
	public NodePath StartButtonPath { get; set; } = "Center/MarginContainer/VBox/StartButton";

	[Export]
	public NodePath FullStoryButtonPath { get; set; } = "Center/MarginContainer/VBox/FullStoryButton";

	[Export]
	public NodePath AboutButtonPath { get; set; } = "Center/MarginContainer/VBox/AboutButton";

	[Export]
	public NodePath QuitButtonPath { get; set; } = "Center/MarginContainer/VBox/QuitButton";

	[Export]
	public NodePath FullStoryModalPath { get; set; } = "SubViewportContainer/SubViewport/FullStoryModal";

	[Export]
	public NodePath AboutModalPath { get; set; } = "SubViewportContainer/SubViewport/AboutModal";

	[ExportGroup("Full Story")]
	[Export]
	public string FullStoryTitle { get; set; } = "Full Story";

	[Export(PropertyHint.MultilineText)]
	public string FullStoryBody { get; set; } = "";

	[Export]
	public Texture2D FullStoryArt { get; set; } = null;

	[ExportGroup("About")]
	[Export]
	public string AboutTitle { get; set; } = "About";

	[Export(PropertyHint.MultilineText)]
	public string AboutBody { get; set; } = "";

	[Export]
	public Texture2D AboutArt { get; set; } = null;

	private BaseButton _startButton;
	private BaseButton _continueButton;
	private BaseButton _fullStoryButton;
	private BaseButton _aboutButton;
	private BaseButton _quitButton;
	private InfoModal _fullStoryModal;
	private InfoModal _aboutModal;

	public override void _Ready()
	{
		_startButton = ResolveButton(StartButtonPath, "StartButton",
			"Center/MarginContainer/VBox/StartButton",
			"Center/VBox/StartButton");
		_fullStoryButton = ResolveButton(FullStoryButtonPath, "FullStoryButton",
			"Center/MarginContainer/VBox/FullStoryButton",
			"Center/VBox/FullStoryButton");
		_aboutButton = ResolveButton(AboutButtonPath, "AboutButton",
			"Center/MarginContainer/VBox/AboutButton",
			"Center/VBox/AboutButton");
		_quitButton = ResolveButton(QuitButtonPath, "QuitButton",
			"Center/MarginContainer/VBox/QuitButton",
			"Center/VBox/QuitButton");

		_fullStoryModal = GetNodeOrNull<InfoModal>(FullStoryModalPath);
		if (_fullStoryModal == null)
			Logger.Error("MainMenu could not find FullStoryModal at: ", FullStoryModalPath);

		_aboutModal = GetNodeOrNull<InfoModal>(AboutModalPath);
		if (_aboutModal == null)
			Logger.Error("MainMenu could not find AboutModal at: ", AboutModalPath);

		if (_startButton == null)
			Logger.Error("MainMenu could not find StartButton at Center/VBox/StartButton.");
		else
			_startButton.Pressed += OnStartPressed;

		if (_fullStoryButton == null)
			Logger.Error("MainMenu could not find FullStoryButton at Center/VBox/FullStoryButton.");
		else
			_fullStoryButton.Pressed += OnFullStoryPressed;

		if (_aboutButton == null)
			Logger.Error("MainMenu could not find AboutButton at Center/VBox/AboutButton.");
		else
			_aboutButton.Pressed += OnAboutPressed;

		if (_quitButton == null)
			Logger.Error("MainMenu could not find QuitButton at Center/VBox/QuitButton.");
		else
			_quitButton.Pressed += OnQuitPressed;

		foreach (var b in new[] { _startButton, _fullStoryButton, _aboutButton, _quitButton })
			HookButtonSounds(b);

		BuildContinueButton();

		(_continueButton ?? _startButton)?.GrabFocus();

		// Music is owned by AudioManager now (buses, volume settings, looping,
		// crossfades) instead of a throwaway per-scene player.
		AudioManager.Instance?.StopAmbience();
		AudioManager.Instance?.PlayMusic(AudioManager.MusicMenu, -7f, 1.4f);
	}

	private static void HookButtonSounds(BaseButton button)
	{
		if (button == null)
			return;
		button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -13f);
		button.Pressed += () => AudioManager.Play(Sfx.UiClick, -6f);
	}

	/// <summary>
	/// A "Continue" button exists only when a memory has been preserved.
	/// Added at runtime above Start so the original scene layout is untouched.
	/// </summary>
	private void BuildContinueButton()
	{
		if (_startButton == null || GameState.Instance == null || !GameState.Instance.HasSave)
			return;

		var vbox = _startButton.GetParent();
		if (vbox == null)
			return;

		var button = new Button { Text = "CONTINUE", Name = "ContinueButton" };
		var font = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		if (font != null)
			button.AddThemeFontOverride("font", font);
		button.AddThemeFontSizeOverride("font_size", 34);
		button.AddThemeColorOverride("font_color", new Color(0.35f, 0.28f, 0.2f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.12f, 0.09f, 0.06f));
		var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
		button.AddThemeStyleboxOverride("normal", flat);
		button.AddThemeStyleboxOverride("hover", flat);
		button.AddThemeStyleboxOverride("pressed", flat);
		button.AddThemeStyleboxOverride("focus", flat);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		button.Pressed += OnContinuePressed;
		HookButtonSounds(button);
		vbox.AddChild(button);
		vbox.MoveChild(button, _startButton.GetIndex());
		_continueButton = button;

		// "Start" now explicitly means beginning again.
		_startButton.TooltipText = "Begin again (lets go of the preserved memory)";
	}

	private void OnFullStoryPressed()
	{
		if (_fullStoryModal == null)
			return;

		_fullStoryModal.Open(FullStoryTitle, FullStoryBody, FullStoryArt);
	}

	private void OnAboutPressed()
	{
		if (_aboutModal == null)
			return;

		_aboutModal.Open(AboutTitle, AboutBody, AboutArt);
	}

	private BaseButton ResolveButton(NodePath configuredPath, string logicalName, params string[] fallbackPaths)
	{
		BaseButton button = null;

		if (configuredPath != null && !configuredPath.IsEmpty)
			button = GetNodeOrNull<BaseButton>(configuredPath);

		if (button != null)
			return button;

		foreach (var fallback in fallbackPaths)
		{
			button = GetNodeOrNull<BaseButton>(fallback);
			if (button != null)
				return button;
		}

		Logger.Error("MainMenu could not find ", logicalName, ". Tried: ", configuredPath, " and fallbacks.");
		return null;
	}

	private void OnStartPressed()
	{
		if (string.IsNullOrWhiteSpace(StartScenePath))
		{
			Logger.Error("MainMenu StartScenePath is empty.");
			return;
		}

		// Beginning again over a preserved memory is a choice, not an accident.
		if (GameState.Instance != null && GameState.Instance.HasSave && GameUI.Instance != null)
		{
			GameUI.Instance.ShowConfirm(
				"Begin again? The memory you preserved will be let go.",
				StartNewGame);
			return;
		}

		StartNewGame();
	}

	private void StartNewGame()
	{
		Logger.Info("Starting new game; changing scene to: ", StartScenePath);
		GameState.Instance?.NewGame();
		AudioManager.Instance?.StopMusic(0.5f);
		GetTree().ChangeSceneToFile(StartScenePath);
	}

	private void OnContinuePressed()
	{
		if (GameState.Instance == null || !GameState.Instance.PrepareContinue())
		{
			Logger.Warning("Continue requested but the save could not be loaded; starting new game.");
			StartNewGame();
			return;
		}

		Logger.Info("Continuing preserved memory; changing scene to: ", StartScenePath);
		AudioManager.Instance?.StopMusic(0.5f);
		GetTree().ChangeSceneToFile(StartScenePath);
	}

	private void OnQuitPressed()
	{
		Logger.Info("Quitting game from main menu.");
		Settings.Instance?.Save();
		GetTree().Quit();
	}
}
