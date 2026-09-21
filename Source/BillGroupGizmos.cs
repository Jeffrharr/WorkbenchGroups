using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// The link/unlink UI, modelled on vanilla's storage-group gizmos so the interaction is one
    /// players already know: select the buildings you want linked, press the button.
    ///
    /// Multi-selection rather than a click-to-target cursor is a deliberate copy of that
    /// precedent. It also means every action here operates on the whole current selection, which
    /// makes RimWorld's merging of identical gizmos across selected things harmless instead of
    /// silently acting on only one bench.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BillGroupGizmos
    {
        // Reusing vanilla's storage-link icons rather than shipping our own: the action is the
        // same idea, and a familiar icon is worth more than a bespoke one.
        private static readonly Texture2D LinkTex = ContentFinder<Texture2D>.Get("UI/Commands/LinkStorageSettings");
        private static readonly Texture2D UnlinkTex = ContentFinder<Texture2D>.Get("UI/Commands/UnlinkStorageSettings");
        private static readonly Texture2D SelectLinkedTex = ContentFinder<Texture2D>.Get("UI/Commands/SelectAllLinked");

        // Vanilla's two-arrow cycle, borrowed for "work the list in turn". Authored at gizmo size,
        // unlike the reorder arrows in UI/Buttons, which are list-row art and blur when scaled up.
        private static readonly Texture2D OrderingTex = ContentFinder<Texture2D>.Get("UI/Commands/SwapOutfits");

        private const int LinkGroupKey = 63140021;
        private const int UnlinkGroupKey = 63140022;
        private const int SelectGroupKey = 63140023;
        private const int OrderingGroupKey = 63140024;

        public static IEnumerable<Gizmo> GizmosFor(CompBillGroup comp)
        {
            Building_WorkTable bench = comp?.Bench;
            if (bench == null || !bench.Spawned || bench.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null)
            {
                yield break;
            }

            yield return LinkCommand(bench);

            if (index.GroupSize(bench) >= 2)
            {
                yield return UnlinkCommand();
                yield return SelectLinkedCommand(bench, index);

                // Normally there is no ordering gizmo: it lives at the top of the bills tab
                // (Patch_ITab_Bills_FillTab), because ordering is a property of the list and
                // every other control that shapes the list is already there.
                //
                // The exception is a tab we do not own. Nice Bill Tab replaces the panel
                // outright, and its layout offers nowhere stable to put a button, so the control
                // falls back to gizmo space — which is ours regardless of who draws the tab.
                // Exactly one of the two is ever shown, on the same live test.
                if (Compat.NiceBillTabCompat.IsDrawingTab())
                {
                    yield return OrderingCommand(index.AnchorOf(bench)?.GetComp<CompBillGroup>());
                }
            }
        }

        private static Gizmo LinkCommand(Building_WorkTable bench)
        {
            List<Building_WorkTable> selected = SelectedBenches(bench.Map);

            Command_Action command = new Command_Action
            {
                defaultLabel = "WBG_CommandLink".Translate(),
                defaultDesc = "WBG_CommandLinkDesc".Translate(),
                icon = LinkTex,
                groupKey = LinkGroupKey,
                action = delegate
                {
                    List<Building_WorkTable> benches = SelectedBenches(bench.Map);
                    BillGroupOps.Link(benches);
                    Messages.Message(
                        "WBG_MessageLinked".Translate(benches.Count),
                        benches[0],
                        MessageTypeDefOf.TaskCompletion,
                        historical: false);
                },
            };

            // The reason is computed for the tooltip, not just to gate the click: "you can't do
            // this" without saying why is the most common complaint about gizmos like this one.
            if (!BillGroupOps.CanLink(selected, out string reason))
            {
                command.Disable(reason);
            }

            return command;
        }

        private static Gizmo UnlinkCommand()
        {
            return new Command_Action
            {
                defaultLabel = "WBG_CommandUnlink".Translate(),
                defaultDesc = "WBG_CommandUnlinkDesc".Translate(),
                icon = UnlinkTex,
                groupKey = UnlinkGroupKey,
                action = delegate
                {
                    foreach (Building_WorkTable selected in SelectedBenches(null))
                    {
                        BillGroupOps.Unlink(selected.GetComp<CompBillGroup>());
                    }
                },
            };
        }

        /// <summary>
        /// The ordering choice, as a gizmo, for when a replacement bills tab has left nowhere in
        /// the panel to put it.
        ///
        /// Offers the same float menu as the tab button — see <see cref="OrderingMenu"/> — so the
        /// two homes cannot drift apart. Unlike the other gizmos here this one deliberately acts
        /// on the group rather than on the whole selection: ordering is anchor-held state, so
        /// applying it per selected bench would write it repeatedly to the same group, and to
        /// several groups at once when benches from two are selected together.
        /// </summary>
        private static Gizmo OrderingCommand(CompBillGroup anchorComp)
        {
            OrderingMode current = OrderingMenu.CurrentOf(anchorComp);

            return new Command_Action
            {
                defaultLabel = "WBG_CommandOrdering".Translate(OrderingMenu.LabelOf(current)),
                defaultDesc = "WBG_CommandOrderingDesc".Translate(),
                icon = OrderingTex,
                groupKey = OrderingGroupKey,
                action = delegate
                {
                    Find.WindowStack.Add(new FloatMenu(OrderingMenu.OptionsFor(anchorComp, current)));
                },
            };
        }

        private static Gizmo SelectLinkedCommand(Building_WorkTable bench, BillGroupIndex index)
        {
            return new Command_Action
            {
                defaultLabel = "WBG_CommandSelectLinked".Translate(),
                defaultDesc = "WBG_CommandSelectLinkedDesc".Translate(),
                icon = SelectLinkedTex,
                groupKey = SelectGroupKey,
                action = delegate
                {
                    List<Building_WorkTable> roster = index.RosterOf(bench);
                    Find.Selector.ClearSelection();
                    foreach (Building_WorkTable member in roster)
                    {
                        if (member.Spawned)
                        {
                            Find.Selector.Select(member);
                        }
                    }
                },
            };
        }

        /// <summary>
        /// Every groupable bench in the current selection. Filtered by map when one is given, so
        /// a selection spanning two maps cannot produce a cross-map link.
        /// </summary>
        private static List<Building_WorkTable> SelectedBenches(Map map)
        {
            List<Building_WorkTable> benches = new List<Building_WorkTable>();

            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (selected is Building_WorkTable bench
                    && BenchEligibility.IsGroupableBench(bench)
                    && (map == null || bench.Map == map))
                {
                    benches.Add(bench);
                }
            }

            return benches;
        }

        /// <summary>Outlines the whole group when any one of its benches is selected.</summary>
        public static void DrawGroupOverlays(CompBillGroup comp)
        {
            Building_WorkTable bench = comp?.Bench;
            if (bench == null || !bench.Spawned)
            {
                return;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null || index.GroupSize(bench) < 2)
            {
                return;
            }

            List<IntVec3> cells = new List<IntVec3>();
            foreach (Building_WorkTable member in index.RosterOf(bench))
            {
                if (member.Spawned && member != bench)
                {
                    cells.AddRange(member.OccupiedRect().Cells);

                    // The same line vanilla draws between a workbench and its facilities, via the
                    // same default material — so a group reads as "these are connected" using the
                    // visual language players already know, rather than a second convention of
                    // our own. Both ends draw when both are selected and the lines coincide
                    // exactly, which is also what CompFacility and CompAffectedByFacilities do.
                    GenDraw.DrawLineBetween(bench.TrueCenter(), member.TrueCenter());
                }
            }

            // Kept alongside the lines rather than replaced by them. With whole-group selection on
            // every member draws vanilla's own selection brackets anyway, but with it off this
            // box is the only thing that says which benches share the list.
            if (cells.Count > 0)
            {
                GenDraw.DrawFieldEdges(cells, Color.yellow);
            }
        }
    }
}
