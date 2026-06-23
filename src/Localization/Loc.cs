using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Godot;

namespace Embervale.Localization;

/// <summary>
/// The localization facade (Phase 24G): the single entry point for player-facing text. Every UI and
/// dialogue string goes through <see cref="T"/> with a stable key from
/// <c>res://data/locale/strings.csv</c> — <b>no hard-coded display strings from here on</b>.
///
/// At boot <see cref="Initialize"/> loads the CSV catalogue, registers a Godot <c>Translation</c> per
/// locale with the engine's <c>TranslationServer</c>, and selects <see cref="DefaultLocale"/>. Lookups
/// delegate to the server, so a missing key returns the key itself (a visible, debuggable fallback)
/// and switching locale (<see cref="SetLocale"/>) re-points every resolved string. Loading the
/// catalogue at runtime (rather than via the editor's CSV import) keeps the repo buildable/playable
/// without an editor round-trip — the catalogue is plain data alongside the rest of <c>data/</c>.
/// </summary>
public static class Loc
{
    public const string DefaultLocale = "en";
    private const string CatalogPath = "res://data/locale/strings.csv";

    private static bool _initialized;

    /// <summary>Loads the catalogue and selects the default locale. Idempotent; safe to call once at boot.</summary>
    public static void Initialize(string catalogPath = CatalogPath)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        string csv = ReadCatalog(catalogPath);
        if (csv.Length == 0)
        {
            Log.Warn($"Localization catalogue '{catalogPath}' missing or empty; UI keys will show raw.");
            return;
        }

        Dictionary<string, Dictionary<string, string>> byLocale = LocCatalog.Parse(csv);
        int total = 0;
        foreach ((string locale, Dictionary<string, string> messages) in byLocale)
        {
            var translation = new Translation { Locale = locale };
            foreach ((string key, string value) in messages)
            {
                translation.AddMessage(key, value);
            }

            TranslationServer.AddTranslation(translation);
            total += messages.Count;
        }

        TranslationServer.SetLocale(DefaultLocale);
        Log.Info($"Localization: loaded {total} string(s) across {byLocale.Count} locale(s); locale '{DefaultLocale}'.");
    }

    /// <summary>Resolves a key to text in the active locale. An unknown key returns the key itself.</summary>
    public static string T(string key) => TranslationServer.Translate(key).ToString();

    /// <summary>Resolves a key then <c>string.Format</c>s it with <paramref name="args"/> (for strings
    /// with <c>{0}</c> placeholders, e.g. "Level {0}").</summary>
    public static string TF(string key, params object[] args) => string.Format(T(key), args);

    /// <summary>Switches the active locale (must have been loaded from the catalogue). Returns false
    /// if the locale is unknown.</summary>
    public static bool SetLocale(string locale)
    {
        if (System.Array.IndexOf(TranslationServer.GetLoadedLocales(), locale) < 0)
        {
            Log.Warn($"Locale '{locale}' is not loaded; keeping '{TranslationServer.GetLocale()}'.");
            return false;
        }

        TranslationServer.SetLocale(locale);
        return true;
    }

    private static string ReadCatalog(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            return string.Empty;
        }

        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file?.GetAsText() ?? string.Empty;
    }
}
