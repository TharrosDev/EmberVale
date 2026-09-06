using System.IO;
using Godot;

namespace Embervale.Core;

/// <summary>Automation uses per-run saves/settings; ordinary play keeps user:// unchanged.</summary>
public static class UserDataPaths
{
    public static string Resolve(string relative)
    {
#if EMBERVALE_TOOLING
        string directory = OS.GetEnvironment("EMBERVALE_USER_DIR");
        if (!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory))
        {
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, relative).Replace('\\', '/');
        }
#endif
        return "user://" + relative;
    }
}
