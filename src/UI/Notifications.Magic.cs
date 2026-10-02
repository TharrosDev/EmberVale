using Embervale.Corruption;
using Embervale.Core.Events;
using Embervale.Localization;
using Embervale.Magic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The magic learning toasts: a spell learned (once, by any route), a learn attempt that was refused and
/// why, the corrupted-words warning, and a mastery rank gained. Pure presentation over
/// <see cref="SpellLearnedEvent"/>, <see cref="SpellLearnRefusedEvent"/> and <see cref="SchoolRankedUpEvent"/>;
/// a load never raises any of them, so a reload narrates nothing.
/// </summary>
public partial class Notifications
{
    private void SubscribeMagic(EventBus? bus)
    {
        bus?.Subscribe<SpellLearnedEvent>(OnSpellLearned);
        bus?.Subscribe<SpellLearnRefusedEvent>(OnSpellRefused);
        bus?.Subscribe<SchoolRankedUpEvent>(OnSchoolRanked);
    }

    private void UnsubscribeMagic(EventBus bus)
    {
        bus.Unsubscribe<SpellLearnedEvent>(OnSpellLearned);
        bus.Unsubscribe<SpellLearnRefusedEvent>(OnSpellRefused);
        bus.Unsubscribe<SchoolRankedUpEvent>(OnSchoolRanked);
    }

    private void OnSpellLearned(SpellLearnedEvent e)
    {
        if (SpellDatabase.Get(e.SpellId) is not { } spell)
        {
            return;
        }

        bool corrupted = SpellLearnRules.IsCorrupted((int)spell.MinCorruptionTier);
        Push(
            Loc.TF(corrupted ? "magic.learn.learned_corrupt" : "magic.learn.learned", SpellText.Name(spell)),
            corrupted ? UiTheme.CorruptionText : SpellSchools.Color(spell.School),
            NoticeCategory.Reward);
    }

    private void OnSpellRefused(SpellLearnRefusedEvent e)
    {
        if (SpellDatabase.Get(e.SpellId) is not { } spell)
        {
            return;
        }

        string name = SpellText.Name(spell);
        switch (e.Reason)
        {
            case SpellLearnRefusal.CorruptionTooLow:
                Push(
                    Loc.TF(
                        "magic.learn.refused_tier", name,
                        CorruptionTiers.DisplayName((CorruptionTier)e.TierRequired),
                        CorruptionTiers.DisplayName((CorruptionTier)e.TierNow)),
                    UiTheme.CorruptionText, NoticeCategory.Warning);
                break;
            case SpellLearnRefusal.AlreadyKnown:
                Push(Loc.TF("magic.learn.refused_known", name), UiTheme.Dim);
                break;
            case SpellLearnRefusal.Sealed:
                Push(Loc.T("magic.learn.refused_sealed"), UiTheme.Dim, NoticeCategory.Warning);
                break;
            case SpellLearnRefusal.ConfirmCorrupted:
                Push(Loc.TF("magic.learn.confirm_corrupt", name), UiTheme.CorruptionText, NoticeCategory.Warning);
                break;
        }
    }

    private void OnSchoolRanked(SchoolRankedUpEvent e) =>
        Push(
            Loc.TF("magic.learn.rank_up", Loc.T(SchoolNameKey(e.School)), e.Rank),
            SpellSchools.Color(e.School), NoticeCategory.Reward);

    private static string SchoolNameKey(Combat.DamageType school) => school switch
    {
        Combat.DamageType.Frost => "school.frost",
        Combat.DamageType.Lightning => "school.lightning",
        Combat.DamageType.Arcane => "school.arcane",
        Combat.DamageType.Nature => "school.nature",
        Combat.DamageType.Necrotic => "school.necrotic",
        _ => "school.fire",
    };
}
