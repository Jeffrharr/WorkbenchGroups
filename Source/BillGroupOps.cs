using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups
{
    /// <summary>
    /// The transactions that change a group's shape: linking, unlinking, and moving the shared
    /// bill list off a bench that is going away.
    /// </summary>
    public static class BillGroupOps
    {
        /// <summary>
        /// Whether these benches may be linked, and if not, a player-facing reason.
        ///
        /// Every refusal names something the player can act on. Silently linking a subset, or
        /// linking and quietly discarding the bills that did not fit, is worse than not linking:
        /// vanilla's storage-group link does discard all but the first member's settings, but a
        /// discarded filter is recoverable in seconds and eight discarded work orders are not.
        /// </summary>
        public static bool CanLink(List<Building_WorkTable> benches, out string reason)
        {
            reason = null;

            if (benches == null || benches.Count < 2)
            {
                reason = "WBG_RefuseSelectTwo".Translate();
                return false;
            }

            Building_WorkTable first = benches[0];

            foreach (Building_WorkTable bench in benches)
            {
                if (!BenchEligibility.IsGroupableBench(bench) || !bench.Spawned)
                {
                    reason = "WBG_RefuseNotGroupable".Translate(bench.LabelShortCap);
                    return false;
                }

                if (bench.Map != first.Map)
                {
                    reason = "WBG_RefuseDifferentMaps".Translate();
                    return false;
                }

                if (!BenchEligibility.SameRecipes(first, bench))
                {
                    reason = "WBG_RefuseDifferentRecipes".Translate(
                        first.LabelShortCap, bench.LabelShortCap);
                    return false;
                }

                if (!BenchEligibility.AllBillsShareable(bench, out Bill offender))
                {
                    reason = "WBG_RefuseUnshareableBill".Translate(
                        offender.LabelCap, bench.LabelShortCap);
                    return false;
                }
            }

            if (!GroupMembership.CanMerge(DistinctBillCounts(benches), BillStack.MaxCount, out int total))
            {
                reason = "WBG_RefuseTooManyBills".Translate(total, BillStack.MaxCount);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Merges the given benches, and any groups they already belong to, into one group.
        ///
        /// The order of operations matters: every bill is collected first, then every bench is
        /// returned to owning its own (empty) list, and only then is the combined list installed
        /// on the elected anchor. Re-pointing benches while some still share a stack is what
        /// produces two comps holding the same shadow list, which then both try to restore it.
        /// </summary>
        public static void Link(List<Building_WorkTable> benches)
        {
            List<Building_WorkTable> roster = ExpandToWholeGroups(benches);
            if (roster.Count < 2)
            {
                return;
            }

            List<Bill> allBills = CollectBills(roster);

            Building_WorkTable anchor = ElectAnchor(roster);
            if (anchor == null)
            {
                return;
            }

            // Snapshotted before anything moves. Linking is the one operation that empties several
            // benches before refilling one, so a throw partway through is the one failure that can
            // lose the player's work orders outright — and the throw we cannot rule out is exactly
            // the modded-bench case the eligibility rule admits on trust (see BenchEligibility).
            LinkSnapshot snapshot = LinkSnapshot.Take(roster);

            try
            {
                InstallSharedStack(roster, anchor, allBills);
            }
            catch (Exception ex)
            {
                snapshot.Restore();
                Log.Error($"[Workbench Groups] Linking failed and was rolled back: {ex}");
                Messages.Message("WBG_RefuseLinkFailed".Translate(), MessageTypeDefOf.RejectInput, false);
            }

            BillGroupIndex.For(anchor.Map)?.SetDirty();
        }

        private static void InstallSharedStack(
            List<Building_WorkTable> roster, Building_WorkTable anchor, List<Bill> allBills)
        {
            foreach (Building_WorkTable bench in roster)
            {
                bench.GetComp<CompBillGroup>()?.ClearAnchor();
            }

            foreach (Building_WorkTable bench in roster)
            {
                bench.billStack?.Bills.Clear();
            }

            foreach (Bill bill in allBills)
            {
                anchor.billStack.AddBill(bill);
            }

            foreach (Building_WorkTable bench in roster)
            {
                if (bench != anchor)
                {
                    bench.GetComp<CompBillGroup>()?.SetAnchor(anchor);
                }
            }
        }

        /// <summary>
        /// Which stack each bench held and what was in it, so a failed link can be undone.
        ///
        /// Restoring puts the original stack object back on the bench and refills it with the same
        /// Bill instances. Reusing the instances rather than copying them matters: a bill being
        /// worked is referenced by the pawn's job, and a fresh copy would leave that job pointing
        /// at an orphan.
        /// </summary>
        private readonly struct LinkSnapshot
        {
            private readonly List<Building_WorkTable> benches;
            private readonly List<BillStack> stacks;
            private readonly List<List<Bill>> contents;

            private LinkSnapshot(
                List<Building_WorkTable> benches, List<BillStack> stacks, List<List<Bill>> contents)
            {
                this.benches = benches;
                this.stacks = stacks;
                this.contents = contents;
            }

            public static LinkSnapshot Take(List<Building_WorkTable> roster)
            {
                List<Building_WorkTable> benches = new List<Building_WorkTable>(roster.Count);
                List<BillStack> stacks = new List<BillStack>(roster.Count);
                List<List<Bill>> contents = new List<List<Bill>>(roster.Count);

                foreach (Building_WorkTable bench in roster)
                {
                    benches.Add(bench);
                    stacks.Add(bench.billStack);
                    contents.Add(bench.billStack == null
                        ? new List<Bill>()
                        : new List<Bill>(bench.billStack.Bills));
                }

                return new LinkSnapshot(benches, stacks, contents);
            }

            public void Restore()
            {
                for (int i = 0; i < benches.Count; i++)
                {
                    Building_WorkTable bench = benches[i];
                    BillStack stack = stacks[i];

                    // ClearAnchor first: whatever half-installed redirect exists has to come off
                    // before the original stack goes back, or withdrawing it later would undo the
                    // restore.
                    bench.GetComp<CompBillGroup>()?.ClearAnchor();

                    if (stack != null)
                    {
                        stack.billGiver = bench;
                        stack.Bills.Clear();
                        stack.Bills.AddRange(contents[i]);
                        bench.billStack = stack;
                    }
                }
            }
        }

        /// <summary>
        /// Removes one bench from its group, leaving the rest linked.
        ///
        /// The leaver gets its own empty list rather than a copy of the group's. Copying was
        /// considered and rejected for v1: it doubles every order the moment someone unlinks,
        /// which is a worse default than an empty bench the player can paste into.
        /// </summary>
        public static void Unlink(CompBillGroup comp)
        {
            Building_WorkTable bench = comp?.Bench;
            if (bench == null)
            {
                return;
            }

            Map map = bench.MapHeld;

            // If this bench owns the shared list, the list has to move before it leaves.
            HandOffAnchorIfNeeded(comp, map);
            comp.ClearAnchor();

            BillGroupIndex.For(map)?.SetDirty();
        }

        /// <summary>
        /// Moves ownership of a shared bill list off a bench that is being destroyed, despawned
        /// or unlinked, onto another member.
        ///
        /// This is the single most important piece of housekeeping in the mod.
        /// <c>Bill.DeletedOrDereferenced</c> reports every bill in a stack as dead once the stack's
        /// owner is destroyed, and <c>JobDriver_DoBill</c> fails on it — so without this, blowing
        /// up one bench cancels every craft in progress at every other bench in the group and the
        /// orders themselves vanish.
        ///
        /// Does nothing when no other member is currently spawned. That is deliberate: during a
        /// gravship launch every member despawns together, and handing off to nobody would
        /// dissolve groups on every jump. Leaving the list where it is lets the group reform on
        /// the far side.
        /// </summary>
        public static void HandOffAnchorIfNeeded(CompBillGroup comp, Map map)
        {
            Building_WorkTable oldAnchor = comp?.Bench;
            BillGroupIndex index = BillGroupIndex.For(map);
            if (oldAnchor == null || index == null || !index.IsAnchor(oldAnchor))
            {
                return;
            }

            List<Building_WorkTable> members = index.MembersOf(oldAnchor);
            Building_WorkTable newAnchor = ElectAnchor(SpawnedOnly(members, oldAnchor));
            if (newAnchor == null)
            {
                return;
            }

            BillStack shared = oldAnchor.billStack;

            // The new owner keeps pointing at the same stack object; it simply stops being a
            // follower. Everything referencing the stack — including bills mid-craft — stays valid.
            newAnchor.GetComp<CompBillGroup>()?.PromoteToAnchor();
            shared.billGiver = newAnchor;

            newAnchor.GetComp<CompBillGroup>()?.AdoptGroupState(comp);

            foreach (Building_WorkTable member in new List<Building_WorkTable>(members))
            {
                if (member != newAnchor)
                {
                    member.GetComp<CompBillGroup>()?.SetAnchor(newAnchor);
                }
            }

            // The outgoing bench keeps a valid, empty list of its own so nothing dangles if it
            // survives (a minified bench, or one merely leaving the group).
            oldAnchor.billStack = new BillStack(oldAnchor);
            comp.ClearAnchor();

            index.SetDirty();
        }

        private static List<Building_WorkTable> SpawnedOnly(
            List<Building_WorkTable> members, Building_WorkTable exclude)
        {
            List<Building_WorkTable> result = new List<Building_WorkTable>();
            if (members == null)
            {
                return result;
            }

            foreach (Building_WorkTable member in members)
            {
                if (member != exclude && member.Spawned && !member.Destroyed)
                {
                    result.Add(member);
                }
            }

            return result;
        }

        /// <summary>
        /// Lowest thing ID wins. Stable across saves, and independent of list order — a positional
        /// choice would let a load silently move the shared bills to a different bench.
        /// </summary>
        private static Building_WorkTable ElectAnchor(List<Building_WorkTable> benches)
        {
            if (benches == null || benches.Count == 0)
            {
                return null;
            }

            int[] ids = new int[benches.Count];
            for (int i = 0; i < benches.Count; i++)
            {
                ids[i] = benches[i].thingIDNumber;
            }

            if (!GroupMembership.TryElectAnchor(ids, out int anchorId))
            {
                return null;
            }

            foreach (Building_WorkTable bench in benches)
            {
                if (bench.thingIDNumber == anchorId)
                {
                    return bench;
                }
            }

            return null;
        }

        /// <summary>
        /// Pulls in every bench already grouped with one of the selected benches, so linking two
        /// groups merges them wholly rather than stranding their other members.
        /// </summary>
        private static List<Building_WorkTable> ExpandToWholeGroups(List<Building_WorkTable> benches)
        {
            List<Building_WorkTable> roster = new List<Building_WorkTable>();
            if (benches == null)
            {
                return roster;
            }

            foreach (Building_WorkTable bench in benches)
            {
                AddUnique(roster, bench);

                List<Building_WorkTable> existing = BillGroupIndex.For(bench.Map)?.RosterOf(bench);
                if (existing != null)
                {
                    foreach (Building_WorkTable other in existing)
                    {
                        AddUnique(roster, other);
                    }
                }
            }

            return roster;
        }

        private static void AddUnique(List<Building_WorkTable> roster, Building_WorkTable bench)
        {
            if (bench != null && !roster.Contains(bench))
            {
                roster.Add(bench);
            }
        }

        /// <summary>Bills across the roster, taking each distinct stack once.</summary>
        private static List<Bill> CollectBills(List<Building_WorkTable> roster)
        {
            List<Bill> bills = new List<Bill>();
            foreach (BillStack stack in DistinctStacks(roster))
            {
                bills.AddRange(stack.Bills);
            }

            return bills;
        }

        private static int[] DistinctBillCounts(List<Building_WorkTable> benches)
        {
            List<BillStack> stacks = DistinctStacks(benches);
            int[] counts = new int[stacks.Count];
            for (int i = 0; i < stacks.Count; i++)
            {
                counts[i] = stacks[i].Count;
            }

            return counts;
        }

        /// <summary>
        /// Distinct by reference, because benches already in a group all point at one stack and
        /// counting it once per member would refuse perfectly legal links.
        /// </summary>
        private static List<BillStack> DistinctStacks(List<Building_WorkTable> benches)
        {
            List<BillStack> stacks = new List<BillStack>();
            if (benches == null)
            {
                return stacks;
            }

            foreach (Building_WorkTable bench in benches)
            {
                BillStack stack = bench?.billStack;
                if (stack != null && !stacks.Contains(stack))
                {
                    stacks.Add(stack);
                }
            }

            return stacks;
        }
    }
}
