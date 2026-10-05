using System.Collections.Generic;
using Embervale.Appearance;
using Embervale.Core;
using Embervale.Localization;
using Embervale.Races;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// <c>--validate</c> arm for character appearance (P8): option ids, slots, Loc keys, a default per slot that
/// really is the unmodified look, build widths within the cap, every race allow-list pointing at real options,
/// every option offered by some race, and the shader and region mask present. A separate class like
/// <see cref="BackgroundValidator"/> so the shared <see cref="ContentValidator"/> takes one call line.
/// </summary>
public static class AppearanceValidator
{
    private const string Directory = "res://data/appearance";

    public static void Validate(List<string> issues)
    {
        if (!ResourceLoader.Exists(PlayerAppearance.ShaderPath))
        {
            issues.Add($"player body shader '{PlayerAppearance.ShaderPath}' is missing");
        }

        if (!ResourceLoader.Exists(PlayerAppearance.MaskPath))
        {
            issues.Add($"player region mask '{PlayerAppearance.MaskPath}' is missing (run tools/gen_player_mask.py)");
        }

        CheckDuplicateIds(issues);
        HashSet<string>? locKeys = LocaleKeys();

        var defaults = new int[AppearanceRules.SlotCount];
        foreach (AppearanceOptionResource option in AppearanceDatabase.All)
        {
            string id = option.Id;
            int slot = (int)option.Slot;
            if (slot < 0 || slot >= AppearanceRules.SlotCount)
            {
                issues.Add($"appearance '{id}' has an unknown slot {slot}");
                continue;
            }

            string expectedPrefix = $"{AppearanceRules.IdPrefix}{option.Slot.ToString().ToLowerInvariant()}.";
            if (!id.StartsWith(expectedPrefix, System.StringComparison.Ordinal) || id.Length == expectedPrefix.Length)
            {
                issues.Add($"appearance '{id}' must start with '{expectedPrefix}'");
            }

            if (option.NameKey.Length == 0)
            {
                issues.Add($"appearance '{id}' NameKey is empty");
            }
            else if (locKeys != null && !locKeys.Contains(option.NameKey))
            {
                issues.Add($"appearance '{id}' NameKey '{option.NameKey}' is not a key in data/locale/strings.csv");
            }

            if (option.Slot == AppearanceSlot.Build)
            {
                if (!AppearanceRules.BuildWithinCap(option.BuildScale))
                {
                    issues.Add($"appearance '{id}' build scale {option.BuildScale} is outside 1 +/- {AppearanceRules.MaxBuildDeviation}");
                }
            }
            else if (option.BuildScale != 1f)
            {
                issues.Add($"appearance '{id}' is not a Build option and must leave BuildScale at 1");
            }

            if (option.IsDefault)
            {
                defaults[slot]++;
                CheckDefaultIsIdentity(option, issues);
            }
        }

        for (int slot = 0; slot < defaults.Length; slot++)
        {
            if (defaults[slot] != 1)
            {
                issues.Add($"appearance slot {(AppearanceSlot)slot} must have exactly one default option, found {defaults[slot]}");
            }
        }

        ValidateRaces(issues);
    }

    private static void CheckDefaultIsIdentity(AppearanceOptionResource option, List<string> issues)
    {
        (float R, float G, float B)? reference = option.Slot switch
        {
            AppearanceSlot.Skin => AppearanceRules.SkinReference,
            AppearanceSlot.Hair => AppearanceRules.HairReference,
            AppearanceSlot.Eyes => AppearanceRules.EyeReference,
            AppearanceSlot.Ember => AppearanceRules.EmberReference,
            _ => null,
        };

        if (reference is { } expected && !AppearanceRules.MatchesReference((option.Tint.R, option.Tint.G, option.Tint.B), expected))
        {
            issues.Add($"appearance default '{option.Id}' tint is not the {option.Slot} reference colour, so it would change the unmodified look");
        }
        else if (option.Slot == AppearanceSlot.Build && option.BuildScale != 1f)
        {
            issues.Add($"appearance default '{option.Id}' must have build scale 1");
        }
    }

    private static void ValidateRaces(List<string> issues)
    {
        var offered = new HashSet<string>();
        foreach (RaceResource race in RaceDatabase.All)
        {
            var seen = new HashSet<string>();
            foreach (string id in race.AppearanceOptionIds)
            {
                if (AppearanceDatabase.Get(id) == null)
                {
                    issues.Add($"race '{race.Id}' offers unknown appearance '{id}'");
                }
                else if (!seen.Add(id))
                {
                    issues.Add($"race '{race.Id}' lists appearance '{id}' twice");
                }

                offered.Add(id);
            }
        }

        foreach (AppearanceOptionResource option in AppearanceDatabase.All)
        {
            if (!option.IsDefault && !offered.Contains(option.Id))
            {
                issues.Add($"appearance '{option.Id}' is offered by no race (unreachable)");
            }
        }
    }

    private static HashSet<string>? LocaleKeys()
    {
        const string path = "res://data/locale/strings.csv";
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }

        return LocCatalog.Parse(file.GetAsText()).TryGetValue(Loc.DefaultLocale, out Dictionary<string, string>? messages)
            ? new HashSet<string>(messages.Keys)
            : null;
    }

    /// <summary>The database keeps the last duplicate and hides the other, so scan the files themselves.</summary>
    private static void CheckDuplicateIds(List<string> issues)
    {
        if (!DirAccess.DirExistsAbsolute(Directory))
        {
            issues.Add($"appearance directory '{Directory}' is missing");
            return;
        }

        var seen = new Dictionary<string, string>();
        foreach (string file in DirAccess.GetFilesAt(Directory))
        {
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres") || ResidentResources.Load<AppearanceOptionResource>($"{Directory}/{name}") is not { } resource)
            {
                continue;
            }

            if (seen.TryGetValue(resource.Id, out string? first))
            {
                issues.Add($"appearance id '{resource.Id}' is duplicated (in {first} and {name})");
            }
            else
            {
                seen[resource.Id] = name;
            }
        }
    }
}
