using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The chapter title card: an act line, the chapter's name and a subtitle, held in the lower third of the
/// screen while the player keeps playing. It is shown when a chapter's first quest starts
/// (<see cref="ChapterStartedEvent"/>) or a story beat asks for one (a banner request from the story branch,
/// subscribed by name), once per save per chapter, recorded in the story flag
/// <c>flag.chapter.&lt;key&gt;</c>.
///
/// It does not pause the world and never draws over a cinematic: a banner requested while a narration
/// sequence, a conversation or a menu is up is queued and shown when the screen is the player's again, in the
/// order requested. Reduced motion removes the rise and keeps the fade. The text is looked up as
/// <c>chapter.&lt;key&gt;.title</c> / <c>.subtitle</c>, then <c>pale.chapter.&lt;key&gt;...</c>; a chapter with
/// no title text is skipped rather than drawn as a raw key.
/// </summary>
public partial class ChapterBanner : CanvasLayer
{
    /// <summary>Wrap width of the title and the line under it: inside the narrowest viewport.</summary>
    private const float TextWidth = 560f;

    private readonly BannerQueue _queue = new();

    private Control _root = null!;
    private CenterContainer _center = null!;
    private Label _act = null!;
    private Label _title = null!;
    private Label _subtitle = null!;

    private string? _showing;
    private float _elapsed;

    /// <summary>The chapter key currently on screen, or null. For harness validation.</summary>
    public string? Showing => _showing;

    public override void _Ready()
    {
        // Under the narration sequences (30) and the loading screen (20), over the HUD.
        Layer = 6;
        ProcessMode = ProcessModeEnum.Always;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // A dark band behind the text so it reads over any sky or terrain, with no panel chrome: a banner is
        // a moment, not a window.
        var band = new ColorRect
        {
            Color = UiTheme.ScrimBg with { A = 0.62f },
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        band.AnchorLeft = 0f;
        band.AnchorRight = 1f;
        band.AnchorTop = 0.58f;
        band.AnchorBottom = 0.82f;
        _root.AddChild(band);

        _center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        band.AddChild(_center);

        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _center.AddChild(stack);

        _act = UiTheme.Caption(string.Empty, UiTheme.Accent);
        _act.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(_act);

        stack.AddChild(Rule(UiTheme.RuleLit));

        _title = UiTheme.Display(string.Empty, UiTheme.Text);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.CustomMinimumSize = new Vector2(TextWidth, 0f);
        stack.AddChild(_title);

        stack.AddChild(Rule(UiTheme.Rule));

        _subtitle = UiTheme.Flavour(string.Empty, UiTheme.Dim);
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _subtitle.CustomMinimumSize = new Vector2(TextWidth, 0f);
        stack.AddChild(_subtitle);

        EventBus.Instance?.Subscribe<ChapterStartedEvent>(OnChapterStarted);

        // A banner request from a story beat or a dialogue Banner effect.
        EventBus.Instance?.Subscribe<Narrative.StoryBannerRequestedEvent>(OnBannerRequested);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<ChapterStartedEvent>(OnChapterStarted);
        EventBus.Instance?.Unsubscribe<Narrative.StoryBannerRequestedEvent>(OnBannerRequested);
    }

    /// <summary>The title sits between two hairlines: the lit one above it, a cold one below, so the
    /// band has one lit edge like every other surface.</summary>
    private static Control Rule(Color color)
    {
        var rule = new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(0f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return rule;
    }

    private void OnChapterStarted(ChapterStartedEvent e) => Request(e.ChapterKey);

    private void OnBannerRequested(Narrative.StoryBannerRequestedEvent e) => Request(e.ChapterKey);

    /// <summary>Asks for a chapter's banner. Queued; shown when the screen is free, once per save.</summary>
    public void Request(string chapterKey) => _queue.Enqueue(chapterKey);

    public override void _Process(double delta)
    {
        if (_showing == null)
        {
            if (_queue.Count > 0 && CanShow())
            {
                BeginNext();
            }

            return;
        }

        // A menu or conversation that opens mid-banner hides it and holds its clock, so the card is never
        // read through a menu and is not spent while the player cannot see it.
        if (!CanShow())
        {
            _root.Visible = false;
            return;
        }

        _root.Visible = true;
        _elapsed += (float)delta;
        BannerFrame frame = BannerTimeline.At(_elapsed, UiTheme.MotionEnabled);
        if (frame.Finished)
        {
            Finish();
            return;
        }

        Apply(frame);
    }

    /// <summary>Playing, with no menu, cinematic or conversation holding the screen. Every narration sequence
    /// and modal registers with <see cref="UiState"/>, so this one check queues behind the opening, closing and
    /// ending sequences and the vision cards without naming any of them.</summary>
    private static bool CanShow() =>
        GameManager.Instance is { IsPlaying: true } && !UiState.MenuOpen;

    private void BeginNext()
    {
        while (_queue.TryDequeue(out string key))
        {
            if (!ChapterBannerRules.ShouldShow(key, FlagHeld(key), Resolve(ChapterBannerRules.TitleKeys(key)) != null))
            {
                _queue.Done(key);
                continue;
            }

            Show(key);
            return;
        }
    }

    private void Show(string key)
    {
        _showing = key;
        _elapsed = 0f;

        int? act = ChapterBannerRules.ActNumber(key);
        _act.Text = act is { } n ? Loc.TF("questui.act_line", ChapterBannerRules.Roman(n)) : string.Empty;
        _act.Visible = act != null;
        _title.Text = Loc.T(Resolve(ChapterBannerRules.TitleKeys(key))!);
        string? subtitle = Resolve(ChapterBannerRules.SubtitleKeys(key));
        _subtitle.Text = subtitle != null ? Loc.T(subtitle) : string.Empty;
        _subtitle.Visible = subtitle != null;

        EventBus.Instance?.Publish(new SoundCueRequestedEvent(QuestNoticeCues.ChapterTitle, Vector3.Zero));
        Apply(BannerTimeline.At(0f, UiTheme.MotionEnabled));
        _root.Visible = true;
    }

    private void Apply(BannerFrame frame)
    {
        _root.Modulate = new Color(1f, 1f, 1f, frame.Alpha);
        _center.OffsetTop = frame.Rise;
        _center.OffsetBottom = frame.Rise;
    }

    private void Finish()
    {
        string key = _showing!;
        _showing = null;
        _root.Visible = false;
        _queue.Done(key);

        // Recorded after it has been shown, so a chapter whose banner never got its turn (the session ended
        // first) still gets it later.
        if (Flags() is { } flags)
        {
            flags.Set(ChapterBannerRules.FlagFor(key));
        }
    }

    private static string? Resolve(string[] keys) => JournalIndexRules.FirstResolving(keys, Loc.Has);

    private static bool FlagHeld(string key) => Flags()?.Has(ChapterBannerRules.FlagFor(key)) == true;

    private static StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<StoryFlagsComponent>()
            : null;
}
