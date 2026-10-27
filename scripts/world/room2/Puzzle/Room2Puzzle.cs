using System;
using Godot;

/// <summary>
/// Room 2's four-letter lock. Wrong full attempts now rattle, clear and
/// refocus instead of failing in total silence; typing and solving have
/// matching audio.
/// </summary>
public partial class Room2Puzzle : Area2D
{
	private const string TargetWord = "HOME";
	private const string NextScenePath = "res://scenes/world/room_3.tscn";

	private Node2D _padlock;
	private LineEdit[] _letterBoxes;
	private bool _isSolved;
	private bool _feedbackPlaying;
	private bool _drawerOpen;
	private Vector2 _padlockHome;

	// Typed letters survive stepping back to the room mid-word (statics live
	// for the whole run; GameState.NewGame clears them).
	private static readonly char[] _savedLetters = new char[4];

	/// <summary>True once the player has pressed Unlock (not merely spelled the
	/// word). Drives pass-through state on later visits.</summary>
	public static bool IsUnlocked { get; private set; }

	private Button _unlockButton;
	private bool _readyToUnlock;
	private int _wrongAttempts;

	/// <summary>Clears remembered letters (new game / fresh load / wrong word).</summary>
	public static void ResetSavedState()
	{
		ClearSavedLetters();
		IsUnlocked = false;
	}

	private static void ClearSavedLetters()
	{
		System.Array.Clear(_savedLetters, 0, _savedLetters.Length);
	}

	public override void _Ready()
	{
		_padlock = GetNodeOrNull<Node2D>("Padlock");
		GetNodeOrNull<Sprite2D>("Wall")?.SetDeferred("visible", false);
		GetNodeOrNull<AnimatedSprite2D>("Drawer")?.SetDeferred("visible", false);
		QueueRedraw();
		if (_padlock != null)
		{
			_padlock.Visible = true;
			_padlockHome = _padlock.Position;
		}

		FontFile inputFont = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		var inputStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.9f, 0.82f, 0.65f, 0.98f),
			BorderColor = new Color(0.42f, 0.28f, 0.14f, 0.96f),
		};
		inputStyle.SetBorderWidthAll(2);
		inputStyle.SetCornerRadiusAll(3);
		inputStyle.SetContentMarginAll(5);
		var focusStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.96f, 0.88f, 0.7f, 1f),
			BorderColor = new Color(0.79f, 0.57f, 0.27f, 1f),
		};
		focusStyle.SetBorderWidthAll(3);
		focusStyle.SetCornerRadiusAll(3);
		focusStyle.SetContentMarginAll(4);
		var title = GetNodeOrNull<Label>("Padlock/Title");
		if (title != null && inputFont != null)
		{
			title.AddThemeFontOverride("font", inputFont);
			title.AddThemeFontSizeOverride("font_size", 24);
			title.AddThemeColorOverride("font_color", new Color(0.96f, 0.87f, 0.69f));
		}

		_letterBoxes = new LineEdit[4];
		for (int i = 0; i < _letterBoxes.Length; i++)
		{
			var box = GetNodeOrNull<LineEdit>($"Padlock/letter-{i}");
			if (box == null)
			{
				Logger.Error($"Room2Puzzle missing node: Padlock/letter-{i}");
				continue;
			}

			box.MaxLength = 1;
			box.Alignment = HorizontalAlignment.Center;
			if (inputFont != null)
				box.AddThemeFontOverride("font", inputFont);
			box.AddThemeFontSizeOverride("font_size", 42);
			box.AddThemeColorOverride("font_color", new Color(0.16f, 0.1f, 0.055f));
			box.AddThemeStyleboxOverride("normal", inputStyle);
			box.AddThemeStyleboxOverride("focus", focusStyle);

			// Restore before subscribing so TextChanged doesn't fire for it.
			if (_savedLetters[i] != '\0')
				box.Text = _savedLetters[i].ToString();

			int index = i;
			box.TextChanged += (string newText) => OnLetterBoxTextChanged(index, newText);
			box.GuiInput += (InputEvent @event) => OnLetterBoxGuiInput(index, @event);

			_letterBoxes[i] = box;
		}

		// Resume typing where it left off: first empty box.
		var focusBox = _letterBoxes[0];
		foreach (var box in _letterBoxes)
		{
			if (box != null && string.IsNullOrEmpty(box.Text))
			{
				focusBox = box;
				break;
			}
		}
		focusBox?.GrabFocus();

		// The right word no longer springs the drawer by itself: the player
		// unlocks it. The Unlock button is always there and always pressable;
		// judgement happens on the press, not while typing.
		var firstBox = GetBox(0);
		var lastBox = GetBox(3);
		Vector2 unlockPos = new Vector2(-85, 96);
		if (firstBox != null && lastBox != null)
		{
			unlockPos = new Vector2(
				(firstBox.Position.X + lastBox.Position.X + lastBox.Size.X) / 2f - 85f,
				lastBox.Position.Y + lastBox.Size.Y + 16f);
		}
		if (_padlock != null)
		{
			_unlockButton = BuildUnlockButton(_padlock, unlockPos);
			_unlockButton.Pressed += OnUnlockPressed;
		}

		if (IsUnlocked)
		{
			// Unlocked on an earlier visit — drawer open. Whether
			// the photograph is still inside is Photo1Reward's call:
			// if it is in Mara's bag, the drawer must stay empty.
			_isSolved = true;
			SolveAsync();
		}
		else if (new string(_savedLetters) == TargetWord)
		{
			// Word already spelled from before stepping away — offer Unlock.
			SetReadyToUnlock(true);
		}
	}

	private void OnLetterBoxGuiInput(int index, InputEvent @event)
	{
		if (_isSolved)
			return;

		if (GameState.Instance?.IsRoomSealed("Room2") == true)
		{
			GameState.Instance.NotifySealedInteraction();
			return;
		}

		if (@event is not InputEventKey keyEvent)
			return;

		if (!keyEvent.Pressed)
			return;

		if (keyEvent.Keycode != Key.Backspace)
			return;

		var current = GetBox(index);
		if (current == null)
			return;

		if (!string.IsNullOrEmpty(current.Text))
			return;

		if (index <= 0)
			return;

		var prev = GetBox(index - 1);
		if (prev == null)
			return;

		prev.Text = "";
		_savedLetters[index - 1] = '\0';
		prev.GrabFocus();
	}

	private void OnLetterBoxTextChanged(int index, string newText)
	{
		if (_isSolved || _feedbackPlaying)
			return;

		var box = GetBox(index);
		if (box == null)
			return;

		string filtered = FilterToSingleCapitalLetter(newText);
		if (box.Text != filtered)
			box.Text = filtered;

		_savedLetters[index] = filtered.Length == 1 ? filtered[0] : '\0';

		if (filtered.Length == 1)
		{
			AudioManager.PlayVaried(Sfx.KeyType, -5f, 1f, 0.08f);
			if (index < _letterBoxes.Length - 1)
				GetBox(index + 1)?.GrabFocus();
		}

		CheckSolution();
	}

	private void CheckSolution()
	{
		if (_isSolved)
			return;

		if (_letterBoxes == null || _letterBoxes.Length != 4)
			return;

		Span<char> chars = stackalloc char[4];
		for (int i = 0; i < 4; i++)
		{
			var box = _letterBoxes[i];
			if (box == null)
				return;

			string text = box.Text?.Trim() ?? string.Empty;
			if (text.Length != 1)
			{
				SetReadyToUnlock(false);
				return;
			}

			chars[i] = char.ToUpperInvariant(text[0]);
		}

		string attempt = new string(chars);
		// Typing is never punished: the word is judged when Unlock is pressed.
		SetReadyToUnlock(string.Equals(attempt, TargetWord, StringComparison.Ordinal));
	}

	private void SetReadyToUnlock(bool ready)
	{
		bool wasReady = _readyToUnlock;
		_readyToUnlock = ready;
		if (_unlockButton != null)
		{
			// Always visible, always pressable. Bright when the word feels
			// right, dimmer when it doesn't — never gone.
			_unlockButton.Visible = true;
			_unlockButton.Modulate = ready ? Colors.White : new Color(0.78f, 0.74f, 0.68f, 0.92f);
		}

		if (ready && !wasReady)
		{
			AudioManager.Play(Sfx.WordChime, -9f);
			GameState.Instance?.Toast("That feels right. Now unlock it.");
		}
	}

	private void OnUnlockPressed()
	{
		if (GameState.Instance?.IsRoomSealed("Room2") == true)
		{
			GameState.Instance.NotifySealedInteraction();
			return;
		}

		if (_isSolved || _feedbackPlaying)
			return;

		if (!_readyToUnlock)
		{
			// Honest feedback at the press: incomplete, or simply not right.
			int filled = 0;
			foreach (var c in _savedLetters)
			{
				if (c != '\0')
					filled++;
			}
			if (filled < 4)
			{
				AudioManager.Play(Sfx.LockRattle, -7f);
				GameState.Instance?.Toast("It wants a whole word — four letters. What this room has told you is kept in your Journal (J) → Notes & Clues.");
			}
			else
			{
				WrongAttemptAsync();
			}
			return;
		}

		_isSolved = true;
		IsUnlocked = true;
		_readyToUnlock = false;
		if (_unlockButton != null)
			_unlockButton.Visible = false;

		GameState.Instance?.NotifyPuzzleSolved();
		SolveAsync();
	}

	private static Button BuildUnlockButton(Node parent, Vector2 position)
	{
		var button = new Button
		{
			Text = "Unlock",
			Visible = true,
			CustomMinimumSize = new Vector2(170, 46),
			Position = position,
		};
		var font = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		if (font != null)
			button.AddThemeFontOverride("font", font);
		button.AddThemeFontSizeOverride("font_size", 26);
		button.AddThemeColorOverride("font_color", new Color(0.96f, 0.9f, 0.78f));
		button.AddThemeColorOverride("font_hover_color", new Color(1f, 0.97f, 0.88f));

		var normal = new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.11f, 0.07f, 0.94f),
			BorderColor = new Color(0.8f, 0.66f, 0.42f, 0.9f),
		};
		normal.SetBorderWidthAll(2);
		normal.SetCornerRadiusAll(6);
		normal.SetContentMarginAll(8);
		var hover = new StyleBoxFlat
		{
			BgColor = new Color(0.32f, 0.23f, 0.13f, 0.96f),
			BorderColor = new Color(0.95f, 0.8f, 0.55f, 1f),
		};
		hover.SetBorderWidthAll(2);
		hover.SetCornerRadiusAll(6);
		hover.SetContentMarginAll(8);
		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", hover);
		button.AddThemeStyleboxOverride("pressed", hover);
		button.AddThemeStyleboxOverride("focus", hover);

		button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -10f);
		parent.AddChild(button);
		return button;
	}

	private async void WrongAttemptAsync()
	{
		if (_feedbackPlaying)
			return;
		_feedbackPlaying = true;

		// Repeated wrong words mean a stuck player: point at the journal's
		// clue cards first, then at the H hint.
		_wrongAttempts++;
		if (_wrongAttempts == 1)
			GameState.Instance?.Toast("The lock doesn't turn — that word isn't right. Something in this room has been saying the true one; your Journal (J) → Notes & Clues keeps it.");
		else if (_wrongAttempts == 2)
			GameState.Instance?.Toast("Still not right. Read her parents' letter again — J, then Notes & Clues.");
		else if (_wrongAttempts == 4)
			GameState.Instance?.Toast("Press H and the house will nudge you closer.");

		AudioManager.Play(Sfx.LockRattle, -5f);
		AudioManager.Play(Sfx.Denied, -11f);

		if (_padlock != null && !Settings.ReduceMotionOrDefault())
		{
			var tween = CreateTween();
			foreach (float offset in new[] { -7f, 6f, -4f, 3f, 0f })
				tween.TweenProperty(_padlock, "position", _padlockHome + new Vector2(offset, 0), 0.05f);
			await ToSignal(tween, Tween.SignalName.Finished);
		}
		else
		{
			await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
		}

		if (!GodotObject.IsInstanceValid(this) || _isSolved)
		{
			_feedbackPlaying = false;
			return;
		}

		// Clearing triggers TextChanged; the _feedbackPlaying guard turns
		// those callbacks into no-ops.
		foreach (var box in _letterBoxes)
		{
			if (box != null)
				box.Text = string.Empty;
		}
		ClearSavedLetters();
		GetBox(0)?.GrabFocus();
		_feedbackPlaying = false;
	}

	private async void SolveAsync()
	{
		AudioManager.Play(Sfx.LockOpen, -4f);
		await PlayDrawerAnimation();
		bool photoWaiting = RevealPhotoReward();
		_padlock?.SetDeferred("visible", false);
		AudioManager.Play(Sfx.PuzzleSolve, -6f);
		GameState.Instance?.Toast(photoWaiting
			? "Inside: a photograph, face-down all these years."
			: "The drawer is empty where the photograph lay — it's in Mara's bag now.");
	}

	private bool RevealPhotoReward()
	{
		// Never set Photo1.Visible from here. That was the bug: this
		// solve replay runs on every return to an unlocked drawer,
		// and the direct write undid Photo1Reward's taken-state.
		// The photograph owns its reveal and refuses it once bagged.
		var photoReward = GetNodeOrNull<Photo1Reward>("Photo1");
		if (photoReward != null)
			return photoReward.RevealFromDrawer();

		// Scene-shape fallback, held to the same rule.
		var photoArea = GetNodeOrNull<Area2D>("Photo1");
		if (photoArea == null || Photo1Reward.PhotographInBag)
		{
			if (photoArea != null)
				photoArea.Visible = false;
			return false;
		}

		photoArea.Visible = true;
		return true;
	}

	private async System.Threading.Tasks.Task PlayDrawerAnimation()
	{
		AudioManager.Play(Sfx.DrawerOpen, -5f);
		if (!Settings.ReduceMotionOrDefault())
			await ToSignal(GetTree().CreateTimer(0.35f), SceneTreeTimer.SignalName.Timeout);
		_drawerOpen = true;
		QueueRedraw();
	}

	public override void _Draw()
	{
		// The sitting room's desk drawer is painted for this close-up rather
		// than borrowing the kitchen cabinet's atlas art.
		DrawRect(new Rect2(0, 0, 1152, 648), new Color(0.035f, 0.027f, 0.024f), true);
		for (int y = 0; y < 648; y += 34)
		{
			DrawRect(new Rect2(0, y, 1152, 2), new Color(0.11f, 0.075f, 0.049f, 0.72f), true);
			DrawRect(new Rect2(0, y + 27, 1152, 1), new Color(0.18f, 0.12f, 0.073f, 0.42f), true);
		}

		var silhouette = new Rect2(178, 82, 796, 492);
		DrawRect(silhouette.Grow(14f), new Color(0.012f, 0.009f, 0.008f, 0.82f), true);
		DrawRect(new Rect2(166, 74, 820, 55), new Color(0.24f, 0.15f, 0.085f), true);
		DrawRect(new Rect2(154, 70, 844, 13), new Color(0.54f, 0.36f, 0.18f), true);
		DrawRect(new Rect2(166, 124, 820, 15), new Color(0.09f, 0.055f, 0.03f), true);

		if (!_drawerOpen)
		{
			DrawRect(silhouette, new Color(0.29f, 0.19f, 0.105f), true);
			DrawRect(silhouette, new Color(0.59f, 0.4f, 0.2f, 0.92f), false, 4f);
			var front = new Rect2(226, 145, 700, 234);
			DrawRect(front, new Color(0.34f, 0.22f, 0.12f), true);
			DrawRect(front.Grow(-13f), new Color(0.27f, 0.17f, 0.095f), true);
			DrawRect(front.Grow(-20f), new Color(0.36f, 0.235f, 0.12f), false, 2f);
			DrawRect(new Rect2(242, 157, 5, 210), new Color(0.69f, 0.46f, 0.23f, 0.62f), true);
			DrawRect(new Rect2(905, 157, 5, 210), new Color(0.12f, 0.07f, 0.035f, 0.88f), true);
			for (int i = 0; i < 4; i++)
				DrawRect(new Rect2(270 + i * 150, 166, 2, 196),
					new Color(0.48f, 0.31f, 0.16f, 0.55f), true);
			DrawRect(new Rect2(226, 387, 700, 16), new Color(0.12f, 0.075f, 0.04f), true);
			DrawRect(new Rect2(256, 412, 280, 128), new Color(0.22f, 0.14f, 0.075f), true);
			DrawRect(new Rect2(616, 412, 280, 128), new Color(0.22f, 0.14f, 0.075f), true);
			DrawRect(new Rect2(268, 422, 256, 108), new Color(0.29f, 0.18f, 0.095f), false, 3f);
			DrawRect(new Rect2(628, 422, 256, 108), new Color(0.29f, 0.18f, 0.095f), false, 3f);
			DrawRect(new Rect2(557, 330, 38, 7), new Color(0.7f, 0.51f, 0.26f), true);
			DrawCircle(new Vector2(576, 334), 4f, new Color(0.86f, 0.69f, 0.39f));
		}
		else
		{
			var cavity = new Rect2(244, 145, 664, 236);
			DrawRect(silhouette, new Color(0.24f, 0.15f, 0.08f), true);
			DrawRect(cavity, new Color(0.035f, 0.025f, 0.018f), true);
			DrawRect(cavity, new Color(0.61f, 0.41f, 0.2f), false, 5f);
			DrawRect(new Rect2(268, 176, 616, 11), new Color(0.42f, 0.28f, 0.14f), true);
			DrawRect(new Rect2(268, 340, 616, 16), new Color(0.39f, 0.25f, 0.12f), true);
			var loweredDrawer = new Rect2(254, 389, 644, 111);
			DrawRect(loweredDrawer, new Color(0.3f, 0.19f, 0.1f), true);
			DrawRect(loweredDrawer, new Color(0.62f, 0.42f, 0.21f), false, 4f);
			DrawRect(new Rect2(280, 405, 590, 78), new Color(0.25f, 0.16f, 0.085f), false, 2f);
		}

		DrawRect(new Rect2(178, 554, 796, 20), new Color(0.1f, 0.06f, 0.032f), true);
		DrawRect(new Rect2(178, 554, 796, 4), new Color(0.53f, 0.35f, 0.17f, 0.82f), true);
	}

	private LineEdit GetBox(int index)
	{
		if (_letterBoxes == null)
			return null;

		if (index < 0 || index >= _letterBoxes.Length)
			return null;

		return _letterBoxes[index];
	}

	private static string FilterToSingleCapitalLetter(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return string.Empty;

		char c = char.ToUpperInvariant(raw.Trim()[0]);
		return c is >= 'A' and <= 'Z' ? c.ToString() : string.Empty;
	}
}
