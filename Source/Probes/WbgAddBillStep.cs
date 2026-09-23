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

            if (args.TryGetValue("repeat", out string repeat)
                && repeat != "count" && repeat != "target" && repeat != "forever")
            {
                error = $"WbgAddBill: 'repeat' must be count, target or forever (got '{repeat}')";
                return false;
            }

            if (args.TryGetValue("slowCount", out string slow) && !bool.TryParse(slow, out _))
            {
                error = $"WbgAddBill: 'slowCount' is not a boolean (got '{slow}')";
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
            int count = args.TryGetValue("count", out string raw) ? int.Parse(raw) : 1;
            string repeat = args.TryGetValue("repeat", out string rawRepeat) ? rawRepeat : "count";
            if (repeat == "target")
            {
                // "Do until you have X", with X taken from 'count'. The stock-aware ordering is
                // defined over these orders, so a scenario has to be able to make one.
                bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                bill.targetCount = count;
            }
            else if (repeat == "forever")
            {
                bill.repeatMode = BillRepeatModeDefOf.Forever;
            }
            else
            {
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                bill.repeatCount = count;
            }

            // Forces vanilla's slow product count. The fast path reads the map's resource
            // counter, which only sees things in stockpiles, so meals a scenario drops on bare
            // floor would count as zero. "Include equipped" is a filter setting with no effect on
            // meals but it takes CountProducts off the fast path, onto the map-wide walk that
            // counts everything spawned — which is also the path whose cost the count cache
            // exists to bound, so a profiled run with this set measures the expensive case.
            if (args.TryGetValue("slowCount", out string slow) && bool.Parse(slow))
            {
                bill.includeEquipped = true;
            }

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
