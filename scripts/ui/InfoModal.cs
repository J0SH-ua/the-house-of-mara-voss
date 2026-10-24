using System.Collections.Generic;
using Godot;

/// <summary>
/// The main menu's two books — Full Story and About — presented as an old
/// journal: a worn leather cover, a parchment page with ink-dark words, the
/// pictured scene pasted in on a parchment mat, one page at a time, turned
/// with Earlier/Later buttons or the arrow keys — never scrolled through
/// like a settings form. The scene supplies the furniture (backdrop, panel,
/// title, art, body, close); this class dresses and paginates it, so both
/// modals look like they belong to the same house.
/// </summary>
public partial class InfoModal : Control
{
    [Export]
    public NodePath BackdropPath { get; set; } = "Backdrop";

    [Export]
    public NodePath PanelPath { get; set; } = "Panel";

    [Export]
    public NodePath TitleLabelPath { get; set; } = "Panel/Content/Title";

    [Export]
    public NodePath ArtRectPath { get; set; } = "Panel/Content/Art";

    [Export]
    public NodePath BodyLabelPath { get; set; } = "Panel/Content/Scroll/Body";

    [Export]
    public NodePath CloseButtonPath { get; set; } = "Panel/Content/CloseButton";

    /// <summary>One pictured page of a book: a heading, the words, and the
    /// scene/room/location the page is about, shown above the text.</summary>
    public sealed class BookPage
    {
        public string Heading;
        public string Body;
        public string ArtPath;
        public Texture2D Art;

        public BookPage(string heading, string body, string artPath = null, Texture2D art = null)
        {
            Heading = heading;
            Body = body;
            ArtPath = artPath;
            Art = art;
        }
    }

    private static readonly Color Gold = new(0.96f, 0.85f, 0.6f);
    private static readonly Color GoldDim = new(0.78f, 0.64f, 0.4f, 0.9f);
    private static readonly Color Cream = new(0.94f, 0.9f, 0.82f);
    private static readonly Color Muted = new(0.76f, 0.72f, 0.65f, 0.85f);

    private Control _backdrop;
    private Control _panel;
    private Label _title;
    private TextureRect _art;
    private RichTextLabel _body;
    private BaseButton _closeButton;

    private readonly List<BookPage> _pages = new();
    private int _pageIndex;
    private Label _pageHeading;
    private Label _pageLabel;
    private Button _prevButton;
    private Button _nextButton;
    private ScrollContainer _scroll;
    private PanelContainer _artMat;
    private Tween _flipTween;

    public override void _Ready()
    {
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        _backdrop = GetNodeOrNull<Control>(BackdropPath);
        _panel = GetNodeOrNull<Control>(PanelPath);
        _title = GetNodeOrNull<Label>(TitleLabelPath);
        _art = GetNodeOrNull<TextureRect>(ArtRectPath);
        _body = GetNodeOrNull<RichTextLabel>(BodyLabelPath);
        _closeButton = GetNodeOrNull<BaseButton>(CloseButtonPath);
        _scroll = _body?.GetParent() as ScrollContainer;

        if (_closeButton != null)
            _closeButton.Pressed += Close;

        if (_backdrop != null)
            _backdrop.GuiInput += OnBackdropGuiInput;

        BuildBookChrome();
        ApplyBookStyle();

        // Make sure the modal can capture escape even when nothing is focused.
        SetProcessUnhandledKeyInput(true);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible)
            return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape)
                Close();
            else if (keyEvent.Keycode == Key.Right)
                TurnPage(1);
            else if (keyEvent.Keycode == Key.Left)
                TurnPage(-1);
        }
    }

    private void OnBackdropGuiInput(InputEvent @event)
    {
        if (!Visible)
            return;

        if (@event is InputEventMouseButton mouse && mouse.Pressed)
            Close();
    }

    /// <summary>Legacy single-page open; the books use OpenBook.</summary>
    public void Open(string title, string body, Texture2D art)
    {
        OpenBook(title, new List<BookPage> { new(string.Empty, body ?? string.Empty, null, art) });
    }

    public void OpenBook(string title, IReadOnlyList<BookPage> pages)
    {
        _pages.Clear();
        if (pages != null)
            _pages.AddRange(pages);
        if (_pages.Count == 0)
            _pages.Add(new BookPage(string.Empty, string.Empty));

        if (_title != null)
            _title.Text = title ?? string.Empty;

        AudioManager.Play(Sfx.PageTurn, -8f);
        Visible = true;
        ShowPage(0, animate: false);
        GrabFocus();
        _closeButton?.GrabFocus();
    }

    public void Close()
    {
        if (Visible)
            AudioManager.Play(Sfx.UiBack, -8f);
        Visible = false;
    }

    private void TurnPage(int direction)
    {
        if (_pages.Count == 0)
            return;

        int next = Mathf.Clamp(_pageIndex + direction, 0, _pages.Count - 1);
        if (next == _pageIndex)
            return;

        AudioManager.Play(Sfx.PageTurn, -9f, direction > 0 ? 1f : 0.9f);
        ShowPage(next);
    }

    private void ShowPage(int index, bool animate = true)
    {
        if (_pages.Count == 0)
            return;

        _pageIndex = Mathf.Clamp(index, 0, _pages.Count - 1);
        var page = _pages[_pageIndex];
        bool multiPage = _pages.Count > 1;

        if (_pageHeading != null)
        {
            _pageHeading.Text = page.Heading ?? string.Empty;
            _pageHeading.Visible = !string.IsNullOrWhiteSpace(page.Heading);
        }

        if (_body != null)
            _body.Text = page.Body ?? string.Empty;

        if (_scroll != null)
            _scroll.ScrollVertical = 0;

        if (_art != null)
        {
            var texture = page.Art;
            if (texture == null && !string.IsNullOrEmpty(page.ArtPath))
                texture = GD.Load<Texture2D>(page.ArtPath);
            _art.Texture = texture;
            _art.Visible = texture != null;
            if (_artMat != null)
                _artMat.Visible = texture != null;
        }

        if (_pageLabel != null)
            _pageLabel.Text = multiPage ? $"— page {_pageIndex + 1} of {_pages.Count} —" : string.Empty;
        if (_prevButton != null)
        {
            _prevButton.Visible = multiPage;
            _prevButton.Disabled = _pageIndex <= 0;
        }
        if (_nextButton != null)
        {
            _nextButton.Visible = multiPage;
            _nextButton.Disabled = _pageIndex >= _pages.Count - 1;
        }

        // A page turning should feel like paper moving, not a form
        // refreshing: the new page settles in with a soft fade.
        _flipTween?.Kill();
        if (animate && !Settings.ReduceMotionOrDefault())
        {
            if (_art != null)
                _art.Modulate = new Color(1, 1, 1, 0.25f);
            if (_body != null)
                _body.Modulate = new Color(1, 1, 1, 0.25f);
            if (_pageHeading != null)
                _pageHeading.Modulate = new Color(1, 1, 1, 0.25f);

            _flipTween = CreateTween();
            if (_art != null)
                _flipTween.TweenProperty(_art, "modulate:a", 1f, 0.3f);
            if (_body != null)
                _flipTween.Parallel().TweenProperty(_body, "modulate:a", 1f, 0.3f);
            if (_pageHeading != null)
                _flipTween.Parallel().TweenProperty(_pageHeading, "modulate:a", 1f, 0.3f);
        }
        else
        {
            if (_art != null)
                _art.Modulate = Colors.White;
            if (_body != null)
                _body.Modulate = Colors.White;
            if (_pageHeading != null)
                _pageHeading.Modulate = Colors.White;
        }
    }

    private void BuildBookChrome()
    {
        var content = _panel?.GetNodeOrNull<VBoxContainer>("Content");
        if (content == null)
            return;

        // The pictured scene is pasted into the journal on a parchment
        // mat, the way a kept photograph would be.
        if (_art != null && _art.GetParent() == content)
        {
            int artIndex = _art.GetIndex();
            var mat = new StyleBoxFlat
            {
                BgColor = new Color(0.84f, 0.77f, 0.6f, 1f),
                BorderColor = new Color(0.48f, 0.37f, 0.23f, 1f),
            };
            mat.SetBorderWidthAll(1);
            mat.SetCornerRadiusAll(2);
            mat.ContentMarginLeft = 10;
            mat.ContentMarginRight = 10;
            mat.ContentMarginTop = 10;
            mat.ContentMarginBottom = 10;
            _artMat = new PanelContainer();
            _artMat.AddThemeStyleboxOverride("panel", mat);
            content.AddChild(_artMat);
            content.MoveChild(_artMat, artIndex);
            content.RemoveChild(_art);
            _artMat.AddChild(_art);
        }

        _pageHeading = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pageHeading.AddThemeFontSizeOverride("font_size", 24);
        _pageHeading.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.62f));
        content.AddChild(_pageHeading);
        content.MoveChild(_pageHeading, 1); // under the book's title

        var nav = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        nav.AddThemeConstantOverride("separation", 18);

        _prevButton = MakeBookButton("←  Earlier page");
        _prevButton.Pressed += () => TurnPage(-1);
        nav.AddChild(_prevButton);

        _pageLabel = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pageLabel.AddThemeFontSizeOverride("font_size", 17);
        _pageLabel.AddThemeColorOverride("font_color", Muted);
        nav.AddChild(_pageLabel);

        _nextButton = MakeBookButton("Later page  →");
        _nextButton.Pressed += () => TurnPage(1);
        nav.AddChild(_nextButton);

        content.AddChild(nav);
        if (_closeButton != null)
            content.MoveChild(nav, _closeButton.GetIndex()); // turning pages sits above Close
    }

    private void ApplyBookStyle()
    {
        if (_backdrop is ColorRect backdrop)
            backdrop.Color = new Color(0.015f, 0.012f, 0.01f, 0.82f);

        if (_panel is PanelContainer panel)
        {
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.1f, 0.072f, 0.045f, 0.99f),
                BorderColor = GoldDim,
                ShadowSize = 18,
                ShadowColor = new Color(0, 0, 0, 0.55f),
            };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(8);
            style.ContentMarginLeft = 32;
            style.ContentMarginRight = 32;
            style.ContentMarginTop = 24;
            style.ContentMarginBottom = 24;
            panel.AddThemeStyleboxOverride("panel", style);
        }

        _title?.AddThemeColorOverride("font_color", Gold);

        // The page itself: aged paper inside the leather cover.
        if (_scroll != null)
        {
            var pageStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.8f, 0.72f, 0.56f, 1f),
                BorderColor = new Color(0.45f, 0.35f, 0.22f, 1f),
            };
            pageStyle.SetBorderWidthAll(1);
            pageStyle.SetCornerRadiusAll(3);
            pageStyle.ContentMarginLeft = 26;
            pageStyle.ContentMarginRight = 26;
            pageStyle.ContentMarginTop = 20;
            pageStyle.ContentMarginBottom = 20;
            _scroll.AddThemeStyleboxOverride("panel", pageStyle);
        }

        if (_body != null)
        {
            _body.AddThemeColorOverride("default_color", new Color(0.17f, 0.12f, 0.08f));
            _body.AddThemeFontSizeOverride("normal_font_size", 20);
            _body.AddThemeFontSizeOverride("bold_font_size", 20);
            _body.AddThemeConstantOverride("line_separation", 7);
        }

        if (_art != null)
            _art.CustomMinimumSize = new Vector2(0, 240);

        if (_closeButton is Button close)
            ApplyBookButtonStyle(close);
    }

    private static Button MakeBookButton(string text)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(196, 44),
        };
        ApplyBookButtonStyle(button);
        return button;
    }

    private static void ApplyBookButtonStyle(Button button)
    {
        button.AddThemeFontSizeOverride("font_size", 19);
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Gold);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.56f, 0.5f, 0.45f));

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.075f, 0.055f, 0.95f),
            BorderColor = new Color(0.72f, 0.6f, 0.4f, 0.55f),
        };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(5);
        normal.SetContentMarginAll(8);

        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.72f, 0.48f, 0.16f),
            BorderColor = GoldDim,
        };
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(5);
        hover.SetContentMarginAll(8);

        var disabled = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.05f, 0.04f, 0.6f),
            BorderColor = new Color(0.5f, 0.44f, 0.34f, 0.25f),
        };
        disabled.SetBorderWidthAll(1);
        disabled.SetCornerRadiusAll(5);
        disabled.SetContentMarginAll(8);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.MouseEntered += () => AudioManager.Play(Sfx.UiHover, -10f);
    }
}
