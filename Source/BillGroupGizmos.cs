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
                yield return OrderingCommand(bench, index);
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
        /// How the group works through its list, as a dropdown rather than a toggle.
        ///
        /// A toggle was fine for two modes and stops being fine at three; more to the point, a
        /// toggle only ever names the mode you are not in, so a player had to reason backwards
        /// from "round robin is off" to what the group was actually doing. The dropdown names the
        /// current mode on the button and lists every mode as a choice, which is the shape
        /// vanilla uses for the storage tab's own multi-way settings.
        /// </summary>
        private static Gizmo OrderingCommand(Building_WorkTable bench, BillGroupIndex index)
        {
            Building_WorkTable anchor = index.AnchorOf(bench);
            CompBillGroup anchorComp = anchor?.GetComp<CompBillGroup>();
            OrderingMode current = anchorComp?.Ordering ?? OrderingMode.InOrder;

            return new Command_Action
            {
                defaultLabel = "WBG_CommandOrdering".Translate(LabelOf(current)),
                defaultDesc = "WBG_CommandOrderingDesc".Translate(),
                icon = TexCommand.RearmTrap,
                groupKey = OrderingGroupKey,
                action = delegate
                {
                    Find.WindowStack.Add(new FloatMenu(OrderingOptions(anchorComp, current)));
                },
            };
        }

        /// <summary>
        /// One entry per mode, current one included.
        ///
        /// The current mode is listed rather than filtered out so the menu always has the same
        /// shape and the same entry in the same place — a menu whose items move depending on
        /// state is one you have to read every time. Re-picking it is a no-op;
        /// <see cref="RoundRobin.SetOrdering"/> returns early when the mode is unchanged, which
        /// matters because switching in is what snapshots the player's ordering.
        /// </summary>
        private static List<FloatMenuOption> OrderingOptions(CompBillGroup anchorComp, OrderingMode current)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (OrderingMode mode in new[] { OrderingMode.InOrder, OrderingMode.RoundRobin })
            {
                OrderingMode chosen = mode;
                string label = mode == current
                    ? "WBG_OrderingCurrent".Translate(LabelOf(mode))
                    : LabelOf(mode);

                options.Add(new FloatMenuOption(label, delegate
                {
                    RoundRobin.SetOrdering(anchorComp, chosen);
                }));
            }

            return options;
        }

        private static string LabelOf(OrderingMode mode)
        {
            return mode == OrderingMode.RoundRobin
                ? "WBG_ModeRoundRobin".Translate()
                : "WBG_ModeInOrder".Translate();
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
