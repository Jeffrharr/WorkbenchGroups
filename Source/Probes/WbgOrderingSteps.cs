using System;
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
    /// Sets one queued bill's round-robin batch size — the same call the chain icon's menu makes.
    ///
    /// Through <c>CompBillGroup.SetBatchSize</c> rather than the dictionary, so the shipped
    /// clamping and the shipped "one means no entry" rule are what the scenario exercises.
    /// </summary>
    public sealed class WbgSetBatchStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgSetBatch";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgSetBatch requires 'slot' (the index WbgAddBill queued the bill at)";
                return false;
            }

            if (!args.TryGetValue("size", out string size) || !int.TryParse(size, out _))
            {
                error = "WbgSetBatch requires 'size' (starts per batch)";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            CompBillGroup anchorComp = WbgNextOrderSupport.AnchorComp(ctx.Map);
            if (anchorComp == null)
            {
                return StepOutcome.Fail("WbgSetBatch: no anchor comp — run WbgLinkBenches first");
            }

            int slot = int.Parse(args["slot"]);
            if (slot < 0 || slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail(
                    $"WbgSetBatch: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            anchorComp.SetBatchSize(WbgTestState.Bills[slot], int.Parse(args["size"]));
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Moves one queued bill with <c>BillStack.Reorder</c>, the method vanilla's row arrows call.
    ///
    /// The arrows are how a player expresses "this order, not that one", so anything that is
    /// meant to honour or refuse a manual reorder has to be tested through this call and not by
    /// shuffling the list directly — a direct shuffle would bypass the very patch under test.
    /// </summary>
    public sealed class WbgReorderBillStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgReorderBill";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("slot", out string slot) || !int.TryParse(slot, out _))
            {
                error = "WbgReorderBill requires 'slot' (the index WbgAddBill queued the bill at)";
                return false;
            }

            if (!args.TryGetValue("offset", out string offset) || !int.TryParse(offset, out _))
            {
                error = "WbgReorderBill requires 'offset' (-1 is the up arrow, 1 the down arrow)";
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
                    $"WbgReorderBill: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            Bill_Production bill = WbgTestState.Bills[slot];
            if (bill.billStack == null)
            {
                return StepOutcome.Fail($"WbgReorderBill: the bill in slot {slot} is not in any stack");
            }

            bill.billStack.Reorder(bill, int.Parse(args["offset"]));
            return new StepOutcome();
        }
    }
}
