using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Godot;

namespace Embervale.Save;

/// <summary>
/// Persists the <em>existence and placement</em> of spawned actors that must survive save/load
/// (named NPCs, world containers, placed props). The base <see cref="SaveManager"/> only restores
/// the component state of actors that are already alive; it cannot recreate one that no longer
/// exists in a freshly-loaded scene. This director closes that gap:
///
///   * actors are spawned through <see cref="Spawn"/>, which assigns a stable
///     <see cref="IEntity.PersistentId"/> and tracks them,
///   * <see cref="Save"/> writes a manifest (template id + persistent id + transform) of the live
///     tracked actors,
///   * <see cref="Load"/> reconciles: it despawns tracked actors absent from the save and recreates
///     missing ones via the <see cref="PersistentActorRegistry"/>. Each recreated actor's components
///     restore their own state through the <see cref="SaveManager"/>'s in-flight-load hook.
///
/// Ambient/transient actors (loot, wandering mobs) are intentionally NOT routed through here — they
/// stay session-only by design.
/// </summary>
[GlobalClass]
public partial class PersistentSpawnDirector : Node, ISaveable
{
    public string SaveId => "spawns";

    private readonly Dictionary<string, IEntity> _tracked = new();
    private int _autoId;

    /// <summary>Persistent ids of the actors this director currently tracks.</summary>
    public IReadOnlyCollection<string> TrackedIds => _tracked.Keys;

    public override void _EnterTree()
    {
        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// Spawns (or returns the existing) persistent actor of <paramref name="templateId"/> under this
    /// director's parent, assigning <paramref name="persistentId"/> (auto-generated when empty).
    /// </summary>
    public IEntity? Spawn(string templateId, string persistentId, Vector3 position, float yawDegrees = 0f)
    {
        if (string.IsNullOrEmpty(persistentId))
        {
            persistentId = $"{templateId}#{++_autoId}";
        }

        if (_tracked.TryGetValue(persistentId, out IEntity? existing) && IsInstanceValid((Node)existing.Body))
        {
            return existing;
        }

        Node3D? host = PersistentActorRegistry.Create(templateId, position);
        if (host is not IEntity entity)
        {
            if (host != null)
            {
                host.QueueFree();
                Log.Warn($"Persistent template '{templateId}' did not build an IEntity; discarded.");
            }

            return null;
        }

        AssignIdentity(host, templateId, persistentId);
        host.Position = World.WorldGround.Lift(position);
        host.RotationDegrees = new Vector3(host.RotationDegrees.X, yawDegrees, host.RotationDegrees.Z);

        GetParent().AddChild(host);
        _tracked[persistentId] = entity;

        // Only drop the key if THIS host is still the tracked one, so a reconcile that replaces an
        // actor isn't undone by the previous instance's deferred TreeExited.
        host.TreeExited += () =>
        {
            if (_tracked.TryGetValue(persistentId, out IEntity? current) && ReferenceEquals(current, entity))
            {
                _tracked.Remove(persistentId);
            }
        };

        return entity;
    }

    /// <summary>Frees a tracked actor. It will be recreated on the next load if the save contains it.</summary>
    public bool Despawn(string persistentId)
    {
        if (!_tracked.TryGetValue(persistentId, out IEntity? entity))
        {
            return false;
        }

        _tracked.Remove(persistentId);
        if (entity.Body is Node node && IsInstanceValid(node))
        {
            node.QueueFree();
        }

        return true;
    }

    public Godot.Collections.Dictionary Save()
    {
        var actors = new Godot.Collections.Array();
        foreach (IEntity entity in _tracked.Values)
        {
            if (entity.Body is not Node node || !IsInstanceValid(node))
            {
                continue;
            }

            Vector3 p = entity.Body.GlobalPosition;
            actors.Add(new Godot.Collections.Dictionary
            {
                ["pid"] = entity.PersistentId ?? string.Empty,
                ["tid"] = entity.TemplateId,
                ["x"] = p.X,
                ["y"] = p.Y,
                ["z"] = p.Z,
                ["yaw"] = entity.Body.RotationDegrees.Y,
            });
        }

        return new Godot.Collections.Dictionary { ["actors"] = actors };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        if (!data.TryGetValue("actors", out Variant actorsVariant) ||
            actorsVariant.VariantType != Variant.Type.Array)
        {
            return;
        }

        // Build the desired set from the save.
        var desired = new Dictionary<string, (string Template, Vector3 Pos, float Yaw)>();
        foreach (Variant element in actorsVariant.AsGodotArray())
        {
            if (element.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            var entry = element.AsGodotDictionary();
            string pid = entry.TryGetValue("pid", out Variant pidV) ? pidV.AsString() : string.Empty;
            string tid = entry.TryGetValue("tid", out Variant tidV) ? tidV.AsString() : string.Empty;
            if (string.IsNullOrEmpty(pid) || string.IsNullOrEmpty(tid))
            {
                continue;
            }

            var pos = new Vector3(
                entry.TryGetValue("x", out Variant x) ? x.AsSingle() : 0f,
                entry.TryGetValue("y", out Variant y) ? y.AsSingle() : 0f,
                entry.TryGetValue("z", out Variant z) ? z.AsSingle() : 0f);
            float yaw = entry.TryGetValue("yaw", out Variant yawV) ? yawV.AsSingle() : 0f;
            desired[pid] = (tid, pos, yaw);
        }

        // Despawn tracked actors that the save no longer contains (snapshot the keys first).
        foreach (string pid in new List<string>(_tracked.Keys))
        {
            if (!desired.ContainsKey(pid))
            {
                Despawn(pid);
            }
        }

        // Recreate missing actors / reposition surviving ones.
        foreach (KeyValuePair<string, (string Template, Vector3 Pos, float Yaw)> kv in desired)
        {
            if (_tracked.TryGetValue(kv.Key, out IEntity? live) && live.Body is Node node && IsInstanceValid(node))
            {
                // Lifted, never lowered: a saved position that predates a landform edit otherwise
                // rebuilds the actor inside the hillside (World.WorldGround.Lift).
                live.Body.GlobalPosition = World.WorldGround.Lift(kv.Value.Pos);
                live.Body.RotationDegrees = new Vector3(live.Body.RotationDegrees.X, kv.Value.Yaw, live.Body.RotationDegrees.Z);
            }
            else
            {
                Spawn(kv.Value.Template, kv.Key, kv.Value.Pos, kv.Value.Yaw);
            }
        }
    }

    private static void AssignIdentity(Node3D host, string templateId, string persistentId)
    {
        switch (host)
        {
            case Entity e:
                e.TemplateId = templateId;
                e.PersistentId = persistentId;
                break;
            case CharacterEntity c:
                c.TemplateId = templateId;
                c.PersistentId = persistentId;
                break;
        }
    }
}
