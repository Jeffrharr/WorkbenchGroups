using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Attaches <see cref="CompBillGroup"/> to every groupable work table at startup.
    ///
    /// Done in code rather than XML because the set of benches is open-ended: matching
    /// <c>thingClass</c> in an XPath patch would need one entry per mod, and would miss any bench
    /// added by a mod that loads after ours. Walking the def database catches all of them with
    /// one rule.
    ///
    /// Safe at this point in startup because static constructors run after defs are loaded and
    /// before any Thing is made — comps are instantiated at ThingMaker time, so a def gaining a
    /// comp here is indistinguishable from having declared it.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BillGroupInjector
    {
        static BillGroupInjector()
        {
            int injected = 0;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (ShouldInject(def))
                {
                    if (def.comps == null)
                    {
                        def.comps = new List<CompProperties>();
                    }

                    def.comps.Add(new CompProperties_BillGroup());
                    injected++;
                }
            }

            Log.Message($"[Workbench Groups] Enabled linking on {injected} work tables.");
        }

        private static bool ShouldInject(ThingDef def)
        {
            // Exact class match, not an assignability test: Building_WorkTableAutonomous and
            // Building_MechGestator derive from Building_WorkTable and then cast their bills'
            // owner back to their own type, so a shared list on those throws every frame rather
            // than degrading. See BenchEligibility for the full reasoning.
            if (def.thingClass != typeof(Building_WorkTable))
            {
                return false;
            }

            return !def.AllRecipes.NullOrEmpty();
        }
    }
}
