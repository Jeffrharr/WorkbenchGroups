using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Keeps bills that cannot be shared out of a shared stack.
    ///
    /// This is the guard that lets bench eligibility be generous. A bench is admitted if it has
    /// *any* plain recipe, because refusing every bench that can also make an unfinished thing
    /// would exclude every crafting bench in the game (see <see cref="Core.RecipeGate"/>). The
    /// price of that generosity is that a grouped machining table still offers "make assault
    /// rifle", and vanilla would happily put the resulting <c>Bill_ProductionWithUft</c> into the
    /// group's list — where its unfinished item resolves through <c>billStack.billGiver</c> and
    /// ends up stranded on whichever bench owns the stack.
    ///
    /// Refused rather than silently unlinking the bench: unlinking is a bigger, less reversible
    /// surprise than a rejected order, and the player who wanted the order can unlink deliberately.
    ///
    /// A prefix on <c>AddBill</c> rather than a filter on the bills tab because <c>AddBill</c> is
    /// the one chokepoint every route passes through — the tab's dropdown, paste from the
    /// clipboard, and other mods adding bills in code. Filtering the dropdown would look tidier
    /// and would be trivially bypassed by the paste button.
    ///
    /// Returning false skips vanilla's body and every lower-priority patch on it — Hauler's Dream
    /// patches this method too. That is the intent rather than a side effect: the bill is not
    /// being added, so nobody's bookkeeping should record that it was.
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.AddBill))]
    public static class Patch_BillStack_AddBill
    {
        public static bool Prefix(BillStack __instance, Bill bill)
        {
            if (BenchEligibility.IsShareableBill(bill) || !IsSharedStack(__instance))
            {
                return true;
            }

            Messages.Message(
                "WBG_RefuseAddUnshareableBill".Translate(bill.LabelCap),
                MessageTypeDefOf.RejectInput,
                historical: false);

            return false;
        }

        /// <summary>
        /// Whether this stack is a group's shared list — that is, whether its owner is a bench
        /// that other benches currently follow.
        ///
        /// Asked of the index rather than the stack because a stack has no idea it is shared; the
        /// sharing lives in the other benches' redirects.
        ///
        /// Note this is false during <c>BillGroupOps.Link</c>'s own AddBill calls, since members
        /// are re-pointed only after the merged list is built. That is correct rather than lucky:
        /// <c>CanLink</c> has already checked every one of those bills.
        /// </summary>
        private static bool IsSharedStack(BillStack stack)
        {
            Building_WorkTable owner = stack?.billGiver as Building_WorkTable;
            if (owner == null || !owner.Spawned)
            {
                return false;
            }

            return BillGroupIndex.For(owner.Map)?.IsAnchor(owner) ?? false;
        }
    }
}
