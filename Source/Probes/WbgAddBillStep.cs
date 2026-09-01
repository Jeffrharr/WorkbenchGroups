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

            bench.billStack.AddBill(bill);
            WbgTestState.Bills.Add(bill);

            return new StepOutcome();
        }
    }
}
