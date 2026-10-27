using System.Collections.Generic;
using Godot;

public partial class MainMenu : Control
{
	[Export(PropertyHint.File, "*.tscn")]
	public string StartScenePath { get; set; } = "res://scenes/managers/game_manager.tscn";

	[Export]
	public NodePath StartButtonPath { get; set; } = "Center/MarginContainer/VBox/StartButton";

	[Export]
	public NodePath ContinueButtonPath { get; set; } = "SubViewportContainer/SubViewport/Center/MarginContainer/VBox/ContinueButton";

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
	private TextureRect _menuRainLeft;
	private TextureRect _menuRainRight;
	private TextureRect _menuSteam;
	private readonly Texture2D[] _menuRainTextures = new Texture2D[3];
	private readonly Texture2D[] _menuSteamTextures = new Texture2D[3];
	private double _menuFxTime;
	private static readonly int[] MenuFxPingPong = { 0, 1, 2, 1 };

	public override void _Ready()
	{
		_startButton = ResolveButton(StartButtonPath, "StartButton",
			"Center/MarginContainer/VBox/StartButton",
			"Center/VBox/StartButton");
		_continueButton = ResolveButton(ContinueButtonPath, "ContinueButton",
			"SubViewportContainer/SubViewport/Center/MarginContainer/VBox/ContinueButton");
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

		if (_continueButton == null)
			Logger.Error("MainMenu could not find ContinueButton.");
		else
		{
			_continueButton.Pressed += OnContinuePressed;
			// Native scene TextureButton, hidden until a memory is preserved.
			_continueButton.Visible = GameState.Instance?.HasSave ?? false;
		}

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

		foreach (var b in new[] { _startButton, _continueButton, _fullStoryButton, _aboutButton, _quitButton })
		{
			TightenMenuButton(b);
			HookButtonSounds(b);
		}
		BuildMenuAtmosphere();

		if (_continueButton?.Visible == true && _startButton != null)
			_startButton.TooltipText = "Begin again (lets go of the preserved memory)";

		((_continueButton?.Visible == true ? _continueButton : _startButton))?.GrabFocus();

		// Music is owned by AudioManager now (buses, volume settings, looping,
		// crossfades) instead of a throwaway per-scene player.
		AudioManager.Instance?.StopAmbience();
		AudioManager.Instance?.PlayMusic(AudioManager.MusicMenu, -7f, 1.4f);
	}

	public override void _Process(double delta)
	{
		ApplyMenuUiScale();
		if (_menuRainLeft == null || _menuRainRight == null || _menuSteam == null)
			return;

		if (!Settings.ReduceMotionOrDefault())
			_menuFxTime += delta;

		int rainFrame = MenuFxPingPong[(int)(_menuFxTime * 7.0) % MenuFxPingPong.Length];
		Texture2D rainTexture = _menuRainTextures[rainFrame];
		if (rainTexture != null)
		{
			_menuRainLeft.Texture = rainTexture;
			_menuRainRight.Texture = rainTexture;
		}

		int steamFrame = MenuFxPingPong[(int)((_menuFxTime + 0.37) * 4.0) % MenuFxPingPong.Length];
		Texture2D steamTexture = _menuSteamTextures[steamFrame];
		if (steamTexture != null)
			_menuSteam.Texture = steamTexture;

		float steamAlpha = 0.52f + 0.08f * Mathf.Sin((float)_menuFxTime * 1.3f);
		_menuSteam.Modulate = new Color(1f, 1f, 1f, steamAlpha);
	}

	private void ApplyMenuUiScale()
	{
		ApplyCenteredScale(GetNodeOrNull<Control>("SubViewportContainer/SubViewport/Center"));
		ApplyCenteredScale(_fullStoryModal?.GetNodeOrNull<Control>("Panel"));
		ApplyCenteredScale(_aboutModal?.GetNodeOrNull<Control>("Panel"));
	}

	private static void ApplyCenteredScale(Control control)
	{
		if (control == null || control.Size.X <= 0f || control.Size.Y <= 0f)
			return;
		control.PivotOffset = control.Size * 0.5f;
		control.Scale = new Vector2(0.8f, 0.8f);
	}

	private void BuildMenuAtmosphere()
	{
		for (int i = 0; i < 3; i++)
		{
			_menuRainTextures[i] = GD.Load<Texture2D>($"res://assets/images/bgs/amb_rain_{i}.png");
			_menuSteamTextures[i] = GD.Load<Texture2D>($"res://assets/images/bgs/amb_steam_{i}.png");
		}

		var background = GetNodeOrNull<TextureRect>("SubViewportContainer/SubViewport/Background");
		Node parent = background?.GetParent();
		if (background == null || parent == null)
		{
			Logger.Warning("[MainMenu] Background not found; menu rain/steam skipped.");
			return;
		}

		int insertIndex = background.GetIndex() + 1;
		// Window glass measured from the 1677x938 source and mapped into the
		// 1152x648 menu viewport. Rain stops above the desk/books.
		_menuRainLeft = MakeMenuFxRect(parent, insertIndex++, "MenuRainLeft", _menuRainTextures[0],
			new Rect2(323f, 73f, 244f, 337f), new Color(1f, 1f, 1f, 0.68f));
		_menuRainRight = MakeMenuFxRect(parent, insertIndex++, "MenuRainRight", _menuRainTextures[0],
			new Rect2(587f, 73f, 330f, 337f), new Color(1f, 1f, 1f, 0.68f));
		// Coffee surface maps to about (979,448) in the menu viewport.
		// Keep the plume's base at that surface (268 + 180 = 448), but let
		// it reach noticeably higher than the first, shorter placement.
		_menuSteam = MakeMenuFxRect(parent, insertIndex, "MenuSteam", _menuSteamTextures[0],
			new Rect2(955f, 268f, 63f, 180f), new Color(1f, 1f, 1f, 0.52f));
	}

	private static TextureRect MakeMenuFxRect(Node parent, int insertIndex, string name,
		Texture2D texture, Rect2 rect, Color modulate)
	{
		var fx = new TextureRect
		{
			Name = name,
			Texture = texture,
			Position = rect.Position,
			Size = rect.Size,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = modulate,
		};
		parent.AddChild(fx);
		parent.MoveChild(fx, Mathf.Clamp(insertIndex, 0, parent.GetChildCount() - 1));
		return fx;
	}

	private static void TightenMenuButton(BaseButton button)
	{
		// The scene's buttons previously used horizontal Expand/Fill inside
		// the menu column, so hover fired far beyond the painted button.
		// Keep their original left-side placement, but use the *resting*
		// label as the hitbox: the hover art is wider because a key appears
		// beside the words, and that not-yet-visible key must not be
		// hoverable empty space. Keep (top-left) preserves the labels' left
		// edge and lets the wider hover/pressed art extend to the right.
		if (button is not TextureButton textureButton)
			return;

		textureButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		textureButton.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		// IgnoreTextureSize stops the wider hover texture from enlarging
		// the control's own minimum size; CustomMinimumSize below supplies
		// the resting label's exact bounds instead.
		textureButton.IgnoreTextureSize = true;
		textureButton.StretchMode = TextureButton.StretchModeEnum.Keep;

		Vector2 minSize = textureButton.TextureNormal?.GetSize() ?? Vector2.Zero;
		if (minSize != Vector2.Zero)
			textureButton.CustomMinimumSize = minSize;
	}

	private static void HookButtonSounds(BaseButton button)
	{
		if (button == null)
			return;
		button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -8f);
		button.Pressed += () => AudioManager.Play(Sfx.UiClick, -4f);
	}


	private void OnFullStoryPressed()
	{
		if (_fullStoryModal == null)
			return;

		// The journal's pages live in code so the story told here is the
		// story the finished game tells — room for room, scene for scene —
		// and every page carries the picture of the place it is about.
		_fullStoryModal.OpenBook(FullStoryTitle, BuildFullStoryPages());
	}

	private void OnAboutPressed()
	{
		if (_aboutModal == null)
			return;

		_aboutModal.OpenBook(AboutTitle, BuildAboutPages());
	}

	private static List<InfoModal.BookPage> BuildFullStoryPages()
	{
		return new List<InfoModal.BookPage>
		{
			new InfoModal.BookPage("Premise", "Mara Voss is thirty-four, and she has not left her apartment in four months — she does not know that. In her mind she still lives in the house in Eldfen where she grew up: seven rooms, cedarwood and old paper, every morning ordinary.\n\nYou are not Mara. You are the part of her memory that still knows something is wrong.", "res://assets/images/bgs/Main_Menu_BG.png"),
			new InfoModal.BookPage("That Day — The Morning", "Ida, eight years younger, left home the way she left every room: loud, hungry, already herself. Mara, twenty-two, promised to fetch her at four o'clock — said it like a fact, like the toast, like the clock.\n\nIda believed her the way she believed the weather.", "res://assets/images/bgs/Room_1_BG.png"),
			new InfoModal.BookPage("That Day — The Waiting", "She waited by the apple trees where she'd been told to wait, basket packed for two. She counted on the swing — one swing for every minute she meant not to mind.\n\nFour o'clock came. Then, slowly, it went. Mara was somewhere ordinary — a book, somebody's kitchen, a clock she didn't look at. She remembers being sure there was time. She remembers choosing.", "res://assets/images/bgs/Truth_Ida_Waits.png"),
			new InfoModal.BookPage("That Day — The Road", "Nobody came for her, so in the end she came for herself. Her own hand worried the gate latch loose. Her own feet took the road home.\n\nThere was no stranger on that road. No monster on the bridge. There never was.", "res://assets/images/bgs/Truth_Ida_Walks.png"),
			new InfoModal.BookPage("That Day — The Bridge", "The road home crosses the little bridge over the Harren canal. Its railing was older than both sisters, rotten through, and nobody's job to mend.\n\nIt gave all at once, the way bad wood gives. Ida went into the black water in her yellow coat, a child's walk from home. She was fourteen.", "res://assets/images/bgs/Truth_Bridge.png"),
			new InfoModal.BookPage("4:47", "Mara remembered at 4:47 and came through the gate calling her sister's name. The garden gave her back a swing, still moving, finishing its swing.\n\nThat night their mother sat on the landing until morning, under the light she always left for latecomers. Their parents never said it was Mara's fault. That was worse.", "res://assets/images/bgs/Porch_447_BG.png"),
			new InfoModal.BookPage("Afterwards", "Mara left Eldfen and built a careful, quiet life she showed almost no one. For twelve years she wrote an apology every October and never sent it.\n\nFour months ago she found a glass jar on the street, lid rusted shut, and something in her closed. She locked her door — and her mind built the house again, filled it with Ida, made it safe.", "res://assets/images/bgs/Apartment_Now_BG.png"),
			new InfoModal.BookPage("The Kitchen", "In memory: bright, warm, two cups out, toast going. What it holds: the morning of that day, kept overexposed so Mara does not have to look at where her mind already was — somewhere else.\n\nThe brass key begins here. So does the clock.", "res://assets/images/bgs/Room_1_BG.png"),
			new InfoModal.BookPage("The Photograph", "A chair Ida fell asleep in, happy just to fall asleep to someone else breathing, someone turning a page. The house keeps the photograph in this room, and one word on its lock: what home used to mean before it meant this.", "res://assets/images/bgs/Room2_no_objects.png"),
			new InfoModal.BookPage("The Preserved Names", "Ida kept spiders in jars and named them so she would not be afraid of them — her whole way of being brave. Mara kept the jars.\n\nIt is easier to preserve a thing than to admit the hands that labelled it are gone.", "res://assets/images/bgs/Room_3_BG.png"),
			new InfoModal.BookPage("The Hallway", "Family photographs, and a mother's sentence in the hall, years too late, to a daughter still measuring every distance from the morning she did not come.\n\nThe brass door here is stamped with a bridge — water moving underneath whether anyone crosses or not. The house hid its truest shape in plain sight.", "res://assets/images/bgs/BgRoom4.png"),
			new InfoModal.BookPage("The Unsent Apology", "Mara's bedroom in two years at once, its furniture sliding between the girl who slept here and the woman who cannot: a desk beginning the same letter for twelve years, twelve sealed envelopes with no address — and one lamp that never changes, a light left on for whoever comes home late.", "res://assets/images/bgs/Room_5_BG.png"),
			new InfoModal.BookPage("The Garden & the Porch", "Basket. Swing. Gate. Road. Traced in order, the garden stops being four separate truths and becomes the afternoon itself.\n\nBeyond the bend lies the porch at 4:47 — the light on, the steps worn by arriving too late, the garden path shorter than memory allows.", "res://assets/images/bgs/Room_6_Final.png"),
			new InfoModal.BookPage("The Last Room", "Six fragments, carried jumbled in a bag, must be set back in the order they were lived. The house answers only once — when all six stand together — and then Mara watches that day whole, and chooses: let the house go and wake; keep it and stay; or be held so roughly by memory that it shatters.\n\nNothing in those rooms was a monster. Only minutes, and who did not come in them.", "res://assets/images/bgs/room_7_bg.png"),
		};
	}

	/// <summary>The About text stays the exported document; it is simply
	/// bound into the same journal — short standing lines become page
	/// headings, paragraphs are gathered a page at a time, and each page
	/// is pictured with a scene its words are nearest to.</summary>
	private List<InfoModal.BookPage> BuildAboutPages()
	{
		string[] artCycle =
		{
			"res://assets/images/bgs/Main_Menu_BG.png",
			"res://assets/images/bgs/Room_1_BG.png",
			"res://assets/images/bgs/Room_5_BG.png",
			"res://assets/images/bgs/Room_6_Final.png",
			"res://assets/images/bgs/Apartment_Now_BG.png",
			"res://assets/images/bgs/room_7_bg.png",
		};

		var pages = new List<InfoModal.BookPage>();
		string heading = string.Empty;
		string body = string.Empty;

		void Flush()
		{
			if (body.Trim().Length == 0 && heading.Length == 0)
				return;
			string text = body.Trim();
			string lower = text.ToLowerInvariant();
			string art = artCycle[pages.Count % artCycle.Length];
			if (lower.Contains("corrupt"))
				art = "res://assets/images/bgs/Room_5_BG.png";
			else if (lower.Contains("verb") || lower.Contains("echo")
				|| lower.Contains("anchor") || lower.Contains("release"))
				art = "res://assets/images/bgs/room_7_bg.png";
			pages.Add(new InfoModal.BookPage(heading, text, art));
			heading = string.Empty;
			body = string.Empty;
		}

		foreach (var raw in (AboutBody ?? string.Empty)
			.Split(new[] { "\n\n" }, System.StringSplitOptions.None))
		{
			string para = raw.Trim();
			if (para.Length == 0)
				continue;
			bool isHeading = !para.Contains("\n") && para.Length <= 42
				&& !para.EndsWith(".") && !para.EndsWith(":");
			if (isHeading)
			{
				Flush();
				heading = para;
				continue;
			}
			if (body.Length > 0 && body.Length + para.Length > 780)
				Flush();
			body = body.Length == 0 ? para : body + "\n\n" + para;
		}
		Flush();

		if (pages.Count == 0)
			pages.Add(new InfoModal.BookPage(string.Empty, AboutBody ?? string.Empty, artCycle[0]));
		return pages;
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
				StartNewGame,
				"Yes, let it go",
				"Not yet");
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
