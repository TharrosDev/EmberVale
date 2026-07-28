namespace Embervale.Companions;

/// <summary>
/// Parses the <c>&lt;companionId&gt;:&lt;amount&gt;</c> argument the companion dialogue hooks take
/// (Phase 32C) — e.g. <c>companion.kael:10</c> for a loyalty shift, or a loyalty gate's threshold.
/// Dialogue conditions/effects carry a single string by design (conversations stay pure data), so the
/// pairing has to live somewhere; keeping it here, Godot-free, means the format is unit-tested rather
/// than re-derived by every caller.
/// </summary>
public static class CompanionArg
{
    /// <summary>
    /// Splits <paramref name="arg"/> into a companion id and an amount. A malformed or amount-less
    /// argument still yields its id with an amount of 0, so a content typo degrades to "no effect"
    /// rather than throwing mid-conversation — the content validator reports the typo separately.
    /// </summary>
    public static bool TryParse(string? arg, out string companionId, out int amount)
    {
        companionId = string.Empty;
        amount = 0;
        if (string.IsNullOrWhiteSpace(arg))
        {
            return false;
        }

        // Companion ids are dotted (companion.kael), so split on the LAST colon: the id itself never
        // contains one, and this stays correct if ids ever grow qualifiers.
        int colon = arg.LastIndexOf(':');
        if (colon < 0)
        {
            companionId = arg.Trim();
            return companionId.Length > 0;
        }

        companionId = arg[..colon].Trim();
        int.TryParse(arg[(colon + 1)..].Trim(), out amount);
        return companionId.Length > 0;
    }
}
