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
		for (int i = 0; i < 5; i++)
		{
			var spinbox = GetNode<SpinBox>($"Padlock/padlock-{i}");
			if (spinbox != null)
			{
				spinbox.MinValue = 0;
				spinbox.MaxValue = 9;
				spinbox.Step = 1;
				spinbox.SetValueNoSignal(_savedDialValues[i]);
				int dialIndex = i;
				spinbox.ValueChanged += (double value) =>
				{
					_savedDialValues[dialIndex] = (int)value;
					if (_isSolved)
						return;
					AudioManager.Play(Sfx.DialTick, -5f, 0.88f + (float)value * 0.035f);
					RegisterDialAttempt();
					CheckPadlockSolution();
				};
			}
		}

		// The right combination no longer springs the drawer by itself: the
		// player does the unlocking. The Unlock button is always there and
		// always pressable — a wrong guess rattles and points at the journal
		// instead of hiding the way forward. (Under Padlock: Esc hides it.)
		var firstDial = GetNodeOrNull<SpinBox>("Padlock/padlock-0");
		var lastDial = GetNodeOrNull<SpinBox>("Padlock/padlock-4");
		Vector2 unlockPos = new Vector2(-85, 96);
		if (firstDial != null && lastDial != null)
		{
			unlockPos = new Vector2(
				(firstDial.Position.X + lastDial.Position.X + lastDial.Size.X) / 2f - 85f,
				lastDial.Position.Y + lastDial.Size.Y + 16f);
		}
		_unlockButton = BuildUnlockButton(GetNode<Node2D>("Padlock"), unlockPos);
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

		if (@event is not InputEventMouseButton mouseEvent)
			return;

		if (!mouseEvent.Pressed || mouseEvent.ButtonIndex != MouseButton.Left)
			return;

		if (IsClickInsidePuzzle(mouseEvent.Position))
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
		for (int i = 0; i < 5; i++)
		{
			var spinbox = GetNodeOrNull<Control>($"Padlock/padlock-{i}");
			if (spinbox != null && spinbox.IsVisibleInTree() && spinbox.GetGlobalRect().HasPoint(globalMousePosition))
				return true;
		}

		var title = GetNodeOrNull<Control>("Padlock/Title");
		return title != null && title.IsVisibleInTree() && title.GetGlobalRect().HasPoint(globalMousePosition);
	}

	private void CheckPadlockSolution()
	{
		if (_isSolved)
			return;

		int[] currentValues = new int[5];
		bool allFound = true;

		for (int i = 0; i < 5; i++)
		{
			var spinbox = GetNode<SpinBox>($"Padlock/padlock-{i}");
			if (spinbox != null)
			{
				currentValues[i] = (int)spinbox.Value;
			}
			else
			{
				allFound = false;
				break;
			}
		}

		if (!allFound) return;


		bool isCorrect = true;
		for (int i = 0; i < 5; i++)
		{
			if (currentValues[i] != expectedCombination[i])
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
