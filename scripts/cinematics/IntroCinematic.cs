using System;
using Godot;
using System.Threading.Tasks;

/// <summary>
/// Opening title sequence. Each line stays on screen until the player
/// clicks (or presses Space/Enter), so there is time to read it; a visible
/// “Click to continue” prompt appears with every line. Esc and the Skip
/// button still jump straight to the menu. Music goes through
/// AudioManager so it crossfades into the menu theme instead of
/// hard-cutting between two throwaway players.
/// </summary>
public partial class IntroCinematic : Control
{
	private const float BaseViewportWidth = 1152f;
	private const float BaseViewportHeight = 648f;
	private const int BaseIntroFontSize = 36;
	private const int BaseTitleFontSize = 56;
	private const float BaseIntroMinimumWidth = 900f;
	private const float BaseTitleMinimumWidth = 209f;
	private const string MainMenuScenePath = "res://scenes/ui/main_menu.tscn";

	private ColorRect _blackBg;
	private Label _introText;
	private Label _titleCard;
	private ColorRect _whiteFlash;
	private Button _skipButton;
	private Label _continueLabel;
	private TaskCompletionSource<bool> _continueSource;

	private bool _finished;
	private bool _waitingForContinue;
	private ulong _startedMsec;

	public override void _Ready()
	{
		_blackBg = GetNode<ColorRect>("BlackBG");
		_introText = GetNode<Label>("IntroText");
		_titleCard = GetNode<Label>("TitleCard");
		_whiteFlash = GetNode<ColorRect>("WhiteFlash");

		AudioManager.Instance?.StopAmbience();
		AudioManager.Instance?.PlayMusic(AudioManager.MusicIntro, -5f, 1.6f);

		ConfigureFullscreenRect(_blackBg, new Color(0, 0, 0, 1), 0);
		ConfigureFullscreenRect(_whiteFlash, new Color(1, 1, 1, 1), 2);
		_introText.ZIndex = 1;
		_titleCard.ZIndex = 3;
		_titleCard.AddThemeColorOverride("font_color", new Color(0.05f, 0.05f, 0.05f, 1.0f));
		GetViewport().SizeChanged += ApplyTypographyScale;

		_introText.Modulate = new Color(1, 1, 1, 0);
		_titleCard.Modulate = new Color(1, 1, 1, 0);
		_whiteFlash.Modulate = new Color(1, 1, 1, 0);

		BuildContinueLabel();
		BuildSkipButton();
		ApplyTypographyScale();
		_startedMsec = Time.GetTicksMsec();

		_ = RunSequence();
	}

	public override void _ExitTree()
	{
		if (GetViewport() != null)
		{
			GetViewport().SizeChanged -= ApplyTypographyScale;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (_finished)
			return;

		// Grace period so the click that launched the game can't run past
		// the first readable line.
		if (Time.GetTicksMsec() - _startedMsec < 900)
			return;

		// The Skip button owns its own click; don't let that same press also
		// count as “continue”.
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left &&
			_skipButton != null && _skipButton.GetGlobalRect().HasPoint(mb.Position))
		{
			return;
		}

		if (@event is InputEventKey escapeKey && escapeKey.Pressed && !escapeKey.Echo &&
			escapeKey.Keycode == Key.Escape)
		{
			Finish();
			return;
		}

		bool advance = (@event is InputEventMouseButton click && click.Pressed &&
						click.ButtonIndex == MouseButton.Left)
					|| (@event is InputEventKey key && key.Pressed && !key.Echo &&
						key.Keycode is Key.Space or Key.Enter or Key.KpEnter);

		if (advance)
			RequestContinue();
	}

	private void RequestContinue()
	{
		if (!_waitingForContinue || _finished)
			return;

		_waitingForContinue = false;
		AudioManager.Play(Sfx.UiClick, -10f, 0.9f);
		_continueSource?.TrySetResult(true);
	}

	private async Task WaitForContinue()
	{
		if (_finished)
			return;

		// Event-driven on purpose: the line simply stays readable until
		// _Input completes this source. No per-frame polling loop.
		_continueSource = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_waitingForContinue = true;
		if (_continueLabel != null)
			_continueLabel.Visible = true;

		await _continueSource.Task;
		_waitingForContinue = false;
	}

	public override void _Process(double delta)
	{
		if (_skipButton == null || _finished)
			return;

		// The Skip button drifts in after a couple of seconds, then breathes.
		ulong elapsed = Time.GetTicksMsec() - _startedMsec;
		float target = elapsed > 2200
			? (Settings.ReduceMotionOrDefault() ? 0.9f : 0.8f + 0.15f * (float)Math.Sin(Time.GetTicksMsec() / 600.0))
			: 0f;
		var m = _skipButton.Modulate;
		m.A = Mathf.MoveToward(m.A, target, (float)delta * 0.6f);
		_skipButton.Modulate = m;

		if (_continueLabel != null)
		{
			float continueTarget = _waitingForContinue
				? (Settings.ReduceMotionOrDefault()
					? 0.95f
					: 0.82f + 0.16f * (float)Math.Sin(Time.GetTicksMsec() / 520.0))
				: 0f;
			var cm = _continueLabel.Modulate;
			cm.A = Mathf.MoveToward(cm.A, continueTarget, (float)delta * 1.8f);
			_continueLabel.Modulate = cm;
			_continueLabel.Visible = cm.A > 0.01f;
		}
	}

	private void BuildContinueLabel()
	{
		_continueLabel = new Label
		{
			Name = "ContinueLabel",
			Text = "Click to continue",
			ZIndex = 4,
			AnchorLeft = 0.5f,
			AnchorTop = 1,
			AnchorRight = 0.5f,
			AnchorBottom = 1,
			OffsetLeft = -220,
			OffsetTop = -104,
			OffsetRight = 220,
			OffsetBottom = -68,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false,
			Modulate = new Color(1, 1, 1, 0),
		};
		var font = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		if (font != null)
			_continueLabel.AddThemeFontOverride("font", font);
		_continueLabel.AddThemeFontSizeOverride("font_size", 24);
		_continueLabel.AddThemeColorOverride("font_color", new Color(0.94f, 0.9f, 0.82f));

		var chip = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.055f, 0.05f, 0.62f),
			BorderColor = new Color(0.85f, 0.8f, 0.7f, 0.38f),
		};
		chip.SetBorderWidthAll(1);
		chip.SetCornerRadiusAll(4);
		chip.SetContentMarginAll(8);
		_continueLabel.AddThemeStyleboxOverride("normal", chip);
		AddChild(_continueLabel);
	}

	private void BuildSkipButton()
	{
		_skipButton = new Button
		{
			Name = "SkipButton",
			Text = "Skip",
			ZIndex = 4,
			AnchorLeft = 1,
			AnchorTop = 1,
			AnchorRight = 1,
			AnchorBottom = 1,
			OffsetLeft = -176,
			OffsetTop = -66,
			OffsetRight = -30,
			OffsetBottom = -22,
			FocusMode = FocusModeEnum.None,
			Modulate = new Color(1, 1, 1, 0),
		};
		var font = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf");
		if (font != null)
			_skipButton.AddThemeFontOverride("font", font);
		_skipButton.AddThemeFontSizeOverride("font_size", 22);
		_skipButton.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.8f));
		_skipButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.97f, 0.9f));

		var normal = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.07f, 0.06f, 0.55f),
			BorderColor = new Color(0.85f, 0.8f, 0.7f, 0.4f),
		};
		normal.SetBorderWidthAll(1);
		normal.SetCornerRadiusAll(4);
		normal.SetContentMarginAll(8);
		var hover = new StyleBoxFlat
		{
			BgColor = new Color(0.85f, 0.78f, 0.62f, 0.22f),
			BorderColor = new Color(0.92f, 0.87f, 0.76f, 0.75f),
		};
		hover.SetBorderWidthAll(1);
		hover.SetCornerRadiusAll(4);
		hover.SetContentMarginAll(8);
		_skipButton.AddThemeStyleboxOverride("normal", normal);
		_skipButton.AddThemeStyleboxOverride("hover", hover);
		_skipButton.AddThemeStyleboxOverride("pressed", hover);
		_skipButton.AddThemeStyleboxOverride("focus", hover);

		_skipButton.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -10f);
		_skipButton.Pressed += () =>
		{
			AudioManager.Play(Sfx.UiClick, -6f);
			Finish();
		};
		AddChild(_skipButton);
	}

	private async Task RunSequence()
	{
		try
		{
			await Wait(2.0f);
			if (_finished) return;

			await ShowLine("Memory is not a recording.", 1.2f, 1.0f);
			if (_finished) return;

			await ShowLine("It is a story a mind tells itself about what it cannot bear to look at directly.", 1.6f, 1.0f);
			if (_finished) return;

			await ShowLine("Mara Voss has not left her apartment in four months. She is not aware of this.", 1.4f, 1.0f);
			if (_finished) return;

			await ShowLine("She believes she is home — in Eldfen, in the house she grew up in. Warm. Kept. Not empty.", 1.4f, 1.0f);
			if (_finished) return;

			await ShowLine("So her mind built the house again. Seven rooms. Each one holds a version of something that happened.", 1.4f, 1.0f);
			if (_finished) return;

			await ShowLine("Some of what it shows you is true. Some of it is what she can bear instead.", 1.4f, 1.0f);
			if (_finished) return;

			await ShowLine("You are not Mara Voss.", 1.0f, 0.8f);
			if (_finished) return;

			await ShowLine("You are the part of her that still knows something is wrong.", 1.4f, 1.0f);
			if (_finished) return;

			// Reduce Motion: the white flash becomes a slow, shallower fade.
			bool reduce = Settings.ReduceMotionOrDefault();
			await FadeRectAlpha(_whiteFlash, reduce ? 0.85f : 1.0f, reduce ? 1.6f : 0.8f);
			if (_finished) return;

			await FadeLabelAlpha(_titleCard, 1.0f, 0.8f);
			if (_finished) return;
			await WaitForContinue();
			if (_finished) return;

			await FadeLabelAlpha(_titleCard, 0.0f, 0.6f);
			if (_finished) return;
			await FadeRectAlpha(_whiteFlash, 0.0f, 0.8f);
			if (_finished) return;
			await Wait(0.3f);

			Finish();
		}
		catch (Exception)
		{
			// Scene was freed mid-sequence (skip). Nothing to clean up.
		}
	}

	private void Finish()
	{
		if (_finished)
			return;
		_finished = true;
		_waitingForContinue = false;
		_continueSource?.TrySetResult(true);

		// Hand off to the Main Menu; AudioManager crossfades the score.
		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}

	private static void ConfigureFullscreenRect(ColorRect rect, Color color, int zIndex)
	{
		rect.SetAnchorsPreset(LayoutPreset.FullRect);
		rect.OffsetLeft = 0;
		rect.OffsetTop = 0;
		rect.OffsetRight = 0;
		rect.OffsetBottom = 0;
		rect.Color = color;
		rect.ZIndex = zIndex;
		// Never swallow clicks: click-to-continue (and Skip) must see them.
		rect.MouseFilter = MouseFilterEnum.Ignore;
	}

	private void ApplyTypographyScale()
	{
		var viewportSize = GetViewportRect().Size;
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
			return;

		var scaleX = viewportSize.X / BaseViewportWidth;
		var scaleY = viewportSize.Y / BaseViewportHeight;
		var scale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.75f, 1.75f);

		_continueLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(24 * scale));
		_introText.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(BaseIntroFontSize * scale));
		_titleCard.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(BaseTitleFontSize * scale));
		_introText.CustomMinimumSize = new Vector2(BaseIntroMinimumWidth * scale, _introText.CustomMinimumSize.Y);
		_titleCard.CustomMinimumSize = new Vector2(BaseTitleMinimumWidth * scale, _titleCard.CustomMinimumSize.Y);
	}

	private async Task ShowLine(string text, float fadeInDuration, float fadeOutDuration)
	{
		_introText.Text = text;
		await FadeLabelAlpha(_introText, 1.0f, fadeInDuration);
		if (_finished)
			return;
		// The line remains up until the player has read it and clicks.
		await WaitForContinue();
		if (_finished)
			return;
		await FadeLabelAlpha(_introText, 0.0f, fadeOutDuration);
	}

	private async Task FadeLabelAlpha(Label label, float targetAlpha, float duration)
	{
		if (_finished)
			return;
		var tween = CreateTween();
		tween.TweenProperty(label, "modulate:a", targetAlpha, duration);
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	private async Task FadeRectAlpha(ColorRect rect, float targetAlpha, float duration)
	{
		if (_finished)
			return;
		var tween = CreateTween();
		tween.TweenProperty(rect, "modulate:a", targetAlpha, duration);
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	private async Task Wait(float seconds)
	{
		if (_finished)
			return;
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	}
}
