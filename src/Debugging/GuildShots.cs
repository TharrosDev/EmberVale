using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Factions;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Live-world visual and behavioural proof for 42B — <c>godot --path . -- --guild-shots</c>.
///
/// It does three things the validator cannot. It <b>drives the real caller</b>: every officer's
/// conversation is opened through the <see cref="DialogueComponent"/> the player's <c>E</c> would
/// press, and the choices that come back are the ones a player would see. It proves the
/// <b>membership-aware greeting actually turns over</b> — the same officer, the same node, one set
/// of choices before joining and a different one after. And it proves that turn survives a
/// <b>wholesale load</b>, which is 42A's carry-forward: a load replays no events, so a surface that
/// only listens to <see cref="StoryFlagChangedEvent"/> would keep drawing the abandoned timeline.
///
/// Then it photographs each of the five hubs from its own approach and from behind, at eye level,
/// with the officers standing in it.
/// </summary>
public sealed partial class GuildShots : ShotHarness
{
    protected override string Flag => "--guild-shots";

    protected override string OutputDir => "user://guild_shots";

    /// <summary>The live dialogue panel, injected by the bootstrap the way <see cref="PanelShots"/>
    /// takes its screens. Needed to CLOSE a conversation between the two greeting shots.</summary>
    public UI.DialoguePanel? Dialogue { get; set; }

    /// <summary>Hub node name, the guild it belongs to, and the offset the approach comes from.
    /// ⚠️ The offsets are per hub rather than one mirrored pair: "the approach" is a different
    /// direction at each of the five, and a camera dropped on a fixed vector photographs the back of
    /// three of them while claiming to show the front.</summary>
    private static readonly (string Node, string Faction, Vector3 Front, Vector3 Back)[] Hubs =
    {
        // ⚠️ The distances are what a two-storey shell actually needs, not a tidy constant. At ten
        // metres a 6.24 m eaves fills the frame with one wall panel and the review says nothing: the
        // first pass photographed the Watch's plaster and no officers at all.
        ("WardensWatch", Core.GameIds.Factions.Dawnwardens,
            new Vector3(19f, 1.75f, 9f), new Vector3(-17f, 1.75f, -9f)),
        ("LedgerHouse", Core.GameIds.Factions.IronSyndicate,
            new Vector3(14f, 1.75f, 4f), new Vector3(-12f, 1.75f, -5f)),
        // ⚠️ The lodge's approach comes in from the south-WEST. Straight south puts the camera
        // behind a dead pine at (22, 4) whose trunk fills the frame — which is a photograph of a
        // tree, not a review of a lodge. A player walking up would step round it; so does this.
        ("DeadfallLodge", Core.GameIds.Factions.AshHunters,
            new Vector3(-5f, 1.75f, 15f), new Vector3(-4f, 1.75f, -14f)),
        // ⚠️ West of the annexe's own axis: due south of it is a market stall whose canopy frame
        // stands square in front of the door. The market is dense and the honest approach is the one
        // between the stalls, not the one through them.
        ("ArchiveAnnexe", Core.GameIds.Factions.VeiledArchive,
            new Vector3(-3f, 1.75f, -10f), new Vector3(-4f, 1.75f, 13f)),
        // ⚠️ The Undercroft is three and a half metres DOWN in the pit, so its camera stays down
        // there with it: the first pass stood ten metres out, which put the lens inside the pit rim
        // and photographed the inside of a hill. NOW.md invariant 6, from the other side.
        ("UndercroftPedestal", Core.GameIds.Factions.Emberbound,
            new Vector3(5.5f, 1.75f, 5f), new Vector3(-5f, 1.75f, -4.5f)),
    };

    protected override void BuildShotList()
    {
        // The behavioural half runs first and once, on the shot that opens the pass: the frames that
        // follow are then of a world whose membership state the harness has already put back.
        Shot("01-wardens-watch-front", () => Frame(0, front: true, exercise: true));
        Shot("02-wardens-watch-back", () => Frame(0, front: false));
        Shot("03-ledger-house-front", () => Frame(1, front: true));
        Shot("04-ledger-house-back", () => Frame(1, front: false));
        Shot("05-deadfall-lodge-front", () => Frame(2, front: true));
        Shot("06-deadfall-lodge-back", () => Frame(2, front: false));
        Shot("07-annexe-front", () => Frame(3, front: true));
        Shot("08-annexe-back", () => Frame(3, front: false));
        Shot("09-undercroft-front", () => Frame(4, front: true));
        Shot("10-undercroft-back", () => Frame(4, front: false));

        // ⚠️ THE TWO GREETING SHOTS COME LAST, AND THAT IS NOT A PREFERENCE. A conversation is a
        // MODAL panel: it fills the middle of the frame and dims the world behind it, so a hub
        // photographed with one open is a photograph of a dialogue box. The placement pass above
        // runs clean, and these two then open the SAME officer's SAME node twice — once as a
        // stranger, once as a member — which is the membership-aware greeting as a player sees it.
        Shot("11-greeting-stranger", () => Greet(member: false));
        Shot("12-greeting-member", () => Greet(member: true));
    }

    protected override string? ValidateShotState(string name)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out PlayerCharacter _))
        {
            return "no player in the world";
        }

        foreach ((string node, _, _, _) in Hubs)
        {
            if (Find(node) == null)
            {
                return $"hub node '{node}' is not in the tree";
            }
        }

        return null;
    }

    private void Frame(int index, bool front, bool exercise = false)
    {
        (string node, _, Vector3 ahead, Vector3 behind) = Hubs[index];
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerController>() is not { } controller ||
            controller.Camera is not { } camera ||
            Find(node) is not { } hub)
        {
            return;
        }

        if (exercise)
        {
            ExerciseEveryHub(player);
        }

        // ⚠️ THIRD PERSON, THEN FREEZE. The first-person arms and the held weapon are parented to
        // the camera, so a camera teleported to a hub carries a pair of hands and a sword blade into
        // the middle of every placement shot. Dropping to third person hides the viewmodel, and the
        // player's own body stays where the player is standing — a long way from the frame.
        controller.SetFirstPerson(false, immediate: true);

        // Freeze the player's rig only; the streamed world, the officers' animations and the weather
        // keep running under the held frame.
        controller.ProcessMode = ProcessModeEnum.Disabled;
        if (locator.TryGet(out WorldClock clock))
        {
            clock.SetTimeOfDay(11);
        }

        Vector3 offset = front ? ahead : behind;
        camera.GlobalPosition = OnGround(camera, hub.GlobalPosition + offset, offset.Y);
        camera.LookAt(hub.GlobalPosition + new Vector3(0f, 1.4f, 0f), Vector3.Up);
    }

    /// <summary>
    /// Drops a camera position onto the terrain under it and stands it back up at eye height.
    ///
    /// ⚠️ <b>An authored offset is a direction, not a height</b> (NOW.md invariant 6). The ground is
    /// a generated surface with real relief now, so "the hub's Y plus 1.75" is eye level only where
    /// the ground happens to be as high as the hub — and where it is higher, the lens ends up inside
    /// a hillside and the frame is a brown wall that a baseline will happily accept. `world_shots.gd`
    /// raycasts for exactly this reason; this is the same rule on the C# side.
    /// </summary>
    private static Vector3 OnGround(Node3D camera, Vector3 position, float eye)
    {
        if (camera.GetWorld3D() is not { } world || world.DirectSpaceState is not { } space)
        {
            return position;
        }

        var query = PhysicsRayQueryParameters3D.Create(
            position + new Vector3(0f, 40f, 0f), position - new Vector3(0f, 60f, 0f));
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || !hit.ContainsKey("position"))
        {
            return position;
        }

        Vector3 ground = hit["position"].AsVector3();
        return new Vector3(position.X, ground.Y + eye, position.Z);
    }

    /// <summary>
    /// Every officer of every guild, through the interactable the player presses — first as a
    /// stranger, then as a member, then across a wholesale load.
    /// </summary>
    private static void ExerciseEveryHub(IEntity player)
    {
        if (player.GetComponent<StoryFlagsComponent>() is not { } flags)
        {
            GD.PushError("--guild-shots: the player has no StoryFlagsComponent.");
            return;
        }

        Godot.Collections.Dictionary before = flags.Save();
        var failures = new List<string>();

        foreach (string guildId in Core.GameIds.Guilds.All)
        {
            if (FactionDatabase.Get(guildId) is not { } guild)
            {
                failures.Add($"{guildId}: no faction resource");
                continue;
            }

            foreach (string npcId in new[] { guild.LeaderNpcId, guild.QuartermasterNpcId, guild.ContactNpcId })
            {
                if (string.IsNullOrEmpty(npcId))
                {
                    continue;
                }

                if (OfficerOf(npcId) is not { } officer)
                {
                    failures.Add($"{npcId}: no placed entity carries that TemplateId");
                    continue;
                }

                if (officer.GetComponent<DialogueComponent>() is not { } talk ||
                    DialogueDatabase.Get(talk.DialogueId) is not { } dialogue)
                {
                    // A reused officer (the Syndicate's contact is Wren, who already had a service
                    // conversation) is a roster entry, not a 42B-authored greeting. Its presence is
                    // what matters here and the validator owns that.
                    continue;
                }

                // ⚠️ The caller is checked through its PROMPT rather than by pressing it here.
                // `Interact` opens a modal panel that would then sit over every frame in the pass;
                // an empty prompt is the same refusal the walk-up would give (`CanTalk`), and the
                // press itself is driven for real by the two greeting shots at the end.
                if (string.IsNullOrEmpty(talk.Prompt))
                {
                    failures.Add($"{npcId}: the interaction prompt is empty, so E would do nothing");
                    continue;
                }

                if (!HasGuildConditions(dialogue))
                {
                    // A REUSED officer. The Iron Syndicate's contact is Wren, who was already
                    // standing at the Crossway with a service conversation of her own; a roster
                    // entry is not a rewrite of the conversation someone already has. Her placement
                    // and her single ownership are the validator's business, and the membership
                    // greeting is asserted only on the fourteen this sub-phase authored.
                    continue;
                }

                flags.Clear(GuildRules.JoinedFlag(guildId));
                if (!OffersOnly(dialogue, player, member: false))
                {
                    failures.Add($"{npcId}: a stranger was not offered the stranger greeting");
                }

                flags.Set(GuildRules.JoinedFlag(guildId));
                if (!OffersOnly(dialogue, player, member: true))
                {
                    failures.Add($"{npcId}: a member was not offered the member greeting");
                }
            }
        }

        // ⚠️ THE WHOLESALE-LOAD HALF (42A's carry-forward). Membership is live and every officer is
        // greeting the player as one of their own; a load of a save taken BEFORE any of that has to
        // put every one of them back to the stranger line, and it replays no events to do it.
        flags.Load(before);
        foreach (string guildId in Core.GameIds.Guilds.All)
        {
            if (FactionDatabase.Get(guildId) is not { } guild ||
                OfficerOf(guild.LeaderNpcId) is not { } leader ||
                leader.GetComponent<DialogueComponent>() is not { } talk ||
                DialogueDatabase.Get(talk.DialogueId) is not { } dialogue)
            {
                continue;
            }

            if (!OffersOnly(dialogue, player, member: false))
            {
                failures.Add($"{guild.Id}: the leader still greeted a member after a wholesale load");
            }
        }

        if (failures.Count > 0)
        {
            GD.PushError("--guild-shots: " + string.Join("; ", failures));
            return;
        }

        GD.Print("--guild-shots: every placed officer answered as a stranger, then as a member, " +
                 "and every leader went back to the stranger greeting across a wholesale load.");
    }

    /// <summary>Opens the conversation the way the UI does and checks which of the two mutually
    /// exclusive greetings is on offer — exactly one, and the right one.</summary>
    private static bool OffersOnly(DialogueResource dialogue, IEntity player, bool member)
    {
        var session = new DialogueSession(dialogue, player);
        bool sawMember = false;
        bool sawStranger = false;
        foreach (DialogueChoice choice in session.VisibleChoices())
        {
            sawMember |= choice.Condition == DialogueCondition.GuildRankAtLeast;
            sawStranger |= choice.Condition == DialogueCondition.GuildNotMember;
        }

        return member ? sawMember && !sawStranger : sawStranger && !sawMember;
    }

    /// <summary>Whether this conversation is one of 42B's membership-aware officer graphs.</summary>
    private static bool HasGuildConditions(DialogueResource dialogue)
    {
        foreach (DialogueNode node in dialogue.NodeList())
        {
            foreach (DialogueChoice choice in node.ChoiceList())
            {
                if (choice.Condition is DialogueCondition.GuildRankAtLeast or DialogueCondition.GuildNotMember)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Opens the Dawnwarden captain's conversation through the interactable the player presses, as
    /// a stranger or as a member. ⚠️ The membership flag is set through
    /// <see cref="StoryFlagsComponent"/> — the same choke point a 42C join effect will use — and not
    /// by poking the dialogue: a greeting driven by anything other than the real flag would be a
    /// photograph of the harness rather than of the game.
    /// </summary>
    private void Greet(bool member)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<PlayerController>() is not { } controller ||
            controller.Camera is not { } camera ||
            FactionDatabase.Get(Core.GameIds.Factions.Dawnwardens) is not { } guild ||
            OfficerOf(guild.LeaderNpcId) is not { } captain ||
            captain.GetComponent<DialogueComponent>() is not { } talk ||
            player.GetComponent<StoryFlagsComponent>() is not { } flags)
        {
            return;
        }

        string joined = GuildRules.JoinedFlag(guild.Id);
        if (member)
        {
            flags.Set(joined);
        }
        else
        {
            flags.Clear(joined);
        }

        // ⚠️ END THE PREVIOUS CONVERSATION FIRST. A start published over a live session is ignored
        // by design, so without this the member shot photographs the stranger shot's still-open node
        // under the member shot's filename — which is the exact off-by-one ShotHarness's own header
        // was written about, and it produced a confident wrong frame on the first pass.
        Dialogue?.EndConversation();

        controller.SetFirstPerson(false, immediate: true);
        controller.ProcessMode = ProcessModeEnum.Disabled;
        // ⚠️ Aimed at his BELT, not his eyes. The conversation panel is modal and owns the bottom
        // half of the screen, so a camera levelled at an officer's face puts the officer behind it.
        camera.GlobalPosition = OnGround(camera, captain.GlobalPosition + new Vector3(2.4f, 1.9f, 2.1f), 1.9f);
        camera.LookAt(captain.GlobalPosition + new Vector3(0f, 0.7f, 0f), Vector3.Up);

        if (!talk.Interact(player))
        {
            GD.PushError($"--guild-shots: {guild.LeaderNpcId} refused to talk.");
            return;
        }

        GD.Print($"--guild-shots: opened '{talk.DialogueId}' as a " + (member ? "member." : "stranger."));
    }

    private static Entity? OfficerOf(string templateId)
    {
        if (string.IsNullOrEmpty(templateId) || Engine.GetMainLoop() is not SceneTree tree)
        {
            return null;
        }

        foreach (Node node in tree.Root.FindChildren("*", recursive: true, owned: false))
        {
            if (node is Entity entity && entity.TemplateId == templateId)
            {
                return entity;
            }
        }

        return null;
    }

    private static Node3D? Find(string nodeName) =>
        Engine.GetMainLoop() is SceneTree tree
            ? tree.Root.FindChild(nodeName, recursive: true, owned: false) as Node3D
            : null;
}
