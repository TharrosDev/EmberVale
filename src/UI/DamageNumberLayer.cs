using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Floating damage numbers: what the player deals, projected onto the target in the world, and what the
/// player takes, floated from the lower middle of the screen (a number over the player's own head is a
/// number over the camera). Reads <see cref="HitConfirmedEvent"/> and draws each outcome differently
/// (<see cref="DamageNumberMath.Style"/>): a crit is large, gold and banged; a block is small, steel and
/// parenthesised; a resisted blow is dim italic and says so; a parry is a word with no number; a guard
/// break and a poise break carry their word.
///
/// <para>Which blows get a number is the player's damage-number mode (<see cref="DamageNumberRules"/>:
/// none, all, their own blows only, or only crits and kills), and none at all with the HUD element
/// hidden. A crit is never told apart by colour alone: it is larger and carries a bang.
/// Under Reduced Motion the numbers hold still and live a little shorter instead of popping and rising.
/// Rapid same-outcome hits on one target fold into one running total, and a pool of labels caps how
/// many can be live, so a burn tick or a pack fight cannot carpet the screen.</para>
/// </summary>
public sealed partial class DamageNumberLayer : Control
{
    private const int MaxLive = 24;
    private const int OutlineSize = 5;

    /// <summary>The size of an ordinary number; each outcome and amount scales from it.</summary>
    private static int BaseFontSize => UiTheme.FontSize(UiTheme.TitleFontSize);

    private sealed class Entry
    {
        public Label Label = null!;
        public Vector3 World;
        public bool ScreenSpace;
        public float Age;
        public float Life;
        public float Drift;
        public float Lift;
        public float Amount;
        public float SizeScale;
        public ulong TargetId;
        public HitOutcome Outcome;
        public NumberStyle Style;
    }

    private readonly List<Entry> _live = new();
    private readonly Stack<Label> _free = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHit);
    }

    public override void _ExitTree() => EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHit);

    /// <summary>How many numbers are on screen — for the probe.</summary>
    public int LiveCount => _live.Count;

    private void OnHit(HitConfirmedEvent e)
    {
        int mode = Mode();
        if (mode == DamageNumberRules.Off || (!e.ByPlayer && !e.OnPlayer))
        {
            return;
        }

        bool kill = e.ByPlayer && e.Target.GetComponent<StatsComponent>() is { IsAlive: false };
        if (!DamageNumberRules.Shows(mode, e.ByPlayer, e.OnPlayer, e.Outcome == HitOutcome.Critical, kill))
        {
            return;
        }

        bool taken = e.OnPlayer && !e.ByPlayer;
        NumberStyle style = DamageNumberMath.Style(e.Outcome, taken);
        if (style.ShowNumber && style.WordKey == null && e.Amount <= 0f)
        {
            return; // nothing was dealt and there is no word to say about it
        }

        ulong targetId = e.Target.RuntimeId;
        foreach (Entry existing in _live)
        {
            if (DamageNumberMath.ShouldMerge(
                    existing.Age, existing.TargetId == targetId, existing.Outcome, e.Outcome))
            {
                existing.Amount += e.Amount;
                existing.Age = 0f;
                existing.World = e.Point;
                Fill(existing);
                return;
            }
        }

        if (_live.Count >= MaxLive)
        {
            Recycle(_live[0]);
            _live.RemoveAt(0);
        }

        // Recent numbers on this target hold their spot, so this one starts above them instead of
        // on top (a poise break and the riposte that follows it used to print over each other).
        float highest = -1f;
        foreach (Entry existing in _live)
        {
            if (existing.TargetId == targetId && existing.Age < DamageNumberMath.LaneWindow)
            {
                float top = existing.Lift +
                            (DamageNumberMath.Rise(existing.Age / existing.Life) * DamageNumberMath.RisePixels);
                highest = Mathf.Max(highest, top);
            }
        }

        var entry = new Entry
        {
            Label = _free.Count > 0 ? _free.Pop() : MakeLabel(),
            Lift = DamageNumberMath.LiftAbove(highest),
            World = e.Point,
            ScreenSpace = taken,
            Age = 0f,
            Life = DamageNumberMath.Life(e.Outcome) * (UiTheme.MotionEnabled ? 1f : 0.7f),
            Drift = DamageNumberMath.Drift(targetId + (Time.GetTicksMsec() / 40UL)),
            Amount = e.Amount,
            SizeScale = DamageNumberMath.SizeScale(e.Amount) * style.Scale,
            TargetId = targetId,
            Outcome = e.Outcome,
            Style = style,
        };
        Fill(entry);
        entry.Label.Visible = true;
        _live.Add(entry);
    }

    /// <summary>The damage-number mode in force: none while the HUD element is hidden, else the
    /// saved mode (or what the older on/off toggle means).</summary>
    private static int Mode()
    {
        if (GameHud.ElementMode(HudElement.DamageNumbers) == HudElementMode.Hidden)
        {
            return DamageNumberRules.Off;
        }

        return ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? SettingsMath.DamageNumberMode(settings.Current.DamageNumberMode, settings.Current.DamageNumbers)
            : DamageNumberRules.All;
    }

    /// <summary>Takes every number off the screen, so a harness can count what one blow adds.</summary>
    public void ClearForCapture()
    {
        foreach (Entry entry in _live)
        {
            Recycle(entry);
        }

        _live.Clear();
    }

    private static Label MakeLabel()
    {
        var label = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visible = false,
        };
        UiTheme.ApplyType(label, UiTheme.FontRole.Display, BaseFontSize);
        label.AddThemeConstantOverride("outline_size", OutlineSize);
        label.AddThemeColorOverride("font_outline_color", UiTheme.Keyline);
        return label;
    }

    private void Fill(Entry entry)
    {
        string? word = entry.Style.WordKey is { } key ? Loc.T(key) : null;
        Label label = entry.Label;
        label.Text = DamageNumberMath.Compose(entry.Outcome, entry.Amount, word);
        UiLive.FontColor(label, new Color(entry.Style.R, entry.Style.G, entry.Style.B));
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(BaseFontSize * entry.SizeScale));
        label.Modulate = new Color(1f, 1f, 1f, entry.Style.Italic ? 0.75f : 1f);
        if (label.GetParent() == null)
        {
            AddChild(label);
        }
    }

    private void Recycle(Entry entry)
    {
        entry.Label.Visible = false;
        _free.Push(entry.Label);
    }

    // Whether the layer is currently shown for play (-1 = not yet decided), so its visibility is
    // written when the game state changes instead of restated to the engine every frame.
    private int _shownForPlay = -1;

    private void ShowForPlay()
    {
        int playing = GameManager.Instance is { IsPlaying: true } ? 1 : 0;
        if (playing != _shownForPlay)
        {
            _shownForPlay = playing;
            Visible = playing == 1;
        }
    }

    public override void _Process(double delta)
    {
        ShowForPlay();
        if (_live.Count == 0)
        {
            return;
        }

        Camera3D? camera = GetViewport().GetCamera3D();
        bool motion = UiTheme.MotionEnabled;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Entry entry = _live[i];
            entry.Age += (float)delta;
            if (entry.Age >= entry.Life)
            {
                Recycle(entry);
                _live.RemoveAt(i);
                continue;
            }

            float t = entry.Age / entry.Life;
            float rise = motion ? DamageNumberMath.Rise(t) * DamageNumberMath.RisePixels : 0f;
            float sway = motion ? entry.Drift * 26f * DamageNumberMath.Rise(t) : entry.Drift * 12f;
            Vector2 at;
            if (entry.ScreenSpace)
            {
                at = new Vector2((Size.X * 0.5f) + (entry.Drift * 70f), Size.Y * 0.68f);
            }
            else if (camera != null && !camera.IsPositionBehind(entry.World))
            {
                at = camera.UnprojectPosition(entry.World + (Vector3.Up * 0.35f));
            }
            else
            {
                entry.Label.Visible = false;
                continue;
            }

            Label label = entry.Label;
            label.Visible = true;
            Vector2 size = label.GetCombinedMinimumSize();
            label.Position = new Vector2(at.X - (size.X * 0.5f) + sway, at.Y - rise - entry.Lift - (size.Y * 0.5f));
            label.PivotOffset = size * 0.5f;
            label.Scale = Vector2.One * (motion ? DamageNumberMath.Pop(t) : 1f);
            float alpha = DamageNumberMath.Alpha(t) * (entry.Style.Italic ? 0.75f : 1f);
            label.Modulate = new Color(1f, 1f, 1f, alpha);
        }
    }
}
