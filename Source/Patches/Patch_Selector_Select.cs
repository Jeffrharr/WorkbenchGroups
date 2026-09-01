using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Selecting one bench in a group selects the whole group.
    ///
    /// A group is one object as far as the player's mental model goes — it has one bill list and
    /// one ordering mode — so clicking any member and being shown the group is what people expect
    /// after using vanilla's linked storage. It also makes the group's extent legible without
    /// hunting for the "select linked" gizmo, which is the same information one click earlier.
    ///
    /// <b>Off by default, for a reason only a screenshot found:</b> RimWorld shows no ITab for a
    /// multi-selection. Two selected stoves give an inspect pane reading "Electric stove x2" with
    /// no tabs, so auto-selecting the group makes the bills tab unreachable by clicking a bench —
    /// and the bills tab is what this mod exists for. The second consequence stands on its own:
    /// gizmos act on the whole selection, so clicking one bench and pressing Deconstruct
    /// deconstructs the group, which is why vanilla's storage groups do not do this either.
    ///
    /// The informative half of the feature does not need this at all: the connecting line and the
    /// group outline draw off a single selected bench, so a player who leaves this off still sees
    /// exactly which benches are grouped.
    ///
    /// Patched on the public <c>Select</c> rather than <c>SelectInternal</c> so a mod selecting
    /// benches in code gets the same expansion, and guarded against reentry because the expansion
    /// itself calls <c>Select</c>.
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selector_Select
    {
        /// <summary>
        /// Set while this patch is adding the rest of a group. Without it the first added member
        /// would expand the group again, and again, until the reentry ran out of stack — the
        /// already-selected check alone does not stop it, because the recursion happens before the
        /// selection has been recorded.
        /// </summary>
        private static bool expanding;

        public static void Postfix(object obj)
        {
            if (expanding || WorkbenchGroupsMod.Settings?.selectWholeGroup != true)
            {
                return;
            }

            if (!(obj is Building_WorkTable bench) || !bench.Spawned)
            {
                return;
            }

            List<Building_WorkTable> roster = BillGroupIndex.For(bench.Map)?.RosterOf(bench);
            if (roster == null || roster.Count < 2)
            {
                return;
            }

            expanding = true;
            try
            {
                foreach (Building_WorkTable member in roster)
                {
                    if (ShouldAdd(member, bench))
                    {
                        // No sound and no designator deselect: this is one conceptual selection,
                        // and firing the click sound once per member turns a two-bench group into
                        // a stutter and a ten-bench one into a noise.
                        Find.Selector.Select(member, playSound: false, forceDesignatorDeselect: false);
                    }
                }
            }
            finally
            {
                // A finalizer-shaped guard rather than a plain reset: a throw from another mod's
                // Select patch would otherwise leave expansion permanently disabled, and the
                // symptom — "linked selection stopped working after some unrelated error" — is
                // close to undiagnosable.
                expanding = false;
            }
        }

        private static bool ShouldAdd(Building_WorkTable member, Building_WorkTable selected)
        {
            return member != selected
                && member.Spawned
                && !member.Destroyed
                && !Find.Selector.IsSelected(member);
        }
    }
}
