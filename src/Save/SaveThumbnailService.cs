using System;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Save;

/// <summary>
/// The slot browser's thumbnails, taken at the right moment and encoded off the main thread.
///
/// <b>Which frame.</b> A save made during play photographs the frame on screen, which is the game.
/// A save made from anywhere else (the pause menu, the map, a loading screen) would photograph that
/// screen instead, so the frame is taken <em>earlier</em>: <see cref="CacheFrame"/> is called the
/// moment play is left, when the viewport still holds the last frame of gameplay because the menu
/// has not been drawn yet, and every save until play resumes reuses that picture.
/// <see cref="AutosaveService"/> drives the cache from <c>GameStateChangedEvent</c>.
///
/// <b>Never a stale picture.</b> When there is no clean frame to use (a headless run, a save with
/// no cached frame, an encode or write that fails) the slot's existing PNG is <em>removed</em>: a
/// missing thumbnail reads as "no picture", an older save's thumbnail reads as a lie.
///
/// <b>What stays on the main thread</b> is the one thing that has to: reading the viewport back
/// from the GPU. The resize to <see cref="Width"/> x <see cref="Height"/>, the PNG encode and the
/// atomic file write run on <see cref="SaveWriteQueue"/>, behind the save they belong to.
/// </summary>
public static class SaveThumbnailService
{
    public const int Width = 320;
    public const int Height = 180;

    private static Image? _cached;

    /// <summary>Whether a frame is being held for saves made outside play.</summary>
    public static bool HasCachedFrame => _cached != null;

    /// <summary>
    /// Remembers the frame currently in <paramref name="viewport"/> as the picture for saves made
    /// until <see cref="DropCache"/>. Call it as play is left, before the next frame is drawn.
    /// </summary>
    public static void CacheFrame(Viewport? viewport)
    {
        Image? frame = Grab(viewport);
        _cached = frame;
        if (frame != null)
        {
            // Shrink it now, off-thread: a held full-resolution frame is megabytes, the thumbnail
            // is not, and every later job that touches this image is queued behind this one.
            SaveWriteQueue.Enqueue(() => Shrink(frame));
        }
    }

    /// <summary>Forgets the held frame. Called when play resumes: from then on the live frame is the
    /// right picture again.</summary>
    public static void DropCache() => _cached = null;

    /// <summary>
    /// Writes the thumbnail for a save to <paramref name="pngPath"/> (a <c>user://</c> or absolute
    /// path), or removes the PNG already there when no clean frame exists. Best effort: a missing
    /// thumbnail never fails a save. <paramref name="onDone"/> is called on the main thread with
    /// whether a thumbnail is now on disk.
    /// </summary>
    public static void Write(string pngPath, Viewport? viewport, Action<bool>? onDone = null)
    {
        string path = ProjectSettings.GlobalizePath(pngPath);

        // During play the screen is the game. Anywhere else it is a menu or a loading screen, and
        // the frame cached on the way out of play is the one to use.
        Image? frame = GameManager.Instance is { IsPlaying: true } ? Grab(viewport) : _cached;
        if (frame == null)
        {
            SaveWriteQueue.Enqueue(() => SaveFiles.TryDelete(path), _ => onDone?.Invoke(false));
            return;
        }

        string error = string.Empty;
        SaveWriteQueue.Enqueue(
            () =>
            {
                try
                {
                    Shrink(frame);
                    byte[] png = frame.SavePngToBuffer();
                    if (png.Length > 0 && SaveFiles.WriteAtomic(path, png, out error))
                    {
                        return true;
                    }

                    if (png.Length == 0)
                    {
                        error = "the frame could not be encoded";
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                // No new picture: make sure the previous save's is not left standing in for it.
                SaveFiles.TryDelete(path);
                return false;
            },
            written =>
            {
                if (!written)
                {
                    Log.Warn($"No thumbnail for '{pngPath}' ({error}); any older one was removed.");
                }

                onDone?.Invoke(written);
            });
    }

    private static Image? Grab(Viewport? viewport)
    {
        // The dummy renderer has no texture, and asking it for one prints a native error.
        if (viewport == null || DisplayServer.GetName() == "headless")
        {
            return null;
        }

        try
        {
            Image? image = viewport.GetTexture()?.GetImage();
            return image == null || image.IsEmpty() ? null : image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the viewport for a save thumbnail: {ex.Message}");
            return null;
        }
    }

    private static bool Shrink(Image frame)
    {
        if (frame.GetWidth() != Width || frame.GetHeight() != Height)
        {
            frame.Resize(Width, Height, Image.Interpolation.Bilinear);
        }

        return true;
    }
}
