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

        public static string LabelOf(OrderingMode mode)
        {
            return mode == OrderingMode.RoundRobin
                ? "WBG_ModeRoundRobin".Translate()
                : "WBG_ModeInOrder".Translate();
        }

        /// <summary>The mode a group is in, read off its anchor. Defaults for an absent comp.</summary>
        public static OrderingMode CurrentOf(CompBillGroup anchorComp)
        {
            return anchorComp?.Ordering ?? OrderingMode.InOrder;
        }
    }
}
