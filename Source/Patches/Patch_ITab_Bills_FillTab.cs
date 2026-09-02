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
        // Vanilla's own layout, in tab space, so this button can line up with it rather than
        // approximately near it. RimWorld's UI is immediate-mode with hardcoded rects — there is
        // no layout system, no struts, nothing to anchor to — so matching it means restating it.
        //
        //   ITab_Bills.FillTab passes new Rect(0, 0, 420, 480).ContractedBy(10) to DoListing,
        //   which BeginGroups it. So the group origin is (10, 10).
        //   BillStack.DoListing draws "Add bill" at group-local (0, 0, 150, 29)  -> tab (10, 10).
        //   Its scroll view starts at group-local y = 35                          -> tab y = 45.
        //   ITab_Bills draws paste at (WinSize.x - 48, 3, 24, 24)                 -> tab (372, 3).
        //
        // Matching "Add bill"'s y and height is what makes the two read as one row; the earlier
        // rect sat eight pixels high with a shorter body, which looked like a mistake because it
        // was one.
        private const float AddBillRight = 160f;   // 10 + 150
        private const float PasteLeft = 372f;      // 420 - 48
        private const float Gap = 8f;

        /// <summary>
        /// Vertically identical to "Add bill", filling the gap between it and the paste button.
        ///
        /// If a future RimWorld moves either neighbour this button does not overlap anything
        /// structural — it just stops being flush, which is visible and harmless. Deliberately not
        /// pinned by a Cecil test: the numbers live inside method bodies, and a test that reads
        /// them would be reimplementing the layout rather than checking it.
        /// </summary>
        private static readonly Rect ButtonRect = new Rect(
            AddBillRight + Gap,
            10f,
            PasteLeft - Gap - (AddBillRight + Gap),
            29f);

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

            // Hover-gated for the same reason as the bill rows: this runs every frame the tab is
            // open, and building a paragraph-length translated string for a tooltip nobody has
            // asked for was a measurable share of this postfix.
            if (Mouse.IsOver(ButtonRect))
            {
                TooltipHandler.TipRegion(ButtonRect, "WBG_CommandOrderingDesc".Translate());
            }

            WarnIfRowAnnotationsWereSkipped(bench);
        }

        /// <summary>
        /// Says so, once, if the bill rows were drawn without our annotations.
        ///
        /// This postfix runs after <c>DoListing</c>, which is what calls <c>Bill.DoInterface</c>
        /// on each row — so by now our row patch should have run this frame. If it has not while
        /// there are bills to draw, something replaced the row drawing, and the chain icons and
        /// active-bill highlight are silently absent. That failure is otherwise invisible: the
        /// tab just looks like plain vanilla, which is exactly what "the mod is off" looks like.
        ///
        /// A warning rather than a fix. Which mod is drawing the rows and where it expects to put
        /// things is not knowable from here, and guessing at a layout we do not own would trade a
        /// missing icon for a misplaced one.
        /// </summary>
        private static void WarnIfRowAnnotationsWereSkipped(Building_WorkTable bench)
        {
            if ((bench.billStack?.Count ?? 0) == 0
                || Patch_Bill_DoInterface.LastDrawnFrame == Time.frameCount)
            {
                return;
            }

            Log.WarningOnce(
                "[Workbench Groups] The bill list was drawn without calling Bill.DoInterface, so "
                + "the shared-order chain icons and the in-progress highlight are missing. Another "
                + "mod is drawing bill rows itself. Group behaviour is unaffected; only these "
                + "annotations are.",
                0x77B6_0001);
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
