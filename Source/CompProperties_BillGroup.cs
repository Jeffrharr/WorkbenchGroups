using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Injected onto every groupable work table at startup by <see cref="BillGroupInjector"/>
    /// rather than declared in XML, so modded benches are covered without a patch per mod.
    /// </summary>
    public class CompProperties_BillGroup : CompProperties
    {
        public CompProperties_BillGroup()
        {
            compClass = typeof(CompBillGroup);
        }
    }
}
