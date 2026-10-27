using Godot;
using System;

/// <summary>
/// Room 1's five-dial padlock. Solved exactly once (the drawer-open sequence
/// used to re-fire on every later dial change), with dial/lock/drawer audio.
/// The brass key inside is collected by BrassKey1.cs — this class previously
/// handled that click too, so the reward dialogue fired twice.
/// </summary>
public partial class Room1Puzzle : Area2D
{
	private const string PuzzleCursorPath = "res://assets/images/core/cursor_magnifying_glass.png";
	private const string PointerCursorPath = "res://assets/images/core/cursor_pointer.png";

	private int[] expectedCombination = { 7, 1, 4, 8, 3 };
	private string _currentHoverCursorPath = string.Empty;
	private bool _isSolved;

	// Dial positions survive stepping back to the room mid-puzzle (statics
	// live for the whole run; GameState.NewGame clears them).
	private static readonly int[] _savedDialValues = new int[5];

	/// <summary>True once the player has pressed Unlock (not merely dialled the
	/// combination). Drives the open-cabinet state back in the room's POV.</summary>
	public static bool IsUnlocked { get; private set; }

	private Button _unlockButton;
	private readonly Label[] _dialValueLabels = new Label[5];
	private readonly Control[] _dialControls = new Control[15];
	private Label _dialStatusLabel;
	private int _selectedDial;
	private bool _readyToUnlock;
	private int _dialTicks;
	private int _wrongUnlocks;

	/// <summary>Clears remembered dial positions (new game / fresh load).</summary>
	public static void ResetSavedState()
	{
		System.Array.Clear(_savedDialValues, 0, _savedDialValues.Length);
		IsUnlocked = false;
	}

	public override void _Ready()
	{
		InputPickable = true;
		FontFile inputFont = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		var padlock = GetNodeOrNull<Node2D>("Padlock");
		var title = GetNodeOrNull<Label>("Padlock/Title");
		if (padlock != null)
			BuildPadlockInterface(padlock, title, inputFont);

		_unlockButton = BuildUnlockButton(padlock ?? this, new Vector2(491, 554));
		_unlockButton.Pressed += OnUnlockPressed;

		if (IsUnlocked)
		{
			// Unlocked on an earlier visit — drawer open, key still waiting.
			_isSolved = true;
			PlayDrawerAnimation();
		}
		else
		{
			// The combination may already be dialled in from before stepping away.
			CheckPadlockSolution();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		// Esc steps away from the padlock (handled before GameUI's pause,
		// which listens on _UnhandledInput — a later stage).
		if (_isSolved)
			return;

		var padlock = GetNodeOrNull<Node2D>("Padlock");
		if (padlock == null || !padlock.Visible)
			return;

		if (@event.IsActionPressed(InputSetup.PauseGame))
		{
			padlock.Visible = false;
			AudioManager.Play(Sfx.UiBack, -8f);
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Input(InputEvent @event)
	{
		var padlock = GetNodeOrNull<Node2D>("Padlock");
		if (_isSolved || padlock == null || !padlock.Visible ||
			GetTree().Paused || Dialogue.IsInputBlocked || Dialogue.IsShowing ||
			(GameUI.Instance?.IsBlockingOverlayOpen() ?? false))
			return;

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			switch (key.PhysicalKeycode)
			{
				case Key.Left:
					SetSelectedDial((_selectedDial + 4) % 5);
					GetViewport().SetInputAsHandled();
					return;
				case Key.Right:
					SetSelectedDial((_selectedDial + 1) % 5);
					GetViewport().SetInputAsHandled();
					return;
				case Key.Up:
					AdjustDial(_selectedDial, 1);
					GetViewport().SetInputAsHandled();
					return;
				case Key.Down:
					AdjustDial(_selectedDial, -1);
					GetViewport().SetInputAsHandled();
					return;
			}
		}

		if (@event is InputEventMouseButton wheel && wheel.Pressed &&
			(wheel.ButtonIndex == MouseButton.WheelUp || wheel.ButtonIndex == MouseButton.WheelDown))
		{
			Vector2 mouse = GetGlobalMousePosition();
			for (int i = 0; i < _dialValueLabels.Length; i++)
			{
				var label = _dialValueLabels[i];
				if (label != null && label.IsVisibleInTree() && label.GetGlobalRect().HasPoint(mouse))
				{
					AdjustDial(i, wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}
	}

	public override void _Process(double delta)
	{
		string desiredCursor = GetHoverCursorPath();

		if (string.IsNullOrEmpty(desiredCursor))
		{
			if (!string.IsNullOrEmpty(_currentHoverCursorPath))
			{
				CursorHelper.EndHover(this);
				_currentHoverCursorPath = string.Empty;
			}

			return;
		}

		if (_currentHoverCursorPath != desiredCursor)
		{
			CursorHelper.BeginHover(this, desiredCursor, 32, 32);
			_currentHoverCursorPath = desiredCursor;
		}
	}

	public override void _ExitTree()
	{
		if (!string.IsNullOrEmpty(_currentHoverCursorPath))
		{
			CursorHelper.EndHover(this);
			_currentHoverCursorPath = string.Empty;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Dialogue.IsInputBlocked)
			return;

		// A finished kitchen goes quiet — unless the drawer still keeps
		// something practical Mara walked past. Then this close-up stays
		// honest work, and she can come back for it.
		if (GameState.Instance?.IsRoomSealed("Room1") == true &&
			GameState.Instance?.RoomHasUnclaimedPracticalItem("Room1") != true)
		{
			if (@event is InputEventMouseButton sealedClick && sealedClick.Pressed
				&& sealedClick.ButtonIndex == MouseButton.Left)
			{
				GameState.Instance.NotifySealedInteraction();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		// A click, or F: the house's hand, for the thing under the
		// cursor. Same reach, same rules, either way.
		bool clicked = @event is InputEventMouseButton mouseEvent && mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left;
		bool useKey = @event is InputEventKey keyEvent && keyEvent.Pressed &&
			!keyEvent.Echo && keyEvent.PhysicalKeycode == Key.F;
		if (!clicked && !useKey)
			return;

		if (IsClickInsidePuzzle(GetGlobalMousePosition()))
		{
			OnPuzzleClicked();
			GetViewport().SetInputAsHandled();
		}
	}

	private bool IsClickInsidePuzzle(Vector2 globalMousePosition)
	{
		var collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collisionShape?.Shape is not RectangleShape2D rect)
			return false;

		var localMousePosition = collisionShape.GetGlobalTransform().AffineInverse() * globalMousePosition;
		var halfSize = rect.Size / 2f;

		return localMousePosition.X >= -halfSize.X &&
			   localMousePosition.X <= halfSize.X &&
			   localMousePosition.Y >= -halfSize.Y &&
			   localMousePosition.Y <= halfSize.Y;
	}

	private void OnPuzzleClicked()
	{
		var padlock = GetNode<Node2D>("Padlock");
		if (!padlock.Visible)
			AudioManager.Play(Sfx.Examine, -7f);
		padlock.Visible = true;
	}

	private string GetHoverCursorPath()
	{
		var mousePosition = GetGlobalMousePosition();

		var brassKey = GetNodeOrNull<Area2D>("BrassKey1");
		if (brassKey != null && brassKey.Visible && IsInsideArea2D(brassKey, mousePosition))
			return PointerCursorPath;

		// The open drawer keeps two smaller takeables beside the brass
		// key; BrassKey1 knows their shapes.
		var drawerItems = GetNodeOrNull<BrassKey1>("BrassKey1");
		if (drawerItems != null && drawerItems.Visible && drawerItems.IsOverTakeableItem(mousePosition))
			return PointerCursorPath;

		var padlock = GetNodeOrNull<Node2D>("Padlock");
		if (padlock != null && padlock.Visible && IsMouseOverPadlock(mousePosition))
			return PointerCursorPath;

		if (IsClickInsidePuzzle(mousePosition))
			return PuzzleCursorPath;

		return string.Empty;
	}

	private bool IsInsideArea2D(Area2D area, Vector2 globalMousePosition)
	{
		var collisionShape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		// A taken thing's shape is disabled — the hover pointer must
		// not keep pointing at the empty place where it lay.
		if (collisionShape?.Disabled == true)
			return false;
		if (collisionShape?.Shape is not RectangleShape2D rect)
			return false;

		var localMousePosition = collisionShape.GetGlobalTransform().AffineInverse() * globalMousePosition;
		var halfSize = rect.Size / 2f;

		return localMousePosition.X >= -halfSize.X &&
			   localMousePosition.X <= halfSize.X &&
			   localMousePosition.Y >= -halfSize.Y &&
			   localMousePosition.Y <= halfSize.Y;
	}

	private bool IsMouseOverPadlock(Vector2 globalMousePosition)
	{
		foreach (var dialControl in _dialControls)
		{
			if (dialControl != null && dialControl.IsVisibleInTree() &&
				dialControl.GetGlobalRect().HasPoint(globalMousePosition))
				return true;
		}

		var title = GetNodeOrNull<Control>("Padlock/Title");
		return title != null && title.IsVisibleInTree() && title.GetGlobalRect().HasPoint(globalMousePosition);
	}

	private void BuildPadlockInterface(Node2D padlock, Label title, FontFile inputFont)
	{
		var plate = padlock.GetNodeOrNull<TextureRect>("LockPlate");
		if (plate == null)
		{
			plate = new TextureRect
			{
				Name = "LockPlate",
				Texture = GD.Load<Texture2D>("res://assets/images/ui/room1_five_dial_lock.png"),
			};
			padlock.AddChild(plate);
		}
		plate.Position = new Vector2(161, 78);
		plate.Size = new Vector2(830, 436);
		plate.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		plate.StretchMode = TextureRect.StretchModeEnum.Scale;
		plate.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		plate.MouseFilter = Control.MouseFilterEnum.Ignore;
		padlock.MoveChild(plate, 0);

		if (title != null)
		{
			title.Text = "Sometimes… you just need to pay attention";
			title.Position = new Vector2(290, 112);
			title.Size = new Vector2(580, 36);
			title.CustomMinimumSize = Vector2.Zero;
			title.HorizontalAlignment = HorizontalAlignment.Center;
			title.VerticalAlignment = VerticalAlignment.Center;
			title.MouseFilter = Control.MouseFilterEnum.Ignore;
			if (inputFont != null)
				title.AddThemeFontOverride("font", inputFont);
			title.AddThemeFontSizeOverride("font_size", 21);
			title.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.62f));
			title.AddThemeColorOverride("font_outline_color", new Color(0.16f, 0.075f, 0.028f, 0.95f));
			title.AddThemeConstantOverride("outline_size", 3);
		}

		float[] dialCenters = { 328f, 452f, 576f, 700f, 824f };
		for (int i = 0; i < 5; i++)
		{
			int dialIndex = i;
			float center = dialCenters[i];
			var up = BuildDialButton(padlock, "+", new Vector2(center - 17f, 207f),
				() => AdjustDial(dialIndex, 1), inputFont);
			up.Name = $"Dial{dialIndex + 1}Up";
			var value = new Label
			{
				Name = $"Dial{dialIndex + 1}Value",
				Text = _savedDialValues[i].ToString(),
				Position = new Vector2(center - 34f, 263f),
				Size = new Vector2(68f, 70f),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			if (inputFont != null)
				value.AddThemeFontOverride("font", inputFont);
			value.AddThemeFontSizeOverride("font_size", 51);
			value.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.72f));
			value.AddThemeColorOverride("font_outline_color", new Color(0.15f, 0.075f, 0.035f, 0.95f));
			value.AddThemeConstantOverride("outline_size", 4);
			value.AddThemeColorOverride("font_shadow_color", new Color(0.08f, 0.035f, 0.015f, 0.8f));
			value.AddThemeConstantOverride("shadow_offset_x", 1);
			value.AddThemeConstantOverride("shadow_offset_y", 2);
			padlock.AddChild(value);
			var down = BuildDialButton(padlock, "−", new Vector2(center - 17f, 354f),
				() => AdjustDial(dialIndex, -1), inputFont);
			down.Name = $"Dial{dialIndex + 1}Down";
			_dialValueLabels[i] = value;
			_dialControls[i * 3] = up;
			_dialControls[i * 3 + 1] = value;
			_dialControls[i * 3 + 2] = down;
		}

		_dialStatusLabel = new Label
		{
			Name = "DialInstructions",
			Text = string.Empty,
			Position = new Vector2(344, 518),
			Size = new Vector2(464, 28),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		if (inputFont != null)
			_dialStatusLabel.AddThemeFontOverride("font", inputFont);
		_dialStatusLabel.AddThemeFontSizeOverride("font_size", 16);
		_dialStatusLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.85f, 0.65f, 0.94f));
		padlock.AddChild(_dialStatusLabel);
		SetSelectedDial(0);
	}

	private Button BuildDialButton(Node parent, string text, Vector2 position, Action pressed, FontFile inputFont)
	{
		var button = new Button
		{
			Text = text,
			Position = position,
			Size = new Vector2(34, 32),
			CustomMinimumSize = new Vector2(34, 32),
			FocusMode = Control.FocusModeEnum.All,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		if (inputFont != null)
			button.AddThemeFontOverride("font", inputFont);
		button.AddThemeFontSizeOverride("font_size", 25);
		button.AddThemeColorOverride("font_color", new Color(0.95f, 0.78f, 0.48f));
		button.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.78f));
		var normal = new StyleBoxFlat
		{
			BgColor = new Color(0.18f, 0.095f, 0.035f, 0.82f),
			BorderColor = new Color(0.66f, 0.43f, 0.19f, 0.92f),
		};
		normal.SetBorderWidthAll(1);
		normal.SetCornerRadiusAll(7);
		normal.SetContentMarginAll(1);
		var hover = new StyleBoxFlat
		{
			BgColor = new Color(0.4f, 0.23f, 0.08f, 0.96f),
			BorderColor = new Color(1f, 0.78f, 0.4f, 1f),
		};
		hover.SetBorderWidthAll(2);
		hover.SetCornerRadiusAll(7);
		hover.SetContentMarginAll(1);
		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", hover);
		button.AddThemeStyleboxOverride("pressed", hover);
		button.AddThemeStyleboxOverride("focus", hover);
		button.Pressed += pressed;
		button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -11f);
		parent.AddChild(button);
		return button;
	}

	private void AdjustDial(int index, int delta)
	{
		if (_isSolved || index < 0 || index >= _savedDialValues.Length ||
			GetTree().Paused || Dialogue.IsInputBlocked || Dialogue.IsShowing ||
			(GameUI.Instance?.IsBlockingOverlayOpen() ?? false))
			return;
		_selectedDial = index;
		_savedDialValues[index] = (_savedDialValues[index] + delta + 10) % 10;
		if (_dialValueLabels[index] != null)
			_dialValueLabels[index].Text = _savedDialValues[index].ToString();
		SetSelectedDial(index);
		AudioManager.Play(Sfx.DialTick, -5f, 0.88f + _savedDialValues[index] * 0.035f);
		RegisterDialAttempt();
		CheckPadlockSolution();
	}

	private void SetSelectedDial(int index)
	{
		_selectedDial = (index + 5) % 5;
		if (_dialStatusLabel != null)
			_dialStatusLabel.Text = $"DIAL {_selectedDial + 1}/5  ·  +/− OR SCROLL  ·  ←/→ SELECT  ·  ↑/↓ TURN";
		for (int i = 0; i < _dialValueLabels.Length; i++)
		{
			var label = _dialValueLabels[i];
			if (label == null)
				continue;
			label.AddThemeColorOverride("font_color", i == _selectedDial
				? new Color(1f, 0.94f, 0.75f)
				: new Color(0.92f, 0.78f, 0.58f));
		}
	}

	private void CheckPadlockSolution()
	{
		if (_isSolved)
			return;

		bool isCorrect = true;
		for (int i = 0; i < 5; i++)
		{
			if (_savedDialValues[i] != expectedCombination[i])
			{
				isCorrect = false;
				break;
			}
		}

		SetReadyToUnlock(isCorrect);
	}

	/// <summary>Lots of turning without opening means a stuck player: point
	/// them at the journal's clue cards first, then at the H hint.</summary>
	private void RegisterDialAttempt()
	{
		if (_isSolved || _readyToUnlock)
			return;

		_dialTicks++;
		if (_dialTicks == 30)
			GameState.Instance?.Toast("The numbers you've seen are kept in your journal — J, then Notes & Clues.");
		else if (_dialTicks == 70)
			GameState.Instance?.Toast("Still turning? Press H — the hint names what's missing, then the order.");
	}

	private void SetReadyToUnlock(bool ready)
	{
		bool wasReady = _readyToUnlock;
		_readyToUnlock = ready;
		if (_unlockButton != null)
		{
			// Always visible, always pressable. Bright when the dials feel
			// right, dimmer when they don't — never gone.
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
		if (GameState.Instance?.IsRoomSealed("Room1") == true &&
			GameState.Instance?.RoomHasUnclaimedPracticalItem("Room1") != true)
		{
			GameState.Instance.NotifySealedInteraction();
			return;
		}

		if (_isSolved)
			return;

		if (!_readyToUnlock)
		{
			// A real attempt, honestly answered: it doesn't turn, and the
			// way to the truth is the journal, not more guessing.
			_wrongUnlocks++;
			AudioManager.Play(Sfx.LockRattle, -5f);
			AudioManager.Play(Sfx.Denied, -11f);
			GameState.Instance?.Toast(_wrongUnlocks >= 2
				? "Still not right. Your Journal (J) keeps the clues under Notes & Clues — or press H and the house will nudge you."
				: "It doesn't turn — that isn't right. The combination is hidden in this room; what you've seen is kept in your Journal (J) → Notes & Clues.");
			return;
		}

		// The combination is the whole of this lock: the drawer was only
		// ever waiting to be remembered correctly. What the drawer keeps
		// beyond the brass key — a smaller copper key — is for later
		// rooms to ask about, not for this one to demand.
		_isSolved = true;
		IsUnlocked = true;
		_readyToUnlock = false;
		if (_unlockButton != null)
			_unlockButton.Visible = false;

		GameState.Instance?.NotifyPuzzleSolved();
		AudioManager.Play(Sfx.LockOpen, -4f);
		PlayDrawerAnimation();
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

	private async void PlayDrawerAnimation()
	{
		var drawer = GetNode<AnimatedSprite2D>("Drawer");
		if (drawer == null)

			return;


		await ToSignal(GetTree().CreateTimer(0.1f), "timeout");

		AudioManager.Play(Sfx.DrawerOpen, -5f);
		drawer.Play("DrawerOpen");


		await ToSignal(GetTree().CreateTimer(0.1f), "timeout");
		drawer.Stop();
		drawer.Frame = 1;


		var padlock = GetNode<Node2D>("Padlock");
		if (padlock != null)
			padlock.Visible = false;


		await ToSignal(GetTree().CreateTimer(0.03f), "timeout");

		var brassKey1 = GetNode<Area2D>("BrassKey1");
		if (brassKey1 != null)
			brassKey1.Visible = true;

		AudioManager.Play(Sfx.PuzzleSolve, -6f);
		GameState.Instance?.Toast("Something small and brass, kept all this time — and the drawer was never only keeping one thing.");
	}
}
