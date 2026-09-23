using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorldTestHarness.Mod;
using RimWorldTestHarness.Mod.Steps;
using RimWorldTestHarness.Shared;
using RimWorldTestHarness.Shared.Steps;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Checks the eligibility rule against the real def database, with every DLC and mod the run
    /// loaded.
    ///
    /// This exists because the rule moved from naming two classes to reading recipes, and the only
    /// thing that can confirm a recipe-shaped rule admits the benches players actually use is the
    /// loaded def database. An offline test proves the boolean logic; it cannot know that apparel
    /// recipes carry an unfinished thing and so cannot notice the rule quietly excluding every
    /// tailoring bench in the game.
    ///
    /// Logs a census of every work-table def and its verdict, so a failure — or a future RimWorld
    /// update — is diagnosable from the run's log without a second run.
    /// </summary>
    public sealed class WbgAssertEligibilityStep : IStepSpec, IStepAction
    {
        public const string TypeName = "WbgAssertEligibility";

        public string Type => TypeName;

        // Reads defs only; nothing on the map changes.
        public ScenarioResidue Residue => ScenarioResidue.None;

        public bool LiveCallable => true;

        public bool TryValidate(IReadOnlyDictionary<string, string> args, out string error)
        {
            error = null;

            if (!args.ContainsKey("groupable") && !args.ContainsKey("notGroupable"))
            {
                error = "WbgAssertEligibility requires 'groupable' and/or 'notGroupable' "
                    + "(comma-separated ThingDef names)";
                return false;
            }

            return true;
        }

        public StepOutcome Execute(IReadOnlyDictionary<string, string> args, StepContext ctx)
        {
            LogCensus();

            List<string> failures = new List<string>();
            List<string> absent = new List<string>();

            CheckAll(args, "groupable", expected: true, failures, absent);
            CheckAll(args, "notGroupable", expected: false, failures, absent);

            // A def missing because its DLC is not installed is not a failure — the same scenario
            // has to pass on a machine without Biotech — but it is reported, so a run that checked
            // nothing cannot look like a run that checked everything.
            if (absent.Count > 0)
            {
                Log.Message($"[Workbench Groups] Eligibility check skipped absent defs: {string.Join(", ", absent)}");
            }

            if (failures.Count > 0)
            {
                return StepOutcome.Fail("WbgAssertEligibility: " + string.Join("; ", failures));
            }

            return new StepOutcome();
        }

        private static void CheckAll(
            IReadOnlyDictionary<string, string> args,
            string key,
            bool expected,
            List<string> failures,
            List<string> absent)
        {
            if (!args.TryGetValue(key, out string raw))
            {
                return;
            }

            foreach (string defName in Split(raw))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    absent.Add(defName);
                }
                else if (BenchEligibility.IsGroupableDef(def) != expected)
                {
                    failures.Add(expected
                        ? $"{defName} should be groupable and is not"
                        : $"{defName} should not be groupable and is");
                }
            }
        }

        private static IEnumerable<string> Split(string raw)
        {
            return raw.Split(',')
                .Select(piece => piece.Trim())
                .Where(piece => piece.Length > 0);
        }

        /// <summary>
        /// Every def whose thingClass is a work table, with its verdict and the recipe counts the
        /// verdict came from. Ordered so two runs are diffable.
        /// </summary>
        private static void LogCensus()
        {
            StringBuilder census = new StringBuilder("[Workbench Groups] Bench eligibility census:");

            IEnumerable<ThingDef> benches = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.thingClass != null
                    && typeof(Building_WorkTable).IsAssignableFrom(def.thingClass))
                .OrderBy(def => def.defName);

            foreach (ThingDef def in benches)
            {
                List<RecipeDef> recipes = def.AllRecipes ?? new List<RecipeDef>();
                int shareable = recipes.Count(recipe => RecipeGate.MakesShareableBill(ShapeOf(recipe)));
                int plain = recipes.Count(recipe => RecipeGate.MakesPlainProductionBill(ShapeOf(recipe)));

                census.Append($"\n  {(BenchEligibility.IsGroupableDef(def) ? "yes" : " no")}  ")
                    .Append($"{def.defName} ({def.thingClass.Name}) ")
                    .Append($"{shareable}/{recipes.Count} shareable recipes, {plain} plain");
            }

            Log.Message(census.ToString());
        }

        /// <summary>
        /// The recipe's shape, judged by the same <see cref="RecipeGate"/> the mod uses. This probe
        /// used to keep its own copy of the rule, and the copy went stale the moment unfinished-item
        /// recipes became shareable — the census then contradicted the verdict printed beside it.
        /// </summary>
        private static RecipeShape ShapeOf(RecipeDef recipe)
        {
            return new RecipeShape(
                recipe.UsesUnfinishedThing,
                recipe.mechResurrection,
                recipe.gestationCycles,
                recipe.formingTicks);
        }
    }
}
