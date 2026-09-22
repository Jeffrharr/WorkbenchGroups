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
    /// Queues one bill onto the linked group, exactly as the bills tab would.
    ///
    /// Adds through <c>BillStack.AddBill</c> on the first tracked bench, which after linking is a
    /// pointer to the group's shared stack — so this also incidentally proves the field swap is
    /// installed, since a failed link would put the bill somewhere the other benches cannot see.
    /// </summary>
    public sealed class WbgAddBillStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgAddBill";

        public string Type => TypeName;

        public ScenarioResidue Residue => ScenarioResidue.NewMap;

        public bool LiveCallable => false;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.TryGetValue("recipe", out string recipe) || string.IsNullOrWhiteSpace(recipe))
            {
                error = "WbgAddBill requires 'recipe' (a RecipeDef name)";
                return false;
            }

            if (args.TryGetValue("count", out string count) && !int.TryParse(count, out _))
            {
                error = $"WbgAddBill: 'count' is not a number (got '{count}')";
                return false;
            }

            if (args.TryGetValue("suspended", out string suspended) && !bool.TryParse(suspended, out _))
            {
                error = $"WbgAddBill: 'suspended' is not a boolean (got '{suspended}')";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            if (WbgTestState.Benches.Count == 0)
            {
                return StepOutcome.Fail("WbgAddBill: no benches tracked — run WbgLinkBenches first");
            }

            string recipeName = args["recipe"];
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeName);
            if (recipe == null)
            {
                return StepOutcome.Fail($"WbgAddBill: no RecipeDef named '{recipeName}'");
            }

            Building_WorkTable bench = WbgTestState.Benches[0];
            if (!bench.def.AllRecipes.Contains(recipe))
            {
                return StepOutcome.Fail(
                    $"WbgAddBill: {bench.def.defName} cannot perform '{recipeName}'");
            }

            Bill_Production bill = (Bill_Production)recipe.MakeNewBill();
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = args.TryGetValue("count", out string raw) ? int.Parse(raw) : 1;

            // Optional rename, which is what lets a scenario build the *tightest* row on purpose
            // rather than hoping the longest vanilla recipe name happens to be long enough. Row
            // annotations are drawn over vanilla's label, so how much label there is to run into
            // is the whole question, and a capture against a comfortable label proves nothing
            // about a cramped one. Players rename bills, so this is a real row, not a synthetic.
            if (args.TryGetValue("label", out string label) && !string.IsNullOrWhiteSpace(label))
            {
                bill.RenamableLabel = label;
            }

            // Vanilla stamps SUSPENDED across the row's centre on a 140x40 plate. Anything else
            // drawn on the row has to be photographed against it rather than reasoned about.
            if (args.TryGetValue("suspended", out string suspended) && bool.Parse(suspended))
            {
                bill.suspended = true;
            }

            bench.billStack.AddBill(bill);
            WbgTestState.Bills.Add(bill);

            return new StepOutcome();
        }
    }
}
