using System;
using System.Collections.Generic;

namespace Embervale.Narrative;

/// <summary>Pure resolution of a story-card key prefix into its ordered card keys.</summary>
public static class StoryCards
{
    /// <summary>Hard cap so a catalogue that answers "exists" for everything cannot loop forever.</summary>
    public const int MaxCards = 24;

    /// <summary><c>prefix.1</c>, <c>prefix.2</c>, ... until <paramref name="keyExists"/> says a key is missing.</summary>
    public static string[] Keys(string prefix, Func<string, bool> keyExists)
    {
        var keys = new List<string>();
        if (string.IsNullOrEmpty(prefix))
        {
            return Array.Empty<string>();
        }

        for (int i = 1; i <= MaxCards; i++)
        {
            string key = $"{prefix}.{i}";
            if (!keyExists(key))
            {
                break;
            }

            keys.Add(key);
        }

        return keys.ToArray();
    }
}
