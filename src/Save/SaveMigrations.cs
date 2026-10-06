using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Embervale.Save;

/// <summary>
/// The world data the v2 -> v3 step needs, injected so the migration chain itself stays free of
/// the engine and of the content databases. <see cref="SaveManager"/> fills these from
/// <c>RegionDatabase</c> and <c>PropertyDatabase</c>; a test fills them with constants.
/// </summary>
public sealed class SaveMigrationLookups
{
    /// <summary>The Ember Crown's authored spawn point (X, Z), or null when the region is unknown
    /// (the start cache is then re-seated beside the world origin, as it always was).</summary>
    public Func<(double X, double Z)?> StartSpawn { get; init; } = static () => null;

    /// <summary>A property's current build-yard centre (X, Z) by property id, or null when no such
    /// property exists (its placed props are then left where the save has them).</summary>
    public Func<string, (double X, double Z)?> PropertyYard { get; init; } = static _ => null;
}

/// <summary>
/// The save format's migration chain, v1 -> v2 -> v3 -> v4, as pure code over a parsed JSON
/// document. Each step upgrades the envelope in place and stamps its new version; a save is walked
/// forward one step at a time until it is current. Nothing here touches Godot, so every step is
/// pinned by golden fixtures in xUnit (<c>tests/Embervale.Tests/Fixtures/saves</c>).
///
/// ⚠️ A step may only drop or rewrite what it documents. A player's progress is not a casualty of
/// a format change: anything a step does not name must come out exactly as it went in.
/// </summary>
public static class SaveMigrations
{
    /// <summary>The homestead build-yard centre every v2 save's placed props were written against,
    /// before the 2026-09 world rebuild moved the holding. History, not configuration.</summary>
    public const double V2HomesteadYardX = 95d;

    /// <inheritdoc cref="V2HomesteadYardX"/>
    public const double V2HomesteadYardZ = 90d;

    // Text is written as it is rather than escaped, so the engine's parser reads back exactly the
    // characters the save held.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] TransformKeys = { "player_x", "player_y", "player_z", "player_yaw" };

    /// <summary>
    /// Walks a whole save document forward to <see cref="SaveEnvelope.CurrentVersion"/> and returns
    /// it re-serialized, or null when the text is not a migratable envelope (validate it with
    /// <see cref="SaveEnvelope.Read"/> first). One line per step taken is added to
    /// <paramref name="notes"/> for the log.
    /// </summary>
    public static string? MigrateText(string json, SaveMigrationLookups lookups, List<string>? notes = null)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: SaveEnvelope.ParseOptions) is JsonObject root &&
                   Migrate(root, lookups, notes)
                ? root.ToJsonString(WriteOptions)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // Not JSON, or an object that repeats a key: neither is a document a step can walk.
            return null;
        }
    }

    /// <summary>
    /// Upgrades <paramref name="root"/> in place from whatever version it declares to the current
    /// one. False when it declares no integer version, a newer one, or one below the first format:
    /// an unmigratable save must fail loudly, never load in pieces.
    /// </summary>
    public static bool Migrate(JsonObject root, SaveMigrationLookups lookups, List<string>? notes = null)
    {
        if (Number(root[SaveEnvelope.VersionKey]) is not { } declared || declared != Math.Truncate(declared) ||
            declared < SaveEnvelope.FirstVersion || declared > SaveEnvelope.CurrentVersion)
        {
            return false;
        }

        int version = (int)declared;
        if (version == 1)
        {
            int cleared = MigrateV1ToV2(root);
            notes?.Add($"migrated v1 -> v2, discarding {cleared} pre-overhaul coordinate record(s). " +
                       "The player lands at the region's spawn point and fast-travel posts need " +
                       "re-attuning; nothing else was touched.");
            version = 2;
        }

        if (version == 2)
        {
            int changed = MigrateV2ToV3(root, lookups);
            notes?.Add($"migrated v2 -> v3 for the world rebuild, updating {changed} coordinate record(s). " +
                       "The player lands at the region's spawn point; progress is untouched.");
            version = 3;
        }

        if (version == 3)
        {
            int renamed = MigrateV3ToV4(root);
            notes?.Add($"migrated v3 -> v4, rewriting {renamed} set-piece key(s) from scene paths to stable ids.");
        }

        return true;
    }

    /// <summary>
    /// v1 -> v2: THE WORLD MOVED UNDER THE SAVE (the 2026-08-29 geography overhaul).
    ///
    /// Every world coordinate a v1 document holds was written against a lattice that no longer
    /// exists: the Ember Crown's cells all moved except the town hub, Frostfang Reach was lifted
    /// out of the Ember Crown's coordinate space entirely, and the ground stopped being flat, so
    /// even an unmoved X/Z can have eight metres of hillside over it. A saved position is therefore
    /// not merely stale: it can put the player inside terrain or in the void.
    ///
    /// Three things carry world coordinates that a player can be TELEPORTED to, and all three are
    /// discarded rather than guessed at:
    ///   the header transform  - dropped, so the load falls through to the region's own SpawnPoint,
    ///                           which is authored, on the ground, and always valid;
    ///   the fast-travel net   - dropped, because a jump to a v1 landing point is a jump into a hill
    ///                           (the posts themselves can be re-attuned by walking to them);
    ///   the map's saved pins  - dropped, because they are the positions of cells that have moved.
    ///                           Every one of them re-registers the moment its cell loads.
    ///
    /// ⚠️ EVERYTHING ELSE IS KEPT ON PURPOSE. Quests, flags, inventory, perks, reputation, the
    /// economy, blessings and companion rosters carry no coordinates. Persistent actor positions
    /// are kept too: losing a chest is worse than a chest sitting a metre low.
    ///
    /// ⚠️ "objects", not "state": the envelope key was always "objects", so until the 2026-09 world
    /// rebuild this step never actually discarded the records it documents discarding.
    /// </summary>
    /// <returns>How many records were discarded.</returns>
    public static int MigrateV1ToV2(JsonObject root)
    {
        int cleared = DropTransform(root);
        if (root[SaveEnvelope.ObjectsKey] is JsonObject objects)
        {
            cleared += objects.Remove("fasttravel") ? 1 : 0;
            cleared += objects.Remove("map") ? 1 : 0;
        }

        root[SaveEnvelope.VersionKey] = 2;
        return cleared;
    }

    /// <summary>
    /// v2 -> v3: THE WORLD REBUILD (2026-09). Every settlement moved and the realms grew roughly
    /// eight times in area, so a v2 world coordinate names a place that is now somewhere else.
    /// Progress is kept whole; only positions a player could be put back at are dealt with, and
    /// none is guessed at:
    ///   the header transform  - dropped; the player lands at the region's authored SpawnPoint;
    ///   the map               - saved footprints and the waypoint dropped; pin positions stay but
    ///                           are outranked by the bake's <c>WorldPlaceIndex</c>, as are travel landings;
    ///   the party             - every companion set to Follow, so the post-load catch-up brings
    ///                           them to the player instead of restoring them at a v2 point;
    ///   a live world event    - dropped (its origin is a v2 point; cooldowns are kept);
    ///   placed holding props  - moved by exactly the distance the build yard moved, so a player's
    ///                           furniture stays arranged on their own lawn;
    ///   the start cache       - re-seated beside the new spawn.
    /// Flags, quests, inventory, discovery and attunement carry no coordinates and are untouched.
    /// </summary>
    /// <returns>How many records were changed.</returns>
    public static int MigrateV2ToV3(JsonObject root, SaveMigrationLookups lookups)
    {
        int changed = DropTransform(root);
        root[SaveEnvelope.VersionKey] = 3;
        if (root[SaveEnvelope.ObjectsKey] is not JsonObject objects)
        {
            return changed;
        }

        if (objects["map"] is JsonObject map)
        {
            changed += map.Remove("footprints") ? 1 : 0;
            changed += map.Remove("waypoint") ? 1 : 0;
        }

        if (objects["world_events"] is JsonObject events)
        {
            changed += events.Remove("active") ? 1 : 0;
        }

        if (objects["companions"] is JsonObject companions && companions["party"] is JsonArray party)
        {
            foreach (JsonNode? member in party)
            {
                if (member is JsonObject companion)
                {
                    companion["stance"] = 0; // CompanionStance.Follow
                    changed++;
                }
            }
        }

        if (objects["spawns"] is not JsonObject spawns || spawns["actors"] is not JsonArray actors)
        {
            return changed;
        }

        (double X, double Z) spawn = lookups.StartSpawn() ?? (0d, 0d);
        foreach (JsonNode? element in actors)
        {
            if (element is not JsonObject actor)
            {
                continue;
            }

            string pid = Text(actor["pid"]);
            if (pid == "cache.world.start")
            {
                actor["x"] = spawn.X + 5d;
                actor["y"] = 0d;
                actor["z"] = spawn.Z - 5d;
                changed++;
                continue;
            }

            if (!pid.StartsWith("place.", StringComparison.Ordinal) ||
                lookups.PropertyYard(pid.Substring(6).Split('#')[0]) is not { } yard)
            {
                continue;
            }

            actor["x"] = (Number(actor["x"]) ?? 0d) + (yard.X - V2HomesteadYardX);
            actor["z"] = (Number(actor["z"]) ?? 0d) + (yard.Z - V2HomesteadYardZ);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// v3 -> v4: SET-PIECE KEYS STOP BEING SCENE PATHS. A set piece saved under
    /// <c>setpiece:res://scenes/regions/&lt;region&gt;/&lt;cell&gt;.tscn#&lt;node&gt;</c>, the one key in the
    /// format that named a file. It is rewritten to <c>setpiece:&lt;region&gt;/&lt;cell&gt;#&lt;node&gt;</c>
    /// (<see cref="SetPieceSaveIds"/>), both at the top level of <c>objects</c> and inside the
    /// <c>cell_persistence</c> ledger's <c>state</c> map, which is keyed by the same ids. The state
    /// under each key is untouched. Format 4 also adds the envelope checksum, which needs no step:
    /// an absent checksum is accepted and the next save writes one.
    /// </summary>
    /// <returns>How many keys were rewritten.</returns>
    public static int MigrateV3ToV4(JsonObject root)
    {
        root[SaveEnvelope.VersionKey] = 4;
        if (root[SaveEnvelope.ObjectsKey] is not JsonObject objects)
        {
            return 0;
        }

        int renamed = RenameSetPieces(objects);
        if (objects["cell_persistence"] is JsonObject ledger && ledger["state"] is JsonObject state)
        {
            renamed += RenameSetPieces(state);
        }

        return renamed;
    }

    private static int RenameSetPieces(JsonObject map)
    {
        var legacy = new List<string>();
        foreach (KeyValuePair<string, JsonNode?> entry in map)
        {
            if (SetPieceSaveIds.IsLegacy(entry.Key))
            {
                legacy.Add(entry.Key);
            }
        }

        foreach (string key in legacy)
        {
            JsonNode? state = map[key];
            map.Remove(key);

            // A document holding both forms of one piece was written by two builds; the stable
            // entry is the newer truth and wins.
            string stable = SetPieceSaveIds.Normalize(key);
            if (!map.ContainsKey(stable))
            {
                map[stable] = state;
            }
        }

        return legacy.Count;
    }

    private static int DropTransform(JsonObject root)
    {
        int dropped = 0;
        if (root[SaveEnvelope.HeaderKey] is JsonObject header)
        {
            foreach (string key in TransformKeys)
            {
                dropped += header.Remove(key) ? 1 : 0;
            }
        }

        return dropped;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text ?? string.Empty : string.Empty;

    // A parsed number is backed by its JSON token and answers as a double; one a step assigned is
    // backed by the CLR value it was given. Both are asked.
    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out double asDouble))
        {
            return double.IsFinite(asDouble) ? asDouble : null;
        }

        if (value.TryGetValue(out long asLong))
        {
            return asLong;
        }

        return value.TryGetValue(out int asInt) ? asInt : null;
    }
}
