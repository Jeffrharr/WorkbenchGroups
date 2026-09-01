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
        /// Anchor only: bill load IDs in the order the player authored, snapshotted when round
        /// robin is switched on so switching it off can put the list back.
        /// </summary>
        private List<string> canonicalOrderIds = new List<string>();

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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref anchor, "wbgAnchor");
            Scribe_Values.Look(ref ordering, "wbgOrdering", OrderingMode.InOrder);
            Scribe_Collections.Look(ref canonicalOrderIds, "wbgCanonicalOrder", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && canonicalOrderIds == null)
            {
                canonicalOrderIds = new List<string>();
            }
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
            canonicalOrderIds = new List<string>(previousAnchor.canonicalOrderIds);
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

            string mode = groupState.ordering == OrderingMode.RoundRobin
                ? "WBG_ModeRoundRobin".Translate()
                : "WBG_ModeInOrder".Translate();

            return "WBG_InspectLinked".Translate(size, mode);
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
}
