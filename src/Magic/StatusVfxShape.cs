namespace Embervale.Magic;

/// <summary>How a status reads on a body: the silhouette, chosen from what the status does so a new
/// status gets a readable shape without new code. Presentation only.</summary>
public enum StatusVfxShape
{
    /// <summary>The school-tinted particle swirl (burning, chill, regrowth, decay, barkskin).</summary>
    Swirl,

    /// <summary>A ring above the head: a mark other spells read (Kindled, Stormbrand, Grave Mark).</summary>
    MarkRing,

    /// <summary>Thorns at the feet: a root.</summary>
    Thorns,

    /// <summary>A broken ring with a slash above the head: a silence.</summary>
    BrokenGlyph,

    /// <summary>Stars circling the head: a stun.</summary>
    Stars,

    /// <summary>A translucent shell of ice: a freeze (Stun and Root together).</summary>
    IceShell,

    /// <summary>A translucent bubble around the body: a ward.</summary>
    WardShell,
}

public static class StatusVfxShapes
{
    /// <summary>The shape for a status with these controls. A ward wins, then the strongest lock-down.</summary>
    public static StatusVfxShape Pick(StatusControl controls, bool isWard)
    {
        if (isWard)
        {
            return StatusVfxShape.WardShell;
        }

        bool stun = (controls & StatusControl.Stun) != 0;
        bool root = (controls & StatusControl.Root) != 0;
        if (stun && root)
        {
            return StatusVfxShape.IceShell;
        }

        if (stun)
        {
            return StatusVfxShape.Stars;
        }

        if (root)
        {
            return StatusVfxShape.Thorns;
        }

        if ((controls & StatusControl.Silence) != 0)
        {
            return StatusVfxShape.BrokenGlyph;
        }

        return (controls & StatusControl.Mark) != 0 ? StatusVfxShape.MarkRing : StatusVfxShape.Swirl;
    }
}
