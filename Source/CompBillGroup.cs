using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// All of this mod's persistent state, attached to each work table.
    ///
    /// Nothing is stored in a MapComponent on purpose. A group's shared bill list is a
    /// <c>BillStack</c> owned by one member — the *anchor* — and everything else is a reference to
    /// that bench. Keeping it Thing-local means it travels with the bench for free when a
    /// gravship moves it to another map, which map-scoped state would not.
    ///
    /// The shared list is installed by pointing this bench's <c>Building_WorkTable.billStack</c>
    /// field at the anchor's stack. It has to be the field rather than a patched property because
    /// the bills tab reads the field while the work giver reads the property — only one shared
    /// object satisfies both, and a field read cannot be patched.
    /// </summary>
    public class CompBillGroup : ThingComp
    {
        /// <summary>
        /// The bench that owns this group's shared bill list. Null when this bench is ungrouped,
        /// and also null when this bench *is* the anchor — "am I an anchor" is asked of
        /// <see cref="BillGroupIndex"/>, which derives it from everyone else's references.
        /// </summary>
        private Building_WorkTable anchor;

        /// <summary>
        /// This bench's own bill list, set aside while the shared one is installed.
        ///
        /// Deliberately not scribed. During saving the field swap (see
        /// <c>Patch_Building_WorkTable_ExposeData</c>) puts this object back into
        /// <c>billStack</c>, so vanilla's own node persists it — saving it here as well would
        /// write the same bills twice and hard-error on load with duplicate load IDs.
        /// </summary>
        private BillStack shadowStack;

        /// <summary>Anchor only: how this group works through its list.</summary>
        private OrderingMode ordering = OrderingMode.InOrder;

        /// <summary>
        /// Anchor only: unique load ID of the one order the player marked "do this next", or null.
        ///
        /// A load ID rather than a <c>Bill</c> reference on purpose. Bills are deep-saved inside
        /// the anchor's own <c>billStack</c> node, and a <c>Scribe_References</c> field cannot
        /// point into a deep-saved graph — it would come back null on every load, a breakage that
        /// only appears after a reload and reads as the player imagining things. Resolving an ID
        /// against the live list also turns "the marked bill no longer exists" into a lookup
        /// miss rather than a dangling pointer, which is the whole of the staleness story.
        ///
        /// Exactly one per group. Marking a second order clears the first; priority over several
        /// bills at once is just an ordering mode again, and a single nullable field keeps both
        /// the state and its semantics trivial.
        /// </summary>
        private string nextOrderBillId;

        /// <summary>
        /// Not saved: the bill <see cref="nextOrderBillId"/> last resolved to.
        ///
        /// The ID is the truth and this is a cache, because "is this row the marked one" is asked
        /// once per visible row per frame while the tab is open and <c>Bill.GetUniqueLoadID</c>
        /// concatenates a fresh string on every call. Holding the object turns that question into
        /// a reference comparison; the ID is only re-read when the cache misses, which is once
        /// after a load and once after the marked bill goes away.
        /// </summary>
        private Bill nextOrderBillCache;

        /// <summary>
        /// Anchor only: bill load IDs in the order the player authored, snapshotted when round
        /// robin is switched on so switching it off can put the list back.
        /// </summary>
        private List<string> canonicalOrderIds = new List<string>();

        /// <summary>
        /// Anchor only: bill load IDs in the order *this mod* last left the list.
        ///
        /// Distinct from <see cref="canonicalOrderIds"/>, which is the order the player authored.
        /// This one is the order we expect to find when we next look, and its only job is to let
        /// <see cref="Core.OrderDivergence"/> notice that something else rearranged the list
        /// behind us — Nice Bill Tab's drag-and-drop bypasses <c>BillStack.Reorder</c> entirely,
        /// so no reorder patch fires and the canonical snapshot goes stale with no trace.
        ///
        /// Scribed because the gap between a drag and the next check can span a save: the player
        /// can rearrange the list, quit, reload, and only then switch the group back to in-order.
        /// </summary>
        private List<string> lastKnownOrderIds = new List<string>();

        /// <summary>
        /// Anchor only: batched round robin's per-bill batch size, keyed by bill load ID. A bill
        /// with no entry has a batch of one, which is plain round robin.
        ///
        /// Group-scoped rather than on the <c>Bill</c> because the mod does not own <c>Bill</c> —
        /// and because a batch size means nothing outside a round-robin group, so storing it with
        /// the group is the correct home rather than a compromise. Keyed by load ID for the same
        /// reason as <see cref="nextOrderBillId"/>: a reference into the deep-saved bill list
        /// would come back null after every load.
        /// </summary>
        private Dictionary<string, int> batchSizes = new Dictionary<string, int>();

        /// <summary>
        /// Anchor only: how many times each bill has been started since it last rotated. Saved,
        /// so "make five, then switch" does not restart its count of five on every reload.
        /// </summary>
        private Dictionary<string, int> batchStarts = new Dictionary<string, int>();

        // Scribe_Collections needs somewhere to put a dictionary's keys and values while it loads.
        private List<string> batchSizeKeys;
        private List<int> batchSizeValues;
        private List<string> batchStartKeys;
        private List<int> batchStartValues;

        /// <summary>
        /// Anchor only: the group's "make one of each first" toggle — a floor of one on every
        /// countable order, applied as a tier in front of whatever the ordering mode does.
        ///
        /// A flag and not an <see cref="OrderingMode"/> value because it is not an alternative to
        /// the modes but a layer over them: "one of each, then my hand-picked order" and "one of
        /// each, then balance" are both things a player means. Two enum values per mode would
        /// have doubled the save format's surface for one bit.
        /// </summary>
        private bool oneEachFirst;

        /// <summary>
        /// Not saved: product counts the urgency sort has taken recently, per bill. See
        /// <see cref="UrgencySort"/> for when an entry is trusted and when it is dropped.
        /// Keyed by the live <c>Bill</c> because it never outlives the session that filled it.
        /// </summary>
        private readonly Dictionary<Bill, CachedCount> productCounts = new Dictionary<Bill, CachedCount>();

        /// <summary>Not saved: tick of the last urgency sort, or -1 for "never".</summary>
        private int lastSortTick = -1;

        /// <summary>
        /// Not saved: set when something the sort depends on changed in a way the clock cannot
        /// see — a job started or ended on one of the group's bills, the mode or the toggle
        /// changed. Starts true so the first scan after a load sorts.
        /// </summary>
        private bool sortDirty = true;

        /// <summary>Set only between the save prefix and its finalizer.</summary>
        private BillStack sharedStackDuringSave;

        public Building_WorkTable Bench => parent as Building_WorkTable;

        /// <summary>Whether this bench follows another bench's bill list.</summary>
        public bool IsMember => anchor != null;

        public Building_WorkTable Anchor => anchor;

        /// <summary>The bench that owns the list this bench works from — itself, if ungrouped.</summary>
        public Building_WorkTable AnchorOrSelf => anchor ?? Bench;

        public OrderingMode Ordering
        {
            get => ordering;
            set => ordering = value;
        }

        public List<string> CanonicalOrderIds => canonicalOrderIds;

        public List<string> LastKnownOrderIds => lastKnownOrderIds;

        public bool OneEachFirst
        {
            get => oneEachFirst;
            set => oneEachFirst = value;
        }

        /// <summary>Whether this group's state rearranges the list on its own.</summary>
        public bool RearrangesList => Core.OrderingTransition.IsListMutating(ordering, oneEachFirst);

        /// <summary>The count cache. Read and written only by <see cref="UrgencySort"/>.</summary>
        public Dictionary<Bill, CachedCount> ProductCounts => productCounts;

        public int LastSortTick
        {
            get => lastSortTick;
            set => lastSortTick = value;
        }

        public bool SortDirty
        {
            get => sortDirty;
            set => sortDirty = value;
        }

        /// <summary>
        /// The marked order's load ID. Writing it drops the resolved-bill cache, so the next read
        /// goes back to the list — the two can never disagree.
        /// </summary>
        public string NextOrderBillId
        {
            get => nextOrderBillId;
            set
            {
                nextOrderBillId = value;
                nextOrderBillCache = null;
            }
        }

        /// <summary>
        /// The cache behind <see cref="NextOrderBillId"/>. Read and written only by
        /// <see cref="NextOrder"/>, which owns the rule for when it is still valid.
        /// </summary>
        public Bill NextOrderBillCache
        {
            get => nextOrderBillCache;
            set => nextOrderBillCache = value;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref anchor, "wbgAnchor");
            Scribe_Values.Look(ref ordering, "wbgOrdering", OrderingMode.InOrder);
            Scribe_Values.Look(ref oneEachFirst, "wbgOneEachFirst", false);
            Scribe_Collections.Look(ref canonicalOrderIds, "wbgCanonicalOrder", LookMode.Value);
            Scribe_Collections.Look(ref lastKnownOrderIds, "wbgLastKnownOrder", LookMode.Value);

            // Null default, so a save written before this feature existed loads as "nothing
            // marked" rather than needing a migration. The cache is deliberately not scribed:
            // it is rebuilt from this ID the first time anything asks.
            Scribe_Values.Look(ref nextOrderBillId, "wbgNextOrderBill", null);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && canonicalOrderIds == null)
            {
                canonicalOrderIds = new List<string>();
            }

            // A save written before this field existed loads it as null. Empty is also the right
            // starting value on its own terms: an empty "expected" order shares no bills with the
            // live list, which OrderDivergence reads as "nothing I remember moved" rather than as
            // a spurious reorder on the first check after loading.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastKnownOrderIds == null)
            {
                lastKnownOrderIds = new List<string>();
            }

            ExposeBatches();
        }

        /// <summary>
        /// Saves the batch state, pruned to bills that still exist.
        ///
        /// Pruned on save rather than on delete because a bill leaves the list by more routes than
        /// the delete button — another mod's code, a bill completing and being cleaned up, the
        /// anchor handing the list over — and every one of them ends with the save seeing the list
        /// as it really is. An entry for a bill that is gone is otherwise harmless, since nothing
        /// can look it up, so the only cost of pruning late is a few bytes held until then.
        /// </summary>
        private void ExposeBatches()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PruneBatchState();
            }

            Scribe_Collections.Look(
                ref batchSizes, "wbgBatchSizes", LookMode.Value, LookMode.Value,
                ref batchSizeKeys, ref batchSizeValues);
            Scribe_Collections.Look(
                ref batchStarts, "wbgBatchStarts", LookMode.Value, LookMode.Value,
                ref batchStartKeys, ref batchStartValues);

            // A save written before batches existed has neither node and loads both as null.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                batchSizes = batchSizes ?? new Dictionary<string, int>();
                batchStarts = batchStarts ?? new Dictionary<string, int>();
            }
        }

        private void PruneBatchState()
        {
            if (batchSizes.Count == 0 && batchStarts.Count == 0)
            {
                return;
            }

            HashSet<string> live = new HashSet<string>();
            List<Bill> bills = Bench?.billStack?.Bills;
            if (bills != null)
            {
                foreach (Bill bill in bills)
                {
                    live.Add(bill.GetUniqueLoadID());
                }
            }

            RemoveKeysNotIn(batchSizes, live);
            RemoveKeysNotIn(batchStarts, live);
        }

        private static void RemoveKeysNotIn(Dictionary<string, int> map, HashSet<string> keep)
        {
            List<string> stale = new List<string>();
            foreach (string key in map.Keys)
            {
                if (!keep.Contains(key))
                {
                    stale.Add(key);
                }
            }

            foreach (string key in stale)
            {
                map.Remove(key);
            }
        }

        /// <summary>
        /// This bill's batch size — one unless the player set one.
        ///
        /// Asked per visible row per frame while a round-robin group's tab is open, and
        /// <c>GetUniqueLoadID</c> builds a fresh string on every call. The empty-map check first
        /// means a group nobody has set a batch on — nearly all of them — never builds one.
        /// </summary>
        public int BatchSizeOf(Bill bill)
        {
            if (bill == null || batchSizes.Count == 0)
            {
                return 1;
            }

            return batchSizes.TryGetValue(bill.GetUniqueLoadID(), out int size)
                ? Core.BillOrdering.ClampBatchSize(size)
                : 1;
        }

        /// <summary>
        /// Sets a bill's batch size. One removes the entry rather than storing it, so the default
        /// stays the absence of state and the empty-map fast path above keeps applying.
        ///
        /// Also restarts the bill's running count: "make five" chosen part-way through a batch of
        /// ten reads as "five from now", not "five counting the three already made".
        /// </summary>
        public void SetBatchSize(Bill bill, int size)
        {
            if (bill == null)
            {
                return;
            }

            string id = bill.GetUniqueLoadID();
            int clamped = Core.BillOrdering.ClampBatchSize(size);
            if (clamped == 1)
            {
                batchSizes.Remove(id);
            }
            else
            {
                batchSizes[id] = clamped;
            }

            batchStarts.Remove(id);
        }

        /// <summary>
        /// Counts one start of <paramref name="bill"/> and says whether its batch is now complete,
        /// i.e. whether round robin should rotate it. The arithmetic is
        /// <see cref="Core.BillOrdering.CompletesBatch"/>; this only keeps the counter.
        /// </summary>
        public bool CountStartAndCheckBatch(Bill bill)
        {
            if (bill == null || batchSizes.Count == 0)
            {
                // Nothing batched anywhere in the group: every start completes a batch of one,
                // and there is no counter worth keeping. Also skips the load-ID string on the
                // path every job start in a round-robin group runs through.
                return true;
            }

            string id = bill.GetUniqueLoadID();
            int size = batchSizes.TryGetValue(id, out int stored) ? stored : 1;
            batchStarts.TryGetValue(id, out int starts);

            bool complete = Core.BillOrdering.CompletesBatch(starts, size, out int after);
            if (after == 0)
            {
                batchStarts.Remove(id);
            }
            else
            {
                batchStarts[id] = after;
            }

            return complete;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            BillGroupIndex.For(parent.Map)?.SetDirty();

            // On a fresh placement or a re-install from a minified state the anchor is already
            // spawned, so the redirect can go in immediately. During a load it cannot: the anchor
            // may not have spawned yet, which is why PostMapInit repeats this.
            if (!respawningAfterLoad)
            {
                TryInstallRedirect();
            }
        }

        public override void PostMapInit()
        {
            base.PostMapInit();

            // Runs once every Thing on the map is spawned and every reference is resolved, so it
            // is the earliest point at which the anchor is guaranteed to exist.
            TryInstallRedirect();
            BillGroupIndex.For(parent.Map)?.SetDirty();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);

            // Membership survives a despawn so that minifying a bench, or riding a gravship to
            // another map, does not silently dissolve the player's group. Only the redirect is
            // withdrawn, so a despawned bench holds its own list rather than a live pointer into
            // a group it may never rejoin.
            BillGroupOps.HandOffAnchorIfNeeded(this, map);
            WithdrawRedirect();
            BillGroupIndex.For(map)?.SetDirty();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            // Destruction is final, so the group must not keep referring to this bench. If it was
            // the anchor, the shared list has to move now or every bill in the group becomes
            // dereferenced and every in-progress craft on every member aborts.
            BillGroupOps.HandOffAnchorIfNeeded(this, previousMap);
            anchor = null;
            shadowStack = null;
            BillGroupIndex.For(previousMap)?.SetDirty();
        }

        public override void PreSwapMap()
        {
            base.PreSwapMap();

            // A gravship carries the whole assembly to a new map. Membership is kept; whether the
            // group actually reforms is re-decided on the far side by TryInstallRedirect, using
            // the same "is my anchor on my map" test vanilla uses for storage groups.
            WithdrawRedirect();
        }

        /// <summary>Joins this bench to a group, or re-points it at a new anchor.</summary>
        public void SetAnchor(Building_WorkTable newAnchor)
        {
            anchor = newAnchor;
            TryInstallRedirect();
        }

        /// <summary>
        /// Takes ownership of the shared list this bench is already pointing at.
        ///
        /// Used when the previous anchor is destroyed or leaves: the stack object does not move,
        /// so bills mid-craft, jobs referencing them and the tab all stay valid — only the
        /// question of who owns it changes. The discarded shadow is always an empty stack,
        /// because linking clears every member's own list.
        /// </summary>
        public void PromoteToAnchor()
        {
            anchor = null;
            shadowStack = null;
        }

        /// <summary>
        /// Inherits the group's mode and remembered ordering from the outgoing anchor, so a
        /// handover does not quietly reset a group from round robin back to in-order.
        /// </summary>
        public void AdoptGroupState(CompBillGroup previousAnchor)
        {
            if (previousAnchor == null)
            {
                return;
            }

            ordering = previousAnchor.ordering;
            oneEachFirst = previousAnchor.oneEachFirst;
            sortDirty = true;
            canonicalOrderIds = new List<string>(previousAnchor.canonicalOrderIds);
            lastKnownOrderIds = new List<string>(previousAnchor.lastKnownOrderIds);

            // Batches are group state too. The bills they are keyed on are the same objects in
            // the same list, so the load IDs still name them.
            batchSizes = new Dictionary<string, int>(previousAnchor.batchSizes);
            batchStarts = new Dictionary<string, int>(previousAnchor.batchStarts);

            // The marked order must move with the group, not with the bench. The shared stack
            // object itself is handed over intact, so the bill the ID names is still in the list
            // the new anchor now owns — losing the marker here would silently un-mark an order
            // because some unrelated bench blew up. The cache comes across too: it is the same
            // Bill object in the same list, so it is still valid.
            nextOrderBillId = previousAnchor.nextOrderBillId;
            nextOrderBillCache = previousAnchor.nextOrderBillCache;

            // And the outgoing anchor stops claiming it, so a bench that is later re-linked into
            // some other group does not arrive carrying a marker for a bill it no longer holds.
            previousAnchor.NextOrderBillId = null;
        }

        /// <summary>Leaves the group, keeping this bench's own list.</summary>
        public void ClearAnchor()
        {
            WithdrawRedirect();
            anchor = null;
        }

        /// <summary>
        /// Points this bench's bill stack at its anchor's, if the anchor is currently a usable
        /// bench on the same map. Otherwise dissolves this bench's membership — which is what
        /// happens to a bench left behind by a gravship, or whose anchor was destroyed while this
        /// one sat minified in a container.
        /// </summary>
        public void TryInstallRedirect()
        {
            Building_WorkTable bench = Bench;
            if (bench == null || anchor == null)
            {
                return;
            }

            if (anchor == bench || anchor.Destroyed || !anchor.Spawned || anchor.Map != bench.Map)
            {
                ClearAnchor();
                return;
            }

            BillStack shared = anchor.billStack;
            if (shared == null || ReferenceEquals(bench.billStack, shared))
            {
                return;
            }

            shadowStack = bench.billStack;
            bench.billStack = shared;
        }

        /// <summary>
        /// Gives the bench its own list back without leaving the group. Used when the bench stops
        /// being present (despawn, map swap) but may come back.
        /// </summary>
        public void WithdrawRedirect()
        {
            Building_WorkTable bench = Bench;
            if (bench == null || anchor == null)
            {
                return;
            }

            if (shadowStack == null)
            {
                shadowStack = new BillStack(bench);
            }

            // billGiver is not saved, so a stack that came back from a load has to be re-pointed
            // at its owner before it is handed back.
            shadowStack.billGiver = bench;
            bench.billStack = shadowStack;
            shadowStack = null;
        }

        /// <summary>
        /// Swaps the shared stack out for this bench's own before vanilla saves it.
        ///
        /// Without this every member deep-saves the same bills, which warns on save, hard-errors
        /// on load with a duplicate load ID, and leaves <c>job.bill</c> resolving to an arbitrary
        /// one of the copies. The symptom is a corrupted save, so this is not optional.
        /// </summary>
        public void BeginSaveSwap()
        {
            Building_WorkTable bench = Bench;
            if (bench == null || anchor == null)
            {
                return;
            }

            if (shadowStack == null)
            {
                shadowStack = new BillStack(bench);
            }

            shadowStack.billGiver = bench;
            sharedStackDuringSave = bench.billStack;
            bench.billStack = shadowStack;
        }

        /// <summary>Restores the shared stack after saving. Runs on the exception path too.</summary>
        public void EndSaveSwap()
        {
            if (sharedStackDuringSave == null)
            {
                return;
            }

            Building_WorkTable bench = Bench;
            if (bench != null)
            {
                shadowStack = bench.billStack;
                bench.billStack = sharedStackDuringSave;
            }

            sharedStackDuringSave = null;
        }

        public override string CompInspectStringExtra()
        {
            Building_WorkTable bench = Bench;
            if (bench == null || !bench.Spawned)
            {
                return null;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null)
            {
                return null;
            }

            int size = index.GroupSize(bench);
            if (size < 2)
            {
                return null;
            }

            // Read the mode off the anchor, not off this bench. `ordering` is anchor-only state,
            // so a follower's copy is whatever it happened to hold before it joined — always the
            // InOrder default in practice. Reading it here made every non-anchor bench in a
            // round-robin group report "in order", which is the mode line saying the opposite of
            // what the group does. Found in a screenshot; no probe could see it, because the
            // ordering probe reads the anchor.
            CompBillGroup groupState = index.AnchorOf(bench)?.GetComp<CompBillGroup>() ?? this;

            return "WBG_InspectLinked".Translate(
                size, OrderingMenu.Describe(groupState.ordering, groupState.oneEachFirst));
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in BillGroupGizmos.GizmosFor(this))
            {
                yield return gizmo;
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            BillGroupGizmos.DrawGroupOverlays(this);
        }
    }

    /// <summary>One cached product count and the tick it was taken.</summary>
    public struct CachedCount
    {
        public int Stock;
        public int StampedAt;
    }
}
