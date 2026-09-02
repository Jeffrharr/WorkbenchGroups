using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Puts the group's ordering control at the top of the bills tab, next to "Add bill".
    ///
    /// It began life as a gizmo. A gizmo was the wrong home for it: ordering is a property of the
    /// list, and every other control that shapes the list — add, paste, reorder, suspend — lives
    /// in the tab. Reading the list and changing how it is worked meant looking at two places at
    /// opposite corners of the screen.
    ///
    /// Drawn in a postfix, in the tab's own GUI space. The strip between "Add bill" and vanilla's
    /// paste button is the only reliably empty room on the panel: the listing beneath is scrolled
    /// content and anything drawn into it would move.
    ///
    /// <b>Compatibility note.</b> Nice Bill Tab prefixes this method and rebuilds the tab. A
    /// Harmony postfix still runs when a prefix skips the original, so with that mod active this
    /// button is drawn over a panel laid out by someone else and may land badly. That is why the
    /// control's absence is survivable — ordering is also shown in the inspect line, and a group
    /// left in the wrong mode is a preference, not a broken save.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Bills), "FillTab")]
    public static class Patch_ITab_Bills_FillTab
    {
        /// <summary>Clear of "Add bill" on the left and vanilla's paste button on the right.</summary>
        private static readonly Rect ButtonRect = new Rect(168f, 2f, 190f, 26f);

        public static void Postfix()
        {
            Building_WorkTable bench = Find.Selector?.SingleSelectedThing as Building_WorkTable;
            if (bench == null || !bench.Spawned)
            {
                return;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null || index.GroupSize(bench) < 2)
            {
                return;
            }

            CompBillGroup anchorComp = index.AnchorOf(bench)?.GetComp<CompBillGroup>();
            OrderingMode current = anchorComp?.Ordering ?? OrderingMode.InOrder;

            if (Widgets.ButtonText(ButtonRect, "WBG_CommandOrdering".Translate(LabelOf(current))))
            {
                Find.WindowStack.Add(new FloatMenu(OrderingOptions(anchorComp, current)));
            }

            TooltipHandler.TipRegion(ButtonRect, "WBG_CommandOrderingDesc".Translate());
        }

        /// <summary>
        /// One entry per mode, the current one included, so the menu keeps the same entries in the
        /// same places rather than reshuffling as state changes. Re-picking the current mode is a
        /// no-op: <see cref="RoundRobin.SetOrdering"/> returns early when nothing changes, which
        /// matters because switching *in* is what snapshots the player's ordering.
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
    }
}
