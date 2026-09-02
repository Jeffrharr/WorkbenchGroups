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
            // The whole rule lives in BenchEligibility so injection and the link-time check can
            // never disagree about what is groupable — a bench that got a comp but is then refused
            // at link time would show a gizmo that always fails. That includes the "has recipes at
            // all" test, which is now part of the recipe gate rather than a second condition here.
            return BenchEligibility.IsGroupableDef(def);
        }
    }
}
