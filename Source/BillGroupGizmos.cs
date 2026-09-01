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

        private static Gizmo OrderingCommand(Building_WorkTable bench, BillGroupIndex index)
        {
            Building_WorkTable anchor = index.AnchorOf(bench);
            CompBillGroup anchorComp = anchor?.GetComp<CompBillGroup>();
            bool roundRobin = anchorComp != null && anchorComp.Ordering == OrderingMode.RoundRobin;

            return new Command_Toggle
            {
                defaultLabel = "WBG_CommandRoundRobin".Translate(),
                defaultDesc = "WBG_CommandRoundRobinDesc".Translate(),
                icon = TexCommand.RearmTrap,
                groupKey = OrderingGroupKey,
                isActive = () => roundRobin,
                toggleAction = delegate
                {
                    RoundRobin.SetOrdering(
                        anchorComp,
                        roundRobin ? OrderingMode.InOrder : OrderingMode.RoundRobin);
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
                }
            }

            if (cells.Count > 0)
            {
                GenDraw.DrawFieldEdges(cells, Color.yellow);
            }
        }
    }
}
