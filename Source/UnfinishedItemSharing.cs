using RimWorld;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Makes an order that leaves an unfinished item behind — a gun, a shirt, a sculpture —
    /// behave in a shared list the way a plain order already does: worked at the bench the pawn
    /// walked to, not at the bench that happens to own the list.
    ///
    /// This is the piece that was missing when unfinished-item orders were refused outright.
    /// Everything on <c>WorkGiver_DoBill</c>'s normal path is anchored on <c>giver</c>, the bench
    /// the pawn walked to, which is why sharing works at all. The unfinished-item path is the one
    /// exception: <c>FinishUftJob</c> aims the resume job at <c>bill.billStack.billGiver</c>
    /// instead — the anchor — and vanilla's own plain path two screens further down passes
    /// <c>giver</c> for the identical call. Shared, that difference sends a pawn standing at a
    /// free smithy trekking to the anchor, and points the haul-off at the wrong bench's cells.
    ///
    /// So the fix is to answer that one read with the bench being scanned. Three consumers ask
    /// where an unfinished item belongs, and all three are served from here:
    ///
    /// - <c>WorkGiver_DoBill.FinishUftJob</c>, via <see cref="ResumeGiver"/> — where to send the
    ///   pawn, and whose cells to clear first.
    /// - <c>HaulAIUtility.PawnCanAutomaticallyHaulFast</c>, via <see cref="ParkedGroupBench"/> —
    ///   whether the item is parked on a bench of its own group and so must be left alone.
    /// - <c>UnfinishedThing.BoundWorkTable</c>, same call — which bench not to pile hauled goods
    ///   onto, and where the selection overlay draws its line.
    ///
    /// What this deliberately does not change: a <c>Bill_ProductionWithUft</c> holds one bound
    /// item and one bound worker at a time, so an unfinished-item order occupies one bench of the
    /// group until it finishes. That is vanilla's rule per bill, not something sharing breaks, and
    /// the group still works its other orders in parallel.
    /// </summary>
    public static class UnfinishedItemSharing
    {
        /// <summary>
        /// The bench <c>WorkGiver_DoBill</c> is scanning right now, or null outside a scan.
        ///
        /// A static because the value has to cross a private vanilla method we do not otherwise
        /// touch, and job scanning is synchronous and single-threaded. Set and cleared by
        /// <see cref="Patches.Patch_WorkGiver_DoBill_JobOnThing"/>, whose finalizer is what makes
        /// it safe: an exception mid-scan must not leave a stale bench behind for the next one.
        /// </summary>
        private static Building_WorkTable scanningBench;

        /// <summary>
        /// Whether the IL redirect actually applied. False means <see cref="ResumeGiver"/> is
        /// never called, so unfinished-item orders would resolve to the anchor — which is why
        /// <c>BenchEligibility.IsShareableBill</c> refuses them in that state rather than shipping
        /// a half-working feature.
        /// </summary>
        public static bool RedirectInstalled { get; private set; }

        public static void NotifyRedirectInstalled()
        {
            RedirectInstalled = true;
        }

        public static void BeginScan(Building_WorkTable bench)
        {
            scanningBench = bench;
        }

        public static void EndScan()
        {
            scanningBench = null;
        }

        /// <summary>
        /// The bench an unfinished-item job should be aimed at: the one being scanned when it
        /// shares this bill's list and can make the recipe, and otherwise exactly what vanilla
        /// would have read.
        ///
        /// Called from IL in place of <c>bill.billStack.billGiver</c>, so it must answer for any
        /// bill, including ones with no group anywhere near them, and must never throw.
        /// </summary>
        public static IBillGiver ResumeGiver(Bill bill)
        {
            IBillGiver owner = bill?.billStack?.billGiver;
            Building_WorkTable bench = scanningBench;

            if (bench == null || ReferenceEquals(bench, owner))
            {
                return owner;
            }

            if (!SharesListWith(bench, owner) || !CanMake(bench, bill.recipe))
            {
                return owner;
            }

            return bench;
        }

        /// <summary>
        /// The bench of the item's own group that it is resting on or beside, or null if it is
        /// lying anywhere else.
        ///
        /// The footprint test mirrors vanilla's — the owner's rect expanded by one, which is how
        /// it recognises an item left on a bench's ingredient cells — and only widens it from the
        /// anchor to every member. Without that, a shirt set down at a member bench reads as
        /// litter and gets carried off to a stockpile the moment a hauler passes.
        /// </summary>
        public static Building_WorkTable ParkedGroupBench(UnfinishedThing uft)
        {
            Bill_ProductionWithUft bill = uft?.BoundBill;
            if (!(bill?.billStack?.billGiver is Building_WorkTable owner) || !owner.Spawned)
            {
                return null;
            }

            BillGroupIndex index = BillGroupIndex.For(owner.Map);
            if (index == null)
            {
                return null;
            }

            IntVec3 position = uft.Position;
            foreach (Building_WorkTable bench in index.RosterOf(owner))
            {
                if (bench.Spawned && bench.OccupiedRect().ExpandedBy(1).Contains(position))
                {
                    return bench;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="bench"/> works from the list <paramref name="owner"/> owns.
        ///
        /// Asked of the index rather than by comparing stacks, because two benches can hold
        /// equal-looking lists without sharing one, and the redirect must only fire for benches
        /// that genuinely share.
        /// </summary>
        private static bool SharesListWith(Building_WorkTable bench, IBillGiver owner)
        {
            if (!(owner is Building_WorkTable ownerBench) || !bench.Spawned)
            {
                return false;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            return index != null && index.AnchorOf(bench) == ownerBench;
        }

        /// <summary>
        /// Whether a bench can make the recipe at all. Always true while linking requires
        /// overlapping recipe sets, and load-bearing the moment a group may mix benches: aiming a
        /// resume job at a bench that cannot make the thing would strand the item for good.
        /// </summary>
        private static bool CanMake(Building_WorkTable bench, RecipeDef recipe)
        {
            return recipe != null && bench.def?.AllRecipes?.Contains(recipe) == true;
        }
    }
}
