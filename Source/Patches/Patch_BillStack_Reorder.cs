using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Keeps a group's remembered ordering, and its "do this next" marker, in step with the
    /// player dragging bills.
    ///
    /// Sharing the reorder itself needs no code: every member points at one <c>BillStack</c>, so
    /// moving a bill at one bench moves it at all of them by construction. The problem is the
    /// snapshot. Round robin is implemented by really rotating the list, so switching it off
    /// reprojects the list onto the order snapshotted when it was switched on — and a drag
    /// performed while round robin was running is not in that snapshot. Without this, arranging
    /// your orders and then switching back to "in order" silently threw the arrangement away and
    /// restored one from minutes earlier.
    ///
    /// A manual reorder is treated as the player re-authoring the order, so the snapshot is
    /// replaced with the list as it now stands. That does bake in whatever rotation round robin
    /// had applied, and that is the intended reading: the list they just arranged is the list
    /// they were looking at, so it is the one they meant.
    ///
    /// The same reading settles the "do this next" marker. Its entire effect is that the marked
    /// order sits at the head of the list, so a drag that puts something above it has already
    /// overridden it, and the honest response is to drop the marker rather than to promote the
    /// bill back and make the reorder arrows feel broken.
    ///
    /// <b>Under Balance a reorder is refused outright.</b> Balance re-sorts the list before every
    /// bench scan, so an arrow click would be undone within a second — the arrows would work just
    /// long enough to look broken. Rather than let them fight the mode, the mode owns the order
    /// honestly: the arrows are drawn greyed (<see cref="Patch_Bill_DoInterface"/>) and the call
    /// they make does nothing. Refused here rather than only in the drawing, so another mod's
    /// reorder button is told the same thing as vanilla's.
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.Reorder))]
    public static class Patch_BillStack_Reorder
    {
        public static bool Prefix(BillStack __instance)
        {
            return !IsOwnedByBalance(__instance);
        }

        /// <summary>
        /// Whether this list is a group's list and the group is in Balance mode. Public so the row
        /// drawing greys the arrows by exactly the rule that refuses their click.
        /// </summary>
        public static bool IsOwnedByBalance(BillStack stack)
        {
            if (!(stack?.billGiver is Building_WorkTable anchor) || !anchor.Spawned)
            {
                return false;
            }

            BillGroupIndex index = BillGroupIndex.For(anchor.Map);
            if (index == null || !index.IsGrouped(anchor))
            {
                return false;
            }

            return anchor.GetComp<CompBillGroup>()?.Ordering == OrderingMode.Balance;
        }

        public static void Postfix(BillStack __instance, bool __runOriginal)
        {
            // A refused reorder changed nothing, so there is no new arrangement to snapshot —
            // and snapshotting here would bake Balance's computed order in as the player's own.
            if (!__runOriginal)
            {
                return;
            }

            if (!(__instance?.billGiver is Building_WorkTable anchor) || !anchor.Spawned)
            {
                return;
            }

            BillGroupIndex index = BillGroupIndex.For(anchor.Map);
            if (index == null || !index.IsAnchor(anchor))
            {
                return;
            }

            CompBillGroup comp = anchor.GetComp<CompBillGroup>();
            if (comp == null)
            {
                return;
            }

            // Runs in every ordering mode, unlike the snapshot below: the marker is mode-agnostic
            // by design, so the drag that overrides it has to be noticed in every mode too.
            NextOrder.ClearIfDisplacedFromHead(comp);

            // Any mode that rearranges the list keeps a snapshot, so any of them needs it
            // refreshed — not just round robin, which used to be the only one.
            if (!comp.RearrangesList)
            {
                return;
            }

            RoundRobin.ResnapshotCanonicalOrder(comp);
        }
    }
}
