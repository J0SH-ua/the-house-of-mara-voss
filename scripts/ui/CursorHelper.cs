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

    public static void ApplyCursor(string texturePath, int width = 0, int height = 0)
    {
        if (string.IsNullOrWhiteSpace(texturePath) || (Globals.Instance?.IS_CLARIFYING ?? false))
            return;

        string key = $"{texturePath}|{width}x{height}";
        if (!TextureCache.TryGetValue(key, out var texToUse))
        {
            var texture = ResourceLoader.Load<Texture2D>(texturePath);
            if (texture == null)
                return;

            texToUse = texture;
            if (width > 0 && height > 0)
            {
                var img = texture.GetImage();
                if (img != null)
                {
                    var dup = img.Duplicate() as Image;
                    if (dup != null)
                    {
                        dup.Resize(width, height);
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
