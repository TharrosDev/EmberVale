using System.Collections.Generic;
using Godot;

namespace Embervale.Core;

/// <summary>
/// The one way gameplay loads an authored C# <see cref="Resource"/> by path: loaded once, then held
/// for the life of the process.
///
/// ⚠️ <b>This is the fix for the <c>--lifecycle</c> FATAL (<c>gchandle.is_released()</c> in
/// <c>mono_object_disposed_baseref</c>, from <c>GodotObject.Finalize</c>).</b> A C#-scripted
/// Resource that only managed code references sits in Godot's <c>ResourceCache</c> behind a
/// <i>weak</i> GC handle. Drop the last managed reference and the GC collects the wrapper; the
/// native object stays in the cache until the finalizer thread gets round to it. If anything loads
/// that path in that window, the cache hands back the same native object, and Godot's
/// <c>CSharpInstance::refcount_incremented</c> finds the weak target dead, clears the instance's
/// handle and returns — so when the finalizer does run, it trips the crash condition. It is not a
/// race between two threads touching one object; it is a window, and the window opens every time a
/// session's actors, factories or map lookups let go of a curve, attribute set, weapon, loot table
/// or prepared region that the next load asks for again.
///
/// Holding the wrapper closes the window at its source: a reachable wrapper is never collected, so
/// a cache hit always finds it alive. The content databases already do this for everything under
/// <c>data/</c> they index (their dictionaries are process-lifetime statics); this covers the loads
/// that are not a database. The set is bounded by the number of distinct authored paths.
///
/// <c>ResidentResourceTests</c> fails any <c>GD.Load&lt;T&gt;</c> of a project Resource type outside
/// this class. It deliberately has no <c>Clear</c>: emptying it reopens the window.
/// </summary>
public static class ResidentResources
{
    private static readonly Dictionary<string, Resource> Loaded = new();
    private static readonly object Gate = new();

    /// <summary><c>GD.Load</c>, held for the process. A failed load is not remembered, so a file
    /// written later (a bake) is still found; like <c>GD.Load</c>, it can return null.</summary>
    public static T? Load<T>(string path) where T : Resource
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(path, out Resource? held))
            {
                return held as T;
            }

            T? resource = GD.Load<T>(path);
            if (resource != null)
            {
                Loaded[path] = resource;
            }

            return resource;
        }
    }
}
