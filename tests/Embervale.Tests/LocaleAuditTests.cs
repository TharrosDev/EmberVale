using Embervale.Localization;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure localization audit behind the Phase 25.5F validator gate. The two failure modes
/// that slip past <see cref="LocCatalog.Parse"/> (which dedupes and tolerates gaps so the game still
/// boots) — a duplicate key, and a key missing its default-locale value — are pinned here.
/// </summary>
public class LocaleAuditTests
{
    private const string Header = "keys,en,fr\n";

    [Fact]
    public void CleanCatalog_HasNoIssues()
    {
        string csv = Header + "hud.compass.n,N,N\nmenu.continue,Continue,Continuer\n";
        Assert.Empty(LocaleAudit.Audit(csv, "en"));
    }

    [Fact]
    public void DuplicateKey_IsFlagged()
    {
        string csv = Header + "menu.continue,Continue,Continuer\nmenu.continue,Resume,Reprendre\n";
        var issues = LocaleAudit.Audit(csv, "en");
        Assert.Contains(issues, i => i.Contains("duplicate") && i.Contains("menu.continue"));
    }

    [Fact]
    public void MissingDefaultLocaleValue_IsFlagged()
    {
        // The fr column has the string but en is empty → the UI would show "menu.settings".
        string csv = Header + "menu.settings,,Paramètres\n";
        var issues = LocaleAudit.Audit(csv, "en");
        Assert.Contains(issues, i => i.Contains("menu.settings") && i.Contains("en"));
    }

    [Fact]
    public void CommentAndBlankRows_AreIgnored()
    {
        string csv = Header + "# a comment line\n\nhud.compass.n,N,N\n";
        Assert.Empty(LocaleAudit.Audit(csv, "en"));
    }

    [Fact]
    public void MissingDefaultLocaleColumn_IsFlagged()
    {
        string csv = "keys,fr\nmenu.continue,Continuer\n";
        var issues = LocaleAudit.Audit(csv, "en");
        Assert.Contains(issues, i => i.Contains("default locale"));
    }
}
