using System.Collections.Generic;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Marks, or unmarks, the group's "do this next" order — the same call the row button makes.
    ///
    /// Goes through <c>NextOrder.Toggle</c> rather than writing the comp field directly, so the
    /// scenario exercises the shipped promotion, the shipped one-per-group replacement and the
    /// shipped toggle-off. Setting the field by hand would prove the probe can read it and
    /// nothing else.
    ///
    /// Bills are named by the slot the scenario queued them in, not by recipe, because the whole
    /// point of the feature is that the list moves underneath: by the time anything is marked the
    /// bill at "slot 2" is no longer the third row.
    /// </summary>
    public sealed class WbgMarkNextOrderStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgMarkNextOrder";

        /// <summary>Slot value meaning "unmark whatever is marked", so one step covers both.</summary>
        private const int ClearSlot = -1;

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgMarkNextOrder requires 'slot' (the index WbgAddBill queued the bill at, "
                        + "or -1 to unmark)";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            CompBillGroup anchorComp = WbgNextOrderSupport.AnchorComp(ctx.Map);
            if (anchorComp == null)
            {
                return StepOutcome.Fail(
                    "WbgMarkNextOrder: no anchor comp — run WbgLinkBenches first");
            }

            int slot = int.Parse(args["slot"]);

            if (slot == ClearSlot)
            {
                NextOrder.Clear(anchorComp);
                return new StepOutcome();
            }

            if (slot < 0 || slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail(
                    $"WbgMarkNextOrder: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            NextOrder.Toggle(anchorComp, WbgTestState.Bills[slot]);
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Deletes one queued bill through <c>BillStack.Delete</c>, the route the row's delete button
    /// takes.
    ///
    /// Exists so a scenario can show that a marker cannot outlive the order it names. The bill is
    /// left in <see cref="WbgTestState.Bills"/> afterwards so the remaining slot numbers keep
    /// meaning what the scenario wrote — a deleted bill simply stops being found in the stack,
    /// which is exactly the state the clearing rule is about.
    /// </summary>
    public sealed class WbgDeleteBillStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgDeleteBill";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgDeleteBill requires 'slot' (the index WbgAddBill queued the bill at)";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            int slot = int.Parse(args["slot"]);
            if (slot < 0 || slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail(
                    $"WbgDeleteBill: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            Bill_Production bill = WbgTestState.Bills[slot];
            BillStack stack = bill.billStack;
            if (stack == null)
            {
                return StepOutcome.Fail($"WbgDeleteBill: the bill in slot {slot} is not in any stack");
            }

            stack.Delete(bill);
            return new StepOutcome();
        }
    }

    /// <summary>Shared lookup, so the two steps and the probe agree on whose marker they mean.</summary>
    internal static class WbgNextOrderSupport
    {
        /// <summary>
        /// The comp holding the tracked group's state — the anchor's, never the bench the
        /// scenario happens to have listed first. Anchor election is by position, so those are
        /// not reliably the same bench, and reading a follower's copy of anchor-only state is how
        /// this mod's inspect line once reported the wrong ordering mode to half a group.
        /// </summary>
        internal static CompBillGroup AnchorComp(Map map)
        {
            Building_WorkTable bench = WbgTestState.Benches.Count > 0 ? WbgTestState.Benches[0] : null;
            if (bench == null)
            {
                return null;
            }

            Building_WorkTable anchor = BillGroupIndex.For(map)?.AnchorOf(bench);
            return anchor?.GetComp<CompBillGroup>();
        }
    }
}
