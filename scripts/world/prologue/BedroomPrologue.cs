using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// The prologue: Mara's childhood bedroom, first thing in the morning.
///
/// The original post-Play memory cutscene plays over this room first; then
/// a New Game wakes here — first person, eyelids and all — staring at a
/// ceiling she has not seen in twenty years. The bedroom is also the live
/// tutorial: all five ordinary things must be examined before Mara will
/// open the door. Beyond it she expects the hallway; the house gives her
/// the kitchen instead. The how-to-play cards and HUD tour are offered here,
/// after Mara has woken, never on kitchen entry.
///
/// The house moved the hallway the first time — but it relents for one
/// small reason: Mum's bent hairpin sleeps in the nightstand's little
/// drawer, visible and takeable, and a skipped hairpin must stay
/// fetchable. Step Back from the kitchen returns here, the sealed room
/// keeps one exception for the nightstand, and that is all.
/// </summary>
public partial class BedroomPrologue : MemoryRoomBase
{
    private const string AwakeFlag = "prologue_awake";
    private const string FromBedroomFlag = "prologue_from_bedroom";
    private const string KitchenScenePath = "res://scenes/world/room_1.tscn";
    private const string CeilingPath = "res://assets/images/bgs/Bedroom_Ceiling_FP.png";
    private const string HairpinFlag = "item_hairpin";
    private const string HairpinIconPath = "res://assets/images/core/items/hairpin.png";
    private static readonly Rect2 HairpinDrawRect = new(36, 452, 86, 86);

    private Texture2D _hairpinTexture;

    // ------------------------------------------------ the drawer close-up
    // Clicking the nightstand no longer decides for her. The drawer
    // gets its own close-up: the knob first, then a real choice —
    // open it, or don't — in Mara's own words either way. The
    // hairpin is taken from inside the drawer, by its own click,
    // and is gone from the drawer once it is in the bag.

    private const string DrawerOpenedFlag = "bedroom_drawer_opened";

    private enum DrawerState { Room, Closed, Asking, Open }

    private static readonly Rect2 DrawerLeaveRect = new(16, 468, 216, 44);
    private static readonly Rect2 DrawerFrontRect = new(326, 168, 500, 300);
    private static readonly Rect2 KnobRect = new(544, 286, 64, 64);
    private static readonly Rect2 CloseupHairpinRect = new(501, 296, 150, 150);
    private static readonly Rect2 ChoiceYesRect = new(316, 508, 250, 52);
    private static readonly Rect2 ChoiceNoRect = new(586, 508, 250, 52);

    private DrawerState _drawer = DrawerState.Room;
    private bool _drawerBusy;
    private bool _drawerCompletedPending;
    private bool _drawerCursorOwned;
    private FontFile _drawerFont;
    private Label _drawerPromptLabel;
    private Label _drawerYesLabel;
    private Label _drawerNoLabel;
    private Label _drawerLeaveLabel;

    private TextureRect _ceiling;
    private ColorRect _lidTop;
    private ColorRect _lidBottom;

    protected override string RoomName => "Bedroom";
    protected override string BackgroundPath => "res://assets/images/bgs/Bedroom_Prologue_BG.png";

    protected override IEnumerable<Hotspot> BuildHotspots()
    {
        // Later entries win overlaps: the bed first, the door last.
        yield return new Hotspot("Bed", "The Bed",
            new Rect2(110, 320, 530, 220), new Vector2(380, 440), Sfx.Examine, 0.8f);
        yield return new Hotspot("Window", "The Window",
            new Rect2(350, 85, 210, 300), new Vector2(455, 235), Sfx.WordChime, 0.92f);
        yield return new Hotspot("Nightstand", "The Nightstand",
            new Rect2(4, 395, 150, 245), new Vector2(78, 500), Sfx.Examine, 0.9f);
        yield return new Hotspot("Drawing", "A Child's Drawing",
            new Rect2(618, 195, 155, 115), new Vector2(695, 252), Sfx.Examine, 1.1f);
        yield return new Hotspot("Wardrobe", "The Wardrobe",
            new Rect2(782, 135, 185, 400), new Vector2(875, 340), Sfx.Examine, 0.75f);
        yield return new Hotspot("Door", "The Door",
            new Rect2(998, 60, 140, 565), new Vector2(1068, 350),
            Sfx.DoorCreak, 0.9f, PointerCursorPath, false);
    }

    protected override void OnRoomReady()
    {
        _hairpinTexture = GD.Load<Texture2D>(HairpinIconPath);
        _drawerFont = GD.Load<FontFile>(FontPath);
        BuildDrawerLabels();
        var gs = GameState.Instance;
        if (gs != null && !gs.HasFlag(AwakeFlag))
            _ = PlayWakingAfterOpeningAsync();
    }

    /// <summary>The finished bedroom goes quiet like every finished
    /// room — except around the nightstand, while its drawer still
    /// keeps the hairpin. Coming back for it always works.</summary>
    protected override bool HotspotSealedException(Hotspot hotspot) =>
        hotspot.Id == "Nightstand" &&
        !(GameState.Instance?.HasFlag(HairpinFlag) ?? false);

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_drawer != DrawerState.Room)
        {
            UpdateDrawerCursor();
            QueueRedraw();
            return;
        }
        // The hairpin's beckoning ring breathes while it waits.
        if (WasExamined("Nightstand") &&
            !(GameState.Instance?.HasFlag(HairpinFlag) ?? false))
            QueueRedraw();
    }

    protected override async Task OnRequiredExaminesCompletedAsync()
    {
        AudioManager.Play(Sfx.RoomComplete, -8f);
        await Say("There. Bed, window, clock, drawing, wardrobe. I've looked at the room I woke in. Now I can face whatever put me here.", 5.5f, 24);
        GameState.Instance?.Toast("Everything in the bedroom has been explored. The door is ready.");
    }

    protected override void OnHotspotClicked(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        switch (hotspot.Id)
        {
            case "Bed":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Still warm on my side. So I really slept here.",
                    "It's a child's bed. My feet would hang off the end of it now. In here — wherever here is — they don't.",
                }, "Warm. Small. Mine. All three impossible.");
                break;

            case "Window":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "Morning over the garden. The apple trees. The fence. The whole of it, exactly—",
                    "It's too still. Like the garden is a photograph of itself, waiting for someone to say it's allowed to move.",
                }, "Still morning. Still too still.");
                break;

            case "Nightstand":
                _ = ExamineNightstandAsync(hotspot);
                break;

            case "Drawing":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "…I made that. Two girls and a house with too many windows.",
                    "The tall one in pink is meant to be me. The little one holds my hand in every drawing I ever did. I don't remember making this one. My hand does.",
                }, "Two girls. A house. Too many windows.");
                break;

            case "Wardrobe":
                _ = ExamineObjectAsync(hotspot, new[]
                {
                    "My wardrobe. Behind that door are jumpers small enough to break my heart.",
                    "Not yet. One impossible thing at a time.",
                }, "Not yet.");
                break;

            case "Door":
                _ = LeaveThroughDoorAsync();
                break;
        }
    }

    /// <summary>The nightstand is one of the five objects Mara must face
    /// before the door works — and its little drawer is where this
    /// house keeps the first practical thing: Mum's bent hairpin,
    /// drawn on the nightstand once the drawer is open, taken by its
    /// own click. She may walk out of the tutorial without it; the
    /// way back to this room exists for exactly that regret.</summary>
    private async Task ExamineNightstandAsync(Hotspot hotspot)
    {
        if (hotspot == null || Busy)
            return;

        Busy = true;
        try
        {
            bool first = RecordExamine(hotspot);
            _drawerCompletedPending = first && AllRequiredExamined();
            AudioManager.PlayVaried(hotspot.SfxId, -5f, hotspot.SfxPitch, 0.04f);

            // After the hairpin is hers the nightstand is just the
            // stopped clock again. Until then every visit returns to
            // the drawer itself — the taking is done by hand, inside.
            if (!first && (GameState.Instance?.HasFlag(HairpinFlag) ?? false))
            {
                GameState.Instance?.RegisterRepeatExaminePenalty();
                await Say("Stopped. Everything here is stopped except me.", 3.5f, 24);
                return;
            }

            if (first)
            {
                await SaySequence(new[]
                {
                    "My alarm clock. The hands have stopped, and honestly? Fair enough.",
                    "It used to ring me awake for school. She'd already be in the doorway talking before I'd even sat up.",
                });
                if (!StillHere())
                    return;
                await Say("Under the clock: the little drawer every childhood nightstand is legally required to have. My hand finds the knob — and stops there. Whatever child-me kept in the dark of this drawer, adult me gets a say before it sees the light.", 5.5f, 24);
                if (!StillHere())
                    return;
            }

            // Into the close-up. Busy stays raised the whole visit —
            // the room waits with its hands folded — and stepping
            // back (ExitDrawerCloseup) is what lowers it again. The
            // tutorial's completion beat rides the step back so the
            // door's waking-up never talks over the drawer.
            _drawer = (GameState.Instance?.HasFlag(DrawerOpenedFlag) ?? false)
                ? DrawerState.Open
                : DrawerState.Closed;
            RefreshDrawerLabels();
            QueueRedraw();
        }
        finally
        {
            if (StillHere() && _drawer == DrawerState.Room)
            {
                Busy = false;
                QueueRedraw();
            }
        }
    }

    private async Task TakeHairpinAsync()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.HasFlag(HairpinFlag))
            return;

        AudioManager.Play(Sfx.KeyType, -10f, 1.3f);
        await Say("Mum's. She could open a stuck preserve jar with one of these and a look. Borrowed for a hundred jobs it wasn't made for, and bent by every single one. …Come on, then. Be useful again.", 5.5f, 24);
        if (!StillHere())
            return;

        gs.SetFlag(HairpinFlag, save: false);
        gs.UnlockClue("bedroom_hairpin");
        gs.Toast("Mum's bent hairpin — kept in Mara's bag (B). Thin enough for seals that fingernails only worry.");
        gs.SaveGame("hairpin taken");
        QueueRedraw();
    }

    protected override void DrawRoom()
    {
        if (_drawer != DrawerState.Room)
        {
            DrawDrawerCloseup();
            return;
        }

        // Only once she has chosen to open it does the drawer's
        // secret catch the light out here too — and once the
        // hairpin is in the bag it is gone from the drawer for good.
        if (_hairpinTexture == null || !WasExamined("Nightstand"))
            return;
        if (!(GameState.Instance?.HasFlag(DrawerOpenedFlag) ?? false))
            return;
        if (GameState.Instance?.HasFlag(HairpinFlag) ?? false)
            return;

        float pulse = Settings.ReduceMotionOrDefault()
            ? 0.72f
            : 0.6f + 0.22f * (float)System.Math.Sin(Time.GetTicksMsec() / 240.0);
        DrawArc(HairpinDrawRect.GetCenter(), 52f, 0f, Mathf.Tau, 40,
            new Color(1f, 0.85f, 0.5f, pulse * 0.55f), 2f, true);
        DrawTextureRect(_hairpinTexture, HairpinDrawRect, false);
    }

    // -------------------------------------------- the drawer close-up

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_drawer == DrawerState.Room || @event is not InputEventMouseButton mouse ||
            !mouse.Pressed || mouse.ButtonIndex != MouseButton.Left)
            return;
        if (GetTree().Paused || Dialogue.IsInputBlocked || Dialogue.IsShowing ||
            _drawerBusy || SceneManager.IsChanging ||
            (GameUI.Instance?.IsBlockingOverlayOpen() ?? false))
            return;

        Vector2 position = GetGlobalMousePosition();
        bool hairpinTaken = GameState.Instance?.HasFlag(HairpinFlag) ?? false;

        if (_drawer == DrawerState.Asking && ChoiceYesRect.HasPoint(position))
        {
            GetTree().Root.SetInputAsHandled();
            _ = ChooseOpenDrawerAsync();
            return;
        }
        if (_drawer == DrawerState.Asking && ChoiceNoRect.HasPoint(position))
        {
            GetTree().Root.SetInputAsHandled();
            _ = ChooseLeaveDrawerAsync();
            return;
        }
        if (_drawer != DrawerState.Asking && DrawerLeaveRect.HasPoint(position))
        {
            GetTree().Root.SetInputAsHandled();
            _ = ExitDrawerCloseupAsync();
            return;
        }
        if (_drawer == DrawerState.Closed && KnobRect.HasPoint(position))
        {
            GetTree().Root.SetInputAsHandled();
            _ = AskAboutDrawerAsync();
            return;
        }
        if (_drawer == DrawerState.Open && !hairpinTaken &&
            CloseupHairpinRect.HasPoint(position))
        {
            GetTree().Root.SetInputAsHandled();
            _ = TakeHairpinFromDrawerAsync();
            return;
        }

        // Anywhere else: a knuckle against the drawer front. The
        // click is answered here, never dropped through to the room.
        GetTree().Root.SetInputAsHandled();
        AudioManager.Play(Sfx.KnockWood, -15f, 1.15f);
    }

    private async Task AskAboutDrawerAsync()
    {
        if (_drawer != DrawerState.Closed || _drawerBusy)
            return;
        _drawerBusy = true;
        Dialogue.BeginInputBlock();
        try
        {
            AudioManager.Play(Sfx.KnockWood, -12f, 1.1f);
            await Say("The knob is worn smooth, the way a secret's handle gets. It would open easy. It always opened easy. The hard part was never the opening.");
            if (!StillHere())
                return;
            await Say("…Well. Am I opening it, or am I walking past my own drawer forever?");
            if (!StillHere())
                return;
            _drawer = DrawerState.Asking;
            RefreshDrawerLabels();
            QueueRedraw();
        }
        finally
        {
            Dialogue.EndInputBlock();
            _drawerBusy = false;
        }
    }

    private async Task ChooseOpenDrawerAsync()
    {
        if (_drawer != DrawerState.Asking || _drawerBusy)
            return;
        _drawerBusy = true;
        Dialogue.BeginInputBlock();
        try
        {
            AudioManager.Play(Sfx.UiClick, -9f);
            GameState.Instance?.SetFlag(DrawerOpenedFlag);
            _drawer = DrawerState.Open;
            RefreshDrawerLabels();
            QueueRedraw();
            AudioManager.Play(Sfx.DrawerOpen, -7f, 0.9f);

            bool hairpinTaken = GameState.Instance?.HasFlag(HairpinFlag) ?? false;
            await Say(hairpinTaken
                ? "It sticks, then gives. Buttons. A ribbon gone grey. Everything else it was keeping, I've already taken with me."
                : "It sticks, then gives. Buttons. A ribbon gone grey. And — oh. One of Mum's hairpins, lying where the dark keeps its thin, useful things. Bent, the way hers always ended up bent.");
            if (!StillHere())
                return;
            if (!hairpinTaken)
                GameState.Instance?.Toast(
                    "The hairpin is in the drawer — click it to take it, or step back and leave it sleeping. It will keep.");
        }
        finally
        {
            Dialogue.EndInputBlock();
            _drawerBusy = false;
        }
    }

    private async Task ChooseLeaveDrawerAsync()
    {
        if (_drawer != DrawerState.Asking || _drawerBusy)
            return;
        _drawerBusy = true;
        Dialogue.BeginInputBlock();
        try
        {
            AudioManager.Play(Sfx.UiClick, -11f, 0.85f);
            _drawer = DrawerState.Closed;
            RefreshDrawerLabels();
            QueueRedraw();
            await Say("No. Not everything I hid needs looking at today. The drawer can keep its dark a while longer. It's good at it. It's had practice.");
        }
        finally
        {
            Dialogue.EndInputBlock();
            _drawerBusy = false;
        }
    }

    private async Task TakeHairpinFromDrawerAsync()
    {
        if (_drawer != DrawerState.Open || _drawerBusy)
            return;
        _drawerBusy = true;
        Dialogue.BeginInputBlock();
        try
        {
            // The taking itself — flag, clue, toast — is unchanged.
            // Because the flag lands mid-sequence, the pin is gone
            // from the drawer the moment it is in the bag.
            await TakeHairpinAsync();
            if (!StillHere())
                return;
            RefreshDrawerLabels();
            QueueRedraw();
        }
        finally
        {
            Dialogue.EndInputBlock();
            _drawerBusy = false;
        }
    }

    private async Task ExitDrawerCloseupAsync()
    {
        if (_drawer == DrawerState.Room || _drawerBusy)
            return;
        _drawerBusy = true;
        try
        {
            AudioManager.Play(Sfx.UiClick, -11f);
            ClearDrawerCursor();
            _drawer = DrawerState.Room;
            RefreshDrawerLabels();
            Busy = false;
            QueueRedraw();

            // The tutorial's completion beat rides the step back, so
            // the door's waking-up is never talked over by the drawer.
            if (_drawerCompletedPending)
            {
                _drawerCompletedPending = false;
                Busy = true;
                try
                {
                    await OnRequiredExaminesCompletedAsync();
                }
                finally
                {
                    Busy = false;
                }
            }
        }
        finally
        {
            _drawerBusy = false;
        }
    }

    private void DrawDrawerCloseup()
    {
        var screen = new Rect2(Vector2.Zero, new Vector2(1152, 648));
        DrawRect(screen, new Color(0.16f, 0.10f, 0.07f, 0.92f));

        bool hairpinTaken = GameState.Instance?.HasFlag(HairpinFlag) ?? false;
        var wood = new Color(0.48f, 0.32f, 0.20f);
        var woodDark = new Color(0.33f, 0.20f, 0.12f);
        var seam = new Color(0.19f, 0.11f, 0.06f);
        var brass = new Color(0.83f, 0.66f, 0.34f);

        // The nightstand body around its one little drawer.
        DrawRect(new Rect2(286, 128, 580, 380), woodDark);
        DrawRect(new Rect2(286, 128, 580, 380), seam, false, 4f);

        if (_drawer == DrawerState.Open)
        {
            // The drawer pulled toward her — an open box, looked into.
            DrawRect(new Rect2(356, 198, 440, 240), new Color(0.55f, 0.38f, 0.23f));
            DrawRect(new Rect2(372, 214, 408, 208), new Color(0.30f, 0.19f, 0.11f));
            DrawRect(new Rect2(388, 230, 376, 176), new Color(0.62f, 0.45f, 0.28f));
            // The drawer's lesser dark: buttons, a ribbon gone grey.
            DrawCircle(new Vector2(430, 270), 10f, new Color(0.75f, 0.68f, 0.55f));
            DrawCircle(new Vector2(716, 258), 8f, new Color(0.68f, 0.60f, 0.48f));
            DrawCircle(new Vector2(694, 386), 9f, new Color(0.58f, 0.52f, 0.44f));
            DrawRect(new Rect2(416, 356, 86, 14), new Color(0.55f, 0.53f, 0.50f));
            if (!hairpinTaken && _hairpinTexture != null)
            {
                float pulse = Settings.ReduceMotionOrDefault()
                    ? 0.72f
                    : 0.6f + 0.22f * (float)System.Math.Sin(Time.GetTicksMsec() / 240.0);
                DrawArc(CloseupHairpinRect.GetCenter(), 88f, 0f, Mathf.Tau, 44,
                    new Color(1f, 0.85f, 0.5f, pulse * 0.6f), 2.5f, true);
                DrawTextureRect(_hairpinTexture, CloseupHairpinRect, false);
            }
            // The drawer front, dropped toward her knees.
            DrawRect(new Rect2(326, 438, 500, 34), wood);
            DrawRect(new Rect2(326, 438, 500, 34), seam, false, 3f);
            DrawCircle(new Vector2(576, 455), 11f, brass);
        }
        else
        {
            DrawRect(DrawerFrontRect, wood);
            DrawRect(new Rect2(DrawerFrontRect.Position + new Vector2(18, 18),
                DrawerFrontRect.Size - new Vector2(36, 36)), seam, false, 3f);
            DrawRect(DrawerFrontRect, seam, false, 4f);
            for (int i = 0; i < 4; i++)
            {
                float y = 208f + i * 66f;
                DrawLine(new Vector2(352, y), new Vector2(800, y + 8f),
                    new Color(0.40f, 0.26f, 0.16f, 0.55f), 3f);
            }
            // The knob, worn smooth.
            Vector2 knob = KnobRect.GetCenter();
            DrawCircle(knob, 17f, new Color(0.15f, 0.09f, 0.05f, 0.5f));
            DrawCircle(knob + new Vector2(-2, -3), 15f, brass);
            DrawCircle(knob + new Vector2(-6, -8), 5f, new Color(0.95f, 0.82f, 0.52f));
            float alpha = Settings.ReduceMotionOrDefault()
                ? 0.55f
                : 0.45f + 0.18f * (float)System.Math.Sin(Time.GetTicksMsec() / 300.0);
            DrawArc(knob, 30f, 0f, Mathf.Tau, 36,
                new Color(1f, 0.87f, 0.55f, alpha), 2.5f, true);
        }

        if (_drawer == DrawerState.Asking)
        {
            DrawDrawerPlate(ChoiceYesRect);
            DrawDrawerPlate(ChoiceNoRect);
        }
        else
        {
            DrawDrawerPlate(DrawerLeaveRect);
        }
    }

    private void DrawDrawerPlate(Rect2 rect)
    {
        bool hover = !_drawerBusy && rect.HasPoint(GetGlobalMousePosition());
        DrawRect(rect, new Color(0.05f, 0.04f, 0.03f, hover ? 0.94f : 0.82f));
        DrawRect(rect, new Color(0.82f, 0.68f, 0.45f, hover ? 1f : 0.55f), false, hover ? 2.5f : 1.5f);
    }

    private void BuildDrawerLabels()
    {
        _drawerPromptLabel = MakeDrawerLabel(string.Empty, new Rect2(236, 560, 680, 38), 21);
        _drawerYesLabel = MakeDrawerLabel("Open it.", ChoiceYesRect, 24);
        _drawerNoLabel = MakeDrawerLabel("Not yet.", ChoiceNoRect, 24);
        _drawerLeaveLabel = MakeDrawerLabel("← Step back", DrawerLeaveRect, 22);
        RefreshDrawerLabels();
    }

    private Label MakeDrawerLabel(string text, Rect2 rect, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            Position = rect.Position,
            Size = rect.Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 2400,
        };
        if (_drawerFont != null)
            label.AddThemeFontOverride("font", _drawerFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.97f, 0.93f, 0.84f));
        AddChild(label);
        return label;
    }

    private void RefreshDrawerLabels()
    {
        bool hairpinTaken = GameState.Instance?.HasFlag(HairpinFlag) ?? false;
        if (_drawerPromptLabel != null)
        {
            _drawerPromptLabel.Text = _drawer switch
            {
                DrawerState.Closed => "The little drawer. Her hand is already on the knob — click it.",
                DrawerState.Open when !hairpinTaken => "Take the hairpin, or step back and let it sleep a while longer.",
                DrawerState.Open => "Buttons. A ribbon gone grey. The bag has the rest of it.",
                _ => string.Empty,
            };
            _drawerPromptLabel.Visible = _drawer == DrawerState.Closed ||
                _drawer == DrawerState.Open;
        }
        if (_drawerYesLabel != null)
            _drawerYesLabel.Visible = _drawer == DrawerState.Asking;
        if (_drawerNoLabel != null)
            _drawerNoLabel.Visible = _drawer == DrawerState.Asking;
        if (_drawerLeaveLabel != null)
            _drawerLeaveLabel.Visible = _drawer == DrawerState.Closed ||
                _drawer == DrawerState.Open;
    }

    private void UpdateDrawerCursor()
    {
        if (_drawerBusy || GetTree().Paused || Dialogue.IsShowing ||
            SceneManager.IsChanging)
        {
            ClearDrawerCursor();
            return;
        }
        Vector2 position = GetGlobalMousePosition();
        bool hairpinTaken = GameState.Instance?.HasFlag(HairpinFlag) ?? false;
        bool over = _drawer switch
        {
            DrawerState.Closed => KnobRect.HasPoint(position) ||
                DrawerLeaveRect.HasPoint(position),
            DrawerState.Asking => ChoiceYesRect.HasPoint(position) ||
                ChoiceNoRect.HasPoint(position),
            DrawerState.Open => DrawerLeaveRect.HasPoint(position) ||
                (!hairpinTaken && CloseupHairpinRect.HasPoint(position)),
            _ => false,
        };
        if (over && !_drawerCursorOwned)
        {
            CursorHelper.BeginHover(this, PointerCursorPath, 32, 32);
            _drawerCursorOwned = true;
        }
        else if (!over && _drawerCursorOwned)
        {
            ClearDrawerCursor();
        }
    }

    private void ClearDrawerCursor()
    {
        if (_drawerCursorOwned)
        {
            CursorHelper.EndHover(this);
            _drawerCursorOwned = false;
        }
    }

    // ------------------------------------------------------------ waking

    /// <summary>First person, waking: black, a few blinks, a known ceiling,
    /// then the slow tilt of a head taking in a room it outgrew.</summary>
    private async Task PlayWakingAsync()
    {
        Busy = true;
        try
        {
            // The original memory intro has first claim on a New Game. It
            // consumes CutscenePending and pauses the tree; Mara's eyelids
            // wait until both the pending flag and that pause are gone.
            while (GameState.Instance?.CutscenePending == true || GetTree().Paused)
            {
                await WaitSeconds(0.1f);
                if (!StillHere())
                    return;
            }

            BuildWakingOverlay();

            if (Settings.ReduceMotionOrDefault())
            {
                await BlinkTo(1f, 0.9f);
            }
            else
            {
                await WaitSeconds(0.7f);
                if (!StillHere())
                    return;
                await BlinkTo(0.25f, 0.28f);
                await WaitSeconds(0.18f);
                await BlinkTo(0.02f, 0.22f);
                await WaitSeconds(0.25f);
                await BlinkTo(0.55f, 0.3f);
                await WaitSeconds(0.2f);
                await BlinkTo(0.1f, 0.2f);
                await WaitSeconds(0.3f);
                if (!StillHere())
                    return;
                await BlinkTo(1f, 0.45f);
            }

            if (!StillHere())
                return;
            await Say("…Ceiling. Okay. Ceiling's where I left it.", 3.6f, 26);
            if (!StillHere())
                return;
            await Say("Wait. That crack — I know that crack. It wanders like a river. I used to follow it with my eyes when I couldn't sleep.", 5.5f, 24);
            if (!StillHere())
                return;

            // Sitting up: the ceiling slides away upward (with a small
            // sideways drift, the way a head turns), and the room is there.
            AudioManager.Play(Sfx.PhotoPickup, -16f, 0.8f);
            var tilt = CreateTween();
            tilt.TweenProperty(_ceiling, "position", new Vector2(-46f, -648f),
                    Settings.ReduceMotionOrDefault() ? 0.4f : 2.3f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            await ToSignal(tilt, Tween.SignalName.Finished);
            if (!StillHere())
                return;

            await Say("My room. My old room. The bed, the window, the wardrobe — everything small. Everything where it was.", 5.5f, 24);
            if (!StillHere())
                return;
            await Say("I rent one room in the city now, with a ceiling I've never looked at once. So this is a dream, or it's… Breathe, Mara. Look around first. Answers after.", 6f, 24);
            if (!StillHere())
                return;
            await Say("Start where I am. Bed, window, clock, drawing, wardrobe — look at all of it properly. Then the door.", 5.5f, 24);
            if (!StillHere())
                return;

            GameState.Instance?.SetFlag(AwakeFlag);
            GameState.Instance?.Toast("Click each object to examine it. Hold Tab to circle the door; the house will let you through when it is ready.");
            GameUI.Instance?.TryStartPrologueTutorial();
        }
        finally
        {
            FreeWakingOverlay();
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private async Task PlayWakingAfterOpeningAsync()
    {
        // _Ready runs before SceneManager raises SceneChanged. Yield once so
        // the New Game screening can start, then let it finish before waking.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        while (StillHere() && GameUI.Instance?.IsOpeningCutscenePlaying == true)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (!StillHere() || (GameState.Instance?.HasFlag(AwakeFlag) ?? false))
            return;
        await PlayWakingAsync();
    }

    private bool StillHere() => GodotObject.IsInstanceValid(this) && IsInsideTree();

    private void BuildWakingOverlay()
    {
        _ceiling = new TextureRect
        {
            Texture = GD.Load<Texture2D>(CeilingPath),
            Position = Vector2.Zero,
            Size = new Vector2(1152f, 648f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4000,
        };
        AddChild(_ceiling);

        _lidTop = new ColorRect
        {
            Color = Colors.Black,
            Position = Vector2.Zero,
            Size = new Vector2(1152f, 324f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4001,
        };
        AddChild(_lidTop);

        _lidBottom = new ColorRect
        {
            Color = Colors.Black,
            Position = new Vector2(0f, 324f),
            Size = new Vector2(1152f, 324f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 4001,
        };
        AddChild(_lidBottom);
    }

    /// <summary>Open (or close) the eyes to a fraction of the screen height:
    /// 0 = shut, 1 = fully open. The ceiling waits behind the lids.</summary>
    private async Task BlinkTo(float openFraction, float seconds)
    {
        if (!StillHere() || _lidTop == null || _lidBottom == null)
            return;
        float lid = (1f - Mathf.Clamp(openFraction, 0f, 1f)) * 324f;
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_lidTop, "size:y", lid, seconds);
        tween.TweenProperty(_lidBottom, "size:y", lid, seconds);
        tween.TweenProperty(_lidBottom, "position:y", 648f - lid, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private void FreeWakingOverlay()
    {
        if (_ceiling != null && GodotObject.IsInstanceValid(_ceiling))
            _ceiling.QueueFree();
        if (_lidTop != null && GodotObject.IsInstanceValid(_lidTop))
            _lidTop.QueueFree();
        if (_lidBottom != null && GodotObject.IsInstanceValid(_lidBottom))
            _lidBottom.QueueFree();
        _ceiling = null;
        _lidTop = null;
        _lidBottom = null;
    }

    // --------------------------------------------------------------- door

    private async Task LeaveThroughDoorAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            if (!AllRequiredExamined())
            {
                RoomExamineTracker.GetRoomProgress(RoomName, out int done, out int total, out var remaining);
                AudioManager.Play(Sfx.LockRattle, -8f, 0.9f);
                await Say("No. Not before I've even looked at where I woke up. If I start by running from one room, I'll run from all of them.", 5.5f, 24);
                if (!StillHere())
                    return;
                await Say($"Still waiting for an honest look: {string.Join(", ", remaining.Select(BedroomObjectName))}. ({done}/{total} explored.)", 4.5f, 23);
                GameState.Instance?.Toast("Explore every bedroom object first — hold Tab to reveal what still waits.");
                return;
            }

            await Say("Hallway, then. Stairs down. Kitchen. Mum at the stove saying I'd sleep through my own funeral—", 5f, 24);
            if (!StillHere())
                return;

            // The kitchen-confusion beat is GameUI's, on arrival; this flag
            // both arms it and marks the bedroom done (RoomFullyDone).
            GameState.Instance?.SetFlag(FromBedroomFlag);
            AudioManager.Play(Sfx.DoorCreak, -7f, 0.9f);
            await WaitSeconds(0.3f);
            SceneManager.ChangeScene(KitchenScenePath);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(this))
                Busy = false;
        }
    }

    private static string BedroomObjectName(string id) => id switch
    {
        "Bed" => "the bed",
        "Window" => "the window",
        "Nightstand" => "the nightstand clock",
        "Drawing" => "the drawing",
        "Wardrobe" => "the wardrobe",
        _ => "the " + (id ?? "room").ToLowerInvariant(),
    };
}
