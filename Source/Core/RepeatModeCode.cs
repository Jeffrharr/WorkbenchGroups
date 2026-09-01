namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Mirror of RimWorld's three <c>BillRepeatModeDef</c>s as a plain value, so the eligibility
    /// arithmetic in <see cref="OvershootPolicy"/> can be unit-tested without loading defs.
    ///
    /// The adapter maps <c>BillRepeatModeDefOf</c> onto this; if Ludeon ever adds a fourth mode,
    /// the adapter's mapping is the single place that has to notice, and it fails closed by
    /// treating an unknown mode as <see cref="Forever"/> (i.e. never blocks work).
    /// </summary>
    public enum RepeatModeCode
    {
        /// <summary>"Do X times" — a finite counter that ticks down.</summary>
        RepeatCount = 0,

        /// <summary>"Do until you have X" — compares a live map-wide product count to a target.</summary>
        TargetCount = 1,

        /// <summary>"Do forever" — unbounded, so overshoot is meaningless and never blocked.</summary>
        Forever = 2,
    }
}
