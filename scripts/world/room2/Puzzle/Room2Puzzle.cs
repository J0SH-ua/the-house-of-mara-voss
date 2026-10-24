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
	private Vector2 _padlockHome;

	// Typed letters survive stepping back to the room mid-word (statics live
	// for the whole run; GameState.NewGame clears them).
	private static readonly char[] _savedLetters = new char[4];

	/// <summary>Clears remembered letters (new game / fresh load / wrong word).</summary>
	public static void ResetSavedState()
	{
		System.Array.Clear(_savedLetters, 0, _savedLetters.Length);
	}

	public override void _Ready()
	{
		_padlock = GetNodeOrNull<Node2D>("Padlock");
		if (_padlock != null)
		{
			_padlock.Visible = true;
			_padlockHome = _padlock.Position;
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
	}

	private void OnLetterBoxGuiInput(int index, InputEvent @event)
	{
		if (_isSolved)
			return;

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
				return;

			chars[i] = char.ToUpperInvariant(text[0]);
		}

		string attempt = new string(chars);
		if (!string.Equals(attempt, TargetWord, StringComparison.Ordinal))
		{
			WrongAttemptAsync();
			return;
		}

		_isSolved = true;
		SolveAsync();
	}

	private async void WrongAttemptAsync()
	{
		if (_feedbackPlaying)
			return;
		_feedbackPlaying = true;

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
		ResetSavedState();
		GetBox(0)?.GrabFocus();
		_feedbackPlaying = false;
	}

	private async void SolveAsync()
	{
		AudioManager.Play(Sfx.LockOpen, -4f);
		await PlayDrawerAnimation();
		RevealPhotoReward();
		_padlock?.SetDeferred("visible", false);
		AudioManager.Play(Sfx.PuzzleSolve, -6f);
		GameState.Instance?.Toast("Inside: a photograph, face-down all these years.");
	}

	private void RevealPhotoReward()
	{
		var photoReward = GetNodeOrNull<Area2D>("Photo1");
		if (photoReward != null)
			photoReward.Visible = true;
	}

	private async System.Threading.Tasks.Task PlayDrawerAnimation()
	{
		var drawer = GetNodeOrNull<AnimatedSprite2D>("Drawer");
		if (drawer == null)
			return;

		AudioManager.Play(Sfx.DrawerOpen, -5f);
		drawer.Play("DrawerOpen");
		await ToSignal(GetTree().CreateTimer(0.35f), SceneTreeTimer.SignalName.Timeout);
		drawer.Stop();
		drawer.Frame = 1;
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
