using System.Collections.Generic;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups
{
    /// <summary>
    /// Stock-aware ordering: re-sorts a group's shared list by urgency just before a bench scan,
    /// for groups in Balance mode or with "one of each first" switched on.
    ///
    /// <b>Why before a scan and not at job start.</b> Round robin and "do this next" are both
    /// driven by events that are also the moments their answer changes — a job start, a click —
    /// so they mutate the list there and cost nothing on the scan path. Stock is not like that.
    /// It moves when a craft finishes, when something is hauled and when something is eaten or
    /// worn, and none of those is a job start. A pawn finishes a shirt at t=2000 taking stock from
    /// 0 to 1; no job started in between, so a job-start sort never ran, and the next pawn scans
    /// against t=0 data with the shirt still at the front and makes a second one — exactly the
    /// case "one of each first" exists to prevent. So the sort runs in the
    /// <c>WorkGiver_DoBill.JobOnThing</c> prefix, which already brackets each bench scan, before
    /// vanilla walks the list. Selection itself is still untouched: this only reorders the list
    /// vanilla then walks top-down, the same trick every other ordering in this mod plays.
    ///
    /// <b>What it costs, and why it is bounded.</b> The prefix runs per bench per pawn per work
    /// scan, and vanilla's product count can be a map-wide walk. So a sort happens at most once
    /// per <see cref="CountFreshness.TtlTicks"/> per group, and each bill's count is reused for
    /// the same window — except that a job starting or ending on one of the group's bills marks
    /// the group dirty and drops that bill's count at once (<see cref="NotifyBillActivity"/>).
    /// Those are the two events after which the old order is most likely to be wrong: a start
    /// adds an in-flight claim, and an end is usually a product arriving. Everything in between
    /// is hauling and consumption, which a second's lag does not matter to.
    ///
    /// <b>The marker re-promotes itself here.</b> The marked "do this next" order is the sort's
    /// top tier, so any sort puts it back at the head. That is the re-promotion a list-rearranging
    /// mode owes the marker, obtained from the key rather than from a second pass.
    /// </summary>
    public static class UrgencySort
    {
        /// <summary>"One of each first". A per-bill floor could replace this constant later
        /// without touching the comparator, which already takes the floor as an argument.</summary>
        public const int OneEachFloor = 1;

        /// <summary>
        /// The prefix's entry point. Cheap for every bench that is not in a sorting group: one map
        /// component lookup, one hash lookup and a comp read, all of which the ingredient-mute
        /// prefix on the same method already pays.
        /// </summary>
        public static void BeforeScan(Thing thing)
        {
            if (!(thing is Building_WorkTable bench) || !bench.Spawned)
            {
                return;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null || !index.IsGrouped(bench))
            {
                return;
            }

            // In a group the bench's stack *is* the anchor's, and its billGiver is the anchor —
            // cheaper than asking the index, and the same answer by construction.
            BillStack stack = bench.billStack;
            CompBillGroup anchorComp = NextOrder.AnchorCompOf(stack);
            if (anchorComp == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (Sorts(anchorComp))
            {
                if (IsDue(anchorComp, now))
                {
                    SortNow(anchorComp, stack, now);
                }

                return;
            }

            // Not a sorting group, but a marked "do until you have X" order still needs a count
            // for its clearing rule. One bill, at most once per TTL.
            RefreshMarkedTargetCount(anchorComp, now);
        }

        /// <summary>
        /// A job carrying <paramref name="bill"/> started or ended. Marks its group for a re-sort
        /// before the next scan and drops the bill's cached count, so neither the order nor the
        /// count waits out the clock after the two events most likely to have changed them.
        /// </summary>
        public static void NotifyBillActivity(Bill bill)
        {
            CompBillGroup anchorComp = NextOrder.AnchorCompOf(bill?.billStack);
            if (anchorComp == null)
            {
                return;
            }

            anchorComp.SortDirty = true;
            anchorComp.ProductCounts.Remove(bill);
        }

        /// <summary>Whether a group re-sorts its list at all.</summary>
        public static bool Sorts(CompBillGroup anchorComp)
        {
            return anchorComp.Ordering == OrderingMode.Balance || anchorComp.OneEachFirst;
        }

        /// <summary>
        /// The product count for one bill: cached if fresh, otherwise recounted and cached.
        /// Returns false for a bill vanilla cannot count, which has no stock to be short of.
        /// </summary>
        public static bool TryCount(CompBillGroup anchorComp, Bill_Production bill, int now, out int stock)
        {
            stock = 0;
            if (!CanCount(bill))
            {
                return false;
            }

            Dictionary<Bill, CachedCount> cache = anchorComp.ProductCounts;
            if (cache.TryGetValue(bill, out CachedCount cached)
                && CountFreshness.IsFresh(cached.StampedAt, now, CountFreshness.TtlTicks))
            {
                stock = cached.Stock;
                return true;
            }

            stock = bill.recipe.WorkerCounter.CountProducts(bill);
            cache[bill] = new CachedCount { Stock = stock, StampedAt = now };
            return true;
        }

        /// <summary>
        /// The last count taken for a bill, however old, without counting. For the marker's
        /// clearing rule, which is asked from the drawing path once per visible row per frame and
        /// so must never be the thing that triggers a map-wide walk.
        /// </summary>
        public static bool TryPeekCount(CompBillGroup anchorComp, Bill bill, out int stock)
        {
            stock = 0;
            if (anchorComp == null || bill == null
                || !anchorComp.ProductCounts.TryGetValue(bill, out CachedCount cached))
            {
                return false;
            }

            stock = cached.Stock;
            return true;
        }

        private static bool IsDue(CompBillGroup anchorComp, int now)
        {
            return anchorComp.SortDirty
                   || !CountFreshness.IsFresh(anchorComp.LastSortTick, now, CountFreshness.TtlTicks);
        }

        /// <summary>
        /// <c>CountProducts</c> dereferences <c>bill.Map</c>, which is the stack owner's map. The
        /// scan only reaches here for a spawned bench in a group, but the anchor can be mid-way
        /// through a despawn during a gravship launch, so this is checked rather than assumed.
        /// </summary>
        private static bool CanCount(Bill_Production bill)
        {
            return bill?.recipe?.WorkerCounter != null
                   && bill.Map != null
                   && bill.recipe.WorkerCounter.CanCountProducts(bill);
        }

        private static void SortNow(CompBillGroup anchorComp, BillStack stack, int now)
        {
            anchorComp.SortDirty = false;
            anchorComp.LastSortTick = now;

            List<Bill> bills = stack.Bills;
            if (bills.Count < 2)
            {
                return;
            }

            Bill marked = NextOrder.Resolve(anchorComp);
            Dictionary<string, int> authored = AuthoredPositions(anchorComp);

            UrgencyInput[] inputs = new UrgencyInput[bills.Count];
            for (int i = 0; i < bills.Count; i++)
            {
                inputs[i] = InputFor(anchorComp, bills[i], i, marked, authored, now);
            }

            int[] order = UrgencyOrder.Sort(
                inputs,
                anchorComp.OneEachFirst ? OneEachFloor : 0,
                anchorComp.Ordering == OrderingMode.Balance);

            // A sort that changes nothing is not applied, so a settled list is never mutated —
            // nothing watching the list sees a change that is not one.
            if (UrgencyOrder.IsIdentity(order))
            {
                return;
            }

            ApplyPermutation(bills, order);
        }

        /// <summary>
        /// The one place the sort writes the list. Kept to one call so anything that has to hear
        /// about a deliberate reorder of ours — such as a check for *other* mods reordering the
        /// list behind our back — has exactly one line to hook.
        /// </summary>
        private static void ApplyPermutation(List<Bill> bills, int[] order)
        {
            Bill[] before = bills.ToArray();
            for (int i = 0; i < order.Length; i++)
            {
                bills[i] = before[order[i]];
            }
        }

        /// <summary>
        /// Where the tiebreak comes from, per mode.
        ///
        /// Under round robin the current position is the base: its rotation *is* its state, and
        /// sorting against the authored order would undo every rotation on every scan. Anywhere
        /// else the authored position is: under "in order" with floors on, that is what lets a
        /// lifted order drop back to where the player put it once it has its one, and under
        /// Balance it makes ties come out in the player's order rather than in whatever order the
        /// previous sort happened to leave.
        ///
        /// Built only when a sort is actually due, because it costs one load-ID string per bill.
        /// </summary>
        private static Dictionary<string, int> AuthoredPositions(CompBillGroup anchorComp)
        {
            if (anchorComp.Ordering == OrderingMode.RoundRobin)
            {
                return null;
            }

            List<string> ids = anchorComp.CanonicalOrderIds;
            Dictionary<string, int> positions = new Dictionary<string, int>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                positions[ids[i]] = i;
            }

            return positions;
        }

        private static UrgencyInput InputFor(
            CompBillGroup anchorComp,
            Bill bill,
            int currentIndex,
            Bill marked,
            Dictionary<string, int> authored,
            int now)
        {
            UrgencyInput input = new UrgencyInput
            {
                IsMarked = ReferenceEquals(bill, marked),
                BaseIndex = BaseIndexOf(bill, currentIndex, authored),
            };

            if (!(bill is Bill_Production production)
                || !TryCount(anchorComp, production, now, out int stock))
            {
                return input;
            }

            input.Countable = true;
            input.Count = UrgencyOrder.EffectiveCount(
                stock, InFlightTracker.InFlight(bill), UnitsPerIteration(production));

            if (BenchEligibility.RepeatModeOf(production) == RepeatModeCode.TargetCount)
            {
                input.HasTarget = true;
                input.Target = production.targetCount;
            }

            return input;
        }

        /// <summary>
        /// Authored position, or — for a bill added since the snapshot — after every authored bill
        /// in its current relative order, which is where <see cref="CanonicalOrder.Restore"/>
        /// would put it too.
        /// </summary>
        private static int BaseIndexOf(Bill bill, int currentIndex, Dictionary<string, int> authored)
        {
            if (authored == null)
            {
                return currentIndex;
            }

            return authored.TryGetValue(bill.GetUniqueLoadID(), out int position)
                ? position
                : authored.Count + currentIndex;
        }

        /// <summary>
        /// Products one craft makes. Vanilla's base counter only counts single-product recipes, so
        /// for those the first product's count is exact; a subclass counter that counts something
        /// else (stone blocks, butchered meat) is credited one per worker, which errs towards
        /// sorting a busy bill slightly too early rather than a second pawn starting it.
        /// </summary>
        private static int UnitsPerIteration(Bill_Production bill)
        {
            List<ThingDefCountClass> products = bill.recipe.products;
            return products != null && products.Count == 1 ? products[0].count : 1;
        }

        private static void RefreshMarkedTargetCount(CompBillGroup anchorComp, int now)
        {
            if (anchorComp.NextOrderBillId == null)
            {
                return;
            }

            if (NextOrder.Resolve(anchorComp) is Bill_Production marked
                && BenchEligibility.RepeatModeOf(marked) == RepeatModeCode.TargetCount)
            {
                TryCount(anchorComp, marked, now, out _);
            }
        }
    }
}
