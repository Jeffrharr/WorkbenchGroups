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
    /// Switches the tracked group's ordering mode through <c>RoundRobin.SetOrdering</c>, the call
    /// the ordering menu makes — so the snapshot on the way in and the restore on the way out are
    /// the shipped ones.
    /// </summary>
    public sealed class WbgSetOrderingStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgSetOrdering";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("mode", out string mode) || !Enum.TryParse(mode, out OrderingMode _))
            {
                error = $"WbgSetOrdering requires 'mode', one of: {string.Join(", ", Enum.GetNames(typeof(OrderingMode)))}";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            CompBillGroup anchorComp = WbgNextOrderSupport.AnchorComp(ctx.Map);
            if (anchorComp == null)
            {
                return StepOutcome.Fail("WbgSetOrdering: no anchor comp — run WbgLinkBenches first");
            }

            RoundRobin.SetOrdering(anchorComp, (OrderingMode)Enum.Parse(typeof(OrderingMode), args["mode"]));
            return new StepOutcome();
        }
    }

    /// <summary>
    /// Moves a queued bill to a new index with a bare <c>Bills.Remove</c>/<c>Bills.Insert</c> on the
    /// shared list — exactly what Nice Bill Tab's <c>HandleBillDrop</c> does, and exactly what no
    /// patch of ours can see.
    ///
    /// Stands in for a real drag because a drag is mouse input the harness cannot replay, and
    /// what matters here is not their gesture but its effect on the list: no <c>Reorder</c> call,
    /// no event, just a list in a new order the next time anything looks.
    /// </summary>
    public sealed class WbgMoveBillDirectStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgMoveBillDirect";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            foreach (string key in new[] { "slot", "toIndex" })
            {
                if (!args.TryGetValue(key, out string value) || !int.TryParse(value, out _))
                {
                    error = $"WbgMoveBillDirect requires integer '{key}'";
                    return false;
                }
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            int slot = int.Parse(args["slot"]);
            if (slot < 0 || slot >= WbgTestState.Bills.Count)
            {
                return StepOutcome.Fail(
                    $"WbgMoveBillDirect: slot {slot} out of range, {WbgTestState.Bills.Count} bills queued");
            }

            Bill bill = WbgTestState.Bills[slot];
            List<Bill> bills = bill.billStack?.Bills;
            int toIndex = int.Parse(args["toIndex"]);
            if (bills == null || toIndex < 0 || toIndex >= bills.Count)
            {
                return StepOutcome.Fail($"WbgMoveBillDirect: index {toIndex} is outside the list");
            }

            bills.Remove(bill);
            bills.Insert(toIndex, bill);
            return new StepOutcome();
        }
    }
}
