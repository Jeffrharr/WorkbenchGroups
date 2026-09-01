using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups
{
    /// <summary>
    /// Decides which benches and which bills this mod is willing to touch.
    ///
    /// The exclusions here are not conservatism for its own sake. Sharing a bill list works
    /// because vanilla's job code follows the bench a pawn walked to rather than the bench that
    /// owns the bill — but a handful of vanilla types break that assumption by hard-casting
    /// <c>bill.billStack.billGiver</c> to their own concrete class. Those cases do not degrade,
    /// they throw every frame, so they are refused at link time instead.
    ///
    /// What is refused is decided from the bench's <em>recipes</em>, not its class: see
    /// <see cref="RecipeGate"/>. The class only enters as a coarse safety net and as the player's
    /// escape hatch.
    /// </summary>
    public static class BenchEligibility
    {
        /// <summary>Last-parsed exclusion setting, and the raw string it was parsed from.</summary>
        private static string cachedExclusionSource;
        private static string[] cachedExclusions = new string[0];

        /// <summary>
        /// Classes that are refused whatever their recipes say.
        ///
        /// A safety net rather than the primary rule. <c>Building_WorkTableAutonomous</c> and its
        /// descendant <c>Building_MechGestator</c> cast a bill's owner back to their own class
        /// (<c>Bill_Autonomous.WorkTable</c>, <c>Bill_Mech.Gestator</c>), so a group anchored on
        /// one throws an InvalidCastException every frame rather than degrading. Their recipes
        /// already give them away — <c>formingTicks</c> and <c>gestationCycles</c> — so this line
        /// is redundant today. It is here for the future vanilla subclass whose recipes do not.
        ///
        /// Assignability, not exact type, because the point is to catch descendants.
        /// </summary>
        private static bool IsAlwaysRefusedClass(Type thingClass)
        {
            return thingClass != null
                && typeof(Building_WorkTableAutonomous).IsAssignableFrom(thingClass);
        }

        /// <summary>
        /// The recipe shapes of a def, in the form <see cref="RecipeGate"/> reasons about.
        /// </summary>
        private static List<RecipeShape> ShapesOf(ThingDef def)
        {
            List<RecipeDef> recipes = def?.AllRecipes;
            if (recipes == null)
            {
                return null;
            }

            List<RecipeShape> shapes = new List<RecipeShape>(recipes.Count);
            foreach (RecipeDef recipe in recipes)
            {
                shapes.Add(new RecipeShape(
                    recipe.UsesUnfinishedThing,
                    recipe.mechResurrection,
                    recipe.gestationCycles,
                    recipe.formingTicks));
            }

            return shapes;
        }

        /// <summary>Whether a Thing is a work table we can safely group.</summary>
        public static bool IsGroupableBench(Thing thing)
        {
            return thing is Building_WorkTable && IsGroupableDef(thing.def);
        }

        /// <summary>
        /// Whether a def's benches can be grouped — the one rule, used both for comp injection at
        /// startup and for the link-time check, so the two can never disagree about what is
        /// groupable. (A bench that got a comp but is then refused at link time would show a gizmo
        /// that always fails.)
        ///
        /// The class test is deliberately an <c>is Building_WorkTable</c> check rather than the
        /// exact-type whitelist this used to be: what makes a bench dangerous is the bills it can
        /// hold, and <see cref="RecipeGate"/> answers that from the def's recipes without naming
        /// any class. See <c>RecipeGate</c> for why that is sound, and <c>DESIGN.md</c> for the
        /// residual risk it cannot cover.
        ///
        /// Note this admits benches that can also make unshareable things — a machining table
        /// makes both components and guns. That is intentional; the unshareable half is refused
        /// per bill by <c>Patch_BillStack_AddBill</c>, not per bench.
        /// </summary>
        public static bool IsGroupableDef(ThingDef def)
        {
            Type thingClass = def?.thingClass;
            if (thingClass == null || !typeof(Building_WorkTable).IsAssignableFrom(thingClass))
            {
                return false;
            }

            if (IsAlwaysRefusedClass(thingClass) || IsExcludedByPlayer(thingClass))
            {
                return false;
            }

            return RecipeGate.AnyMakePlainProductionBill(ShapesOf(def));
        }

        /// <summary>
        /// Whether the player has named this class in the exclusion setting.
        ///
        /// The parse is cached against the raw string because this is asked once per def at
        /// startup and again on every link; re-splitting a string the player edits once a year is
        /// the kind of waste that shows up in someone's load-time profile.
        /// </summary>
        private static bool IsExcludedByPlayer(Type thingClass)
        {
            string raw = WorkbenchGroupsMod.Settings?.excludedBenchClasses;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            if (raw != cachedExclusionSource)
            {
                cachedExclusionSource = raw;
                cachedExclusions = ClassExclusionList.Parse(raw);
            }

            return ClassExclusionList.Excludes(cachedExclusions, thingClass.FullName, thingClass.Name);
        }

        /// <summary>
        /// Whether a bill can live in a shared stack.
        ///
        /// Only plain <c>Bill_Production</c> qualifies. <c>Bill_ProductionWithUft</c> is the
        /// painful exclusion: an unfinished thing is bound to the bill, and both
        /// <c>WorkGiver_DoBill.FinishUftJob</c> and <c>HaulAIUtility</c> resolve it through
        /// <c>bill.billStack.billGiver</c>. Shared, a pawn who started at one bench is sent to
        /// the anchor to finish, and worse, an unfinished item left on a non-anchor bench fails
        /// the "is it inside the owner's footprint" test forever and can never be hauled away.
        /// </summary>
        public static bool IsShareableBill(Bill bill)
        {
            return bill != null && bill.GetType() == typeof(Bill_Production);
        }

        /// <summary>Recipe defNames for a bench's def, for the same-recipe-set link rule.</summary>
        public static string[] RecipeNamesOf(Building_WorkTable bench)
        {
            List<RecipeDef> recipes = bench?.def?.AllRecipes;
            if (recipes == null)
            {
                return new string[0];
            }

            string[] names = new string[recipes.Count];
            for (int i = 0; i < recipes.Count; i++)
            {
                names[i] = recipes[i].defName;
            }

            return names;
        }

        /// <summary>Whether two benches make the same things, and so may be linked.</summary>
        public static bool SameRecipes(Building_WorkTable a, Building_WorkTable b)
        {
            return RecipeSetComparison.SameRecipeSet(RecipeNamesOf(a), RecipeNamesOf(b));
        }

        /// <summary>
        /// Whether every bill on a bench could move into a shared stack. Reported before linking
        /// so the refusal can name the offending bill rather than half-linking and failing later.
        /// </summary>
        public static bool AllBillsShareable(Building_WorkTable bench, out Bill offender)
        {
            offender = null;

            BillStack stack = bench?.billStack;
            if (stack == null)
            {
                return true;
            }

            foreach (Bill bill in stack.Bills)
            {
                if (!IsShareableBill(bill))
                {
                    offender = bill;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Maps RimWorld's repeat-mode def onto the pure core's value.
        ///
        /// Fails open: an unrecognised mode (a future vanilla addition, or one added by another
        /// mod) is treated as Forever, which never blocks a pawn. The overshoot guard only ever
        /// tightens vanilla's answer, so failing open means falling back to vanilla behaviour.
        /// </summary>
        public static RepeatModeCode RepeatModeOf(Bill_Production bill)
        {
            if (bill.repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                return RepeatModeCode.RepeatCount;
            }

            if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                return RepeatModeCode.TargetCount;
            }

            return RepeatModeCode.Forever;
        }
    }
}
