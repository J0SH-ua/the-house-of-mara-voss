using Godot;
using System.Collections.Generic;

/// <summary>
/// Custom mouse cursors with ref-counted hover owners.
/// Resized cursor textures are cached — the previous version decoded,
/// duplicated and resized the source image on *every* hover-enter.
/// </summary>
public static class CursorHelper
{
    private static readonly HashSet<long> ActiveHoverOwners = new();
    private static readonly Dictionary<string, Texture2D> TextureCache = new();
    private static string activeTexturePath = string.Empty;
    private static int activeWidth;
    private static int activeHeight;

    /// <summary>No cursor in this house is bigger than a cursor. The
    /// authored cursor art is 97–314 px square (drawn big so it could be
    /// painted chunky), and SetCustomMouseCursor happily shows it at
    /// native size — a hand the size of Mara's head. Every cursor is
    /// therefore fitted into this box, requested size or not.</summary>
    private const int MaxCursorSize = 48;

    public static void ApplyCursor(string texturePath, int width = 0, int height = 0)
    {
        if (string.IsNullOrWhiteSpace(texturePath) || (Globals.Instance?.IS_CLARIFYING ?? false))
            return;

        if (width > MaxCursorSize) width = MaxCursorSize;
        if (height > MaxCursorSize) height = MaxCursorSize;

        string key = $"{texturePath}|{width}x{height}";
        if (!TextureCache.TryGetValue(key, out var texToUse))
        {
            var texture = ResourceLoader.Load<Texture2D>(texturePath);
            if (texture == null)
                return;

            texToUse = texture;
            Vector2 native = texture.GetSize();
            bool wantsResize = (width > 0 && height > 0) ||
                native.X > MaxCursorSize || native.Y > MaxCursorSize;
            if (wantsResize)
            {
                int targetW = width > 0 ? width
                    : Mathf.Max(1, Mathf.RoundToInt(MaxCursorSize * native.X / Mathf.Max(native.X, native.Y)));
                int targetH = height > 0 ? height
                    : Mathf.Max(1, Mathf.RoundToInt(MaxCursorSize * native.Y / Mathf.Max(native.X, native.Y)));

                // Imported textures may be VRAM-compressed, in which case
                // GetImage() returns null and the old code silently kept
                // the enormous original. Loading the file directly always
                // yields a readable image.
                var img = texture.GetImage() ?? Image.LoadFromFile(texturePath);
                if (img != null)
                {
                    var dup = img.Duplicate() as Image;
                    if (dup != null)
                    {
                        dup.Resize(targetW, targetH, Image.Interpolation.Nearest);
                        if (ImageTexture.CreateFromImage(dup) is Texture2D resized)
                            texToUse = resized;
                    }
                }
            }

            TextureCache[key] = texToUse;
        }

        Input.SetCustomMouseCursor(texToUse);
    }

    public static void BeginHover(GodotObject owner, string texturePath, int width = 0, int height = 0)
    {
        if (owner == null)
            return;

        long ownerId = (long)owner.GetInstanceId();
        bool firstOwner = ActiveHoverOwners.Count == 0;

        ActiveHoverOwners.Add(ownerId);

        // A Memory Verb owns the cursor until it is cancelled or spent;
        // ordinary examine hover must not replace its visual language.
        if ((GameState.Instance?.ActiveVerb ?? MemoryVerb.None) != MemoryVerb.None)
            return;

        if (firstOwner || activeTexturePath != texturePath || activeWidth != width || activeHeight != height)
        {
            activeTexturePath = texturePath;
            activeWidth = width;
            activeHeight = height;
            ApplyCursor(texturePath, width, height);
        }
    }

    public static void EndHover(GodotObject owner)
    {
        if (owner == null)
            return;

        long ownerId = (long)owner.GetInstanceId();
        ActiveHoverOwners.Remove(ownerId);

        if (ActiveHoverOwners.Count == 0)
            ResetCursor();
    }

    public static void ResetCursor()
    {
        // While Clarity is armed the eye cursor must survive hover churn;
        // ClaritySystem is responsible for clearing it on disarm/consume.
        if (Globals.Instance?.IS_CLARIFYING ?? false)
        {
            ActiveHoverOwners.Clear();
            return;
        }

        if ((GameState.Instance?.ActiveVerb ?? MemoryVerb.None) != MemoryVerb.None)
        {
            ActiveHoverOwners.Clear();
            return;
        }

        Input.SetCustomMouseCursor(null);

        ActiveHoverOwners.Clear();
        activeTexturePath = string.Empty;
        activeWidth = 0;
        activeHeight = 0;
    }
}
