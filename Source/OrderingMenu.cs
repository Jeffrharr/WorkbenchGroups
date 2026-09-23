using System.Collections.Generic;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// The group's ordering choice, as a label and a float menu.
    ///
    /// Shared because the control has two homes and must not grow two behaviours. Normally it is
    /// a button at the top of the bills tab (<c>Patches.Patch_ITab_Bills_FillTab</c>); when a mod
    /// has replaced the tab and left nowhere stable to put it, the same choice is offered as a
    /// gizmo instead (<see cref="BillGroupGizmos"/>). Only ever one of the two at a time.
    /// </summary>
    public static class OrderingMenu
    {
        /// <summary>
        /// One entry per mode, the current one included, so the menu keeps the same entries in the
        /// same places rather than reshuffling as state changes. Re-picking the current mode is a
        /// no-op: <see cref="RoundRobin.SetOrdering"/> returns early when nothing changes, which
        /// matters because switching *in* is what snapshots the player's ordering.
        /// </summary>
        public static List<FloatMenuOption> OptionsFor(CompBillGroup anchorComp, OrderingMode current)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (OrderingMode mode in new[] { OrderingMode.InOrder, OrderingMode.RoundRobin, OrderingMode.Balance })
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

            // The "one of each first" layer rides in the same menu as a toggle, after the modes.
            // Not a separate button: both homes of this menu have room for one control, and the
            // toggle composes with every mode, so it belongs with the choice of mode. Being in
            // this shared menu is also what puts it in Nice Bill Tab's pane with no extra code.
            bool floors = anchorComp?.OneEachFirst ?? false;
            options.Add(new FloatMenuOption(
                floors ? "WBG_OneEachFirstOn".Translate() : "WBG_OneEachFirstOff".Translate(),
                delegate { RoundRobin.SetOneEachFirst(anchorComp, !floors); }));

            return options;
        }

        public static string LabelOf(OrderingMode mode)
        {
            switch (mode)
            {
                case OrderingMode.RoundRobin:
                    return "WBG_ModeRoundRobin".Translate();
                case OrderingMode.Balance:
                    return "WBG_ModeBalance".Translate();
                default:
                    return "WBG_ModeInOrder".Translate();
            }
        }

        /// <summary>
        /// The mode, plus the "one of each first" layer when it is on. Every place that names the
        /// group's state — both tabs' button and the inspect line — says it through here; the
        /// inspect line once had its own ternary and told followers the wrong mode.
        /// </summary>
        public static string Describe(OrderingMode mode, bool oneEachFirst)
        {
            return oneEachFirst
                ? "WBG_ModeWithOneEachFirst".Translate(LabelOf(mode))
                : LabelOf(mode);
        }

        /// <summary>The mode a group is in, read off its anchor. Defaults for an absent comp.</summary>
        public static OrderingMode CurrentOf(CompBillGroup anchorComp)
        {
            return anchorComp?.Ordering ?? OrderingMode.InOrder;
        }
    }
}
