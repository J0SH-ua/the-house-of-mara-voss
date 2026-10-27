using Godot;

/// <summary>
/// The sitting-room photograph, held up to the light.
///
/// A plain paper border, the room as it was kept on film — and the
/// four small truths drawn from the very same hand as the crumbling
/// investigation (see SittingRoomDetails), so the photograph can
/// never lie differently than the room does. The mouse wheel leans
/// closer, a drag moves over the print, a double-click puts it back
/// at arm's length. Owned by GameUI's photo panel; also summoned
/// once, unasked, the moment Mara turns the photograph over.
/// </summary>
public partial class PhotoCanvas : Control
{
    private const string FontPath = "res://assets/fonts/CormorantGaramond-VariableFont_wght.ttf";
    private const float DesignW = 1152f;
    private const float DesignH = 648f;
    private const float MinZoom = 1f;
    private const float MaxZoom = 3.2f;

    private Texture2D _texture;
    private FontFile _font;
    private float _zoom = 1f;
    private Vector2 _center = new(576, 324);
    private bool _dragging;
    private Vector2 _dragLast;

    public override void _Ready()
    {
        _texture = GD.Load<Texture2D>(SittingRoomDetails.BackgroundPath);
        _font = GD.Load<FontFile>(FontPath);
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        MouseFilter = Control.MouseFilterEnum.Stop;
    }

    public void ResetView()
    {
        _zoom = 1f;
        _center = new Vector2(576, 324);
        _dragging = false;
        QueueRedraw();
    }

    private Rect2 FrameRect()
    {
        float availW = Mathf.Max(200f, Size.X - 140f);
        float availH = Mathf.Max(160f, Size.Y - 190f);
        float scale = Mathf.Min(availW / DesignW, availH / DesignH);
        var size = new Vector2(DesignW * scale, DesignH * scale);
        var pos = new Vector2((Size.X - size.X) * 0.5f, (Size.Y - 60f - size.Y) * 0.5f);
        return new Rect2(pos, size);
    }

    private Rect2 VisibleDesignRect()
    {
        var size = new Vector2(DesignW / _zoom, DesignH / _zoom);
        return new Rect2(_center - size * 0.5f, size);
    }

    private void ClampCenter()
    {
        var half = new Vector2(DesignW / _zoom, DesignH / _zoom) * 0.5f;
        _center = new Vector2(
            Mathf.Clamp(_center.X, half.X, DesignW - half.X),
            Mathf.Clamp(_center.Y, half.Y, DesignH - half.Y));
    }

    private Vector2 ScreenToDesign(Vector2 local)
    {
        var frame = FrameRect();
        var vis = VisibleDesignRect();
        return vis.Position + (local - frame.Position) / frame.Size * vis.Size;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                ZoomAround(mouse.Position, 1.18f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                ZoomAround(mouse.Position, 1f / 1.18f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.DoubleClick)
                {
                    ResetView();
                    AcceptEvent();
                }
                else if (mouse.Pressed)
                {
                    _dragging = true;
                    _dragLast = mouse.Position;
                    AcceptEvent();
                }
                else
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            var frame = FrameRect();
            var vis = VisibleDesignRect();
            float scale = frame.Size.X / Mathf.Max(1f, vis.Size.X);
            _center -= (motion.Position - _dragLast) / Mathf.Max(0.01f, scale);
            _dragLast = motion.Position;
            ClampCenter();
            QueueRedraw();
            AcceptEvent();
        }
    }

    private void ZoomAround(Vector2 local, float factor)
    {
        Vector2 before = ScreenToDesign(local);
        _zoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        ClampCenter();
        Vector2 after = ScreenToDesign(local);
        _center += before - after;
        ClampCenter();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var frame = FrameRect();
        var vis = VisibleDesignRect();

        if (_texture != null)
        {
            Vector2 texSize = _texture.GetSize();
            var src = new Rect2(
                vis.Position * texSize / new Vector2(DesignW, DesignH),
                vis.Size * texSize / new Vector2(DesignW, DesignH));
            DrawTextureRectRegion(_texture, frame, src);

            float scale = frame.Size.X / Mathf.Max(1f, vis.Size.X);
            DrawSetTransform(frame.Position - vis.Position * scale, 0f, Vector2.One * scale);
            SittingRoomDetails.DrawCorrectSet(this);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
        else
        {
            DrawRect(frame, new Color(0.1f, 0.085f, 0.06f), true);
        }

        // A matte of near-dark around the print — also the honest way
        // to keep zoomed details from painting outside their paper.
        var matte = new Color(0.012f, 0.01f, 0.008f, 0.92f);
        DrawRect(new Rect2(0, 0, Size.X, frame.Position.Y), matte, true);
        DrawRect(new Rect2(0, frame.End.Y, Size.X, Size.Y - frame.End.Y), matte, true);
        DrawRect(new Rect2(0, frame.Position.Y, frame.Position.X, frame.Size.Y), matte, true);
        DrawRect(new Rect2(frame.End.X, frame.Position.Y, Size.X - frame.End.X, frame.Size.Y), matte, true);

        // The paper border and its hairline.
        var paper = new Color(0.9f, 0.86f, 0.74f, 0.98f);
        const float b = 12f;
        DrawRect(new Rect2(frame.Position - new Vector2(b, b), new Vector2(frame.Size.X + b * 2, b)), paper, true);
        DrawRect(new Rect2(frame.Position + new Vector2(-b, frame.Size.Y), new Vector2(frame.Size.X + b * 2, b)), paper, true);
        DrawRect(new Rect2(frame.Position + new Vector2(-b, 0), new Vector2(b, frame.Size.Y)), paper, true);
        DrawRect(new Rect2(frame.Position + new Vector2(frame.Size.X, 0), new Vector2(b, frame.Size.Y)), paper, true);
        DrawRect(frame, new Color(0.16f, 0.12f, 0.08f, 0.9f), false, 2f);

        // Pencil on the back of the border's moment: whose room, when.
        if (_font != null)
        {
            DrawString(_font, new Vector2(frame.Position.X, frame.End.Y + 40f),
                "the sitting room — before", HorizontalAlignment.Center,
                frame.Size.X, 21, new Color(0.88f, 0.83f, 0.7f, 0.9f));
        }
    }
}
