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
    /// </summary>
    public static class BenchEligibility
    {
        /// <summary>
        /// Bench classes a group may contain.
        ///
        /// A whitelist of exact types rather than an <c>is Building_WorkTable</c> check, because
        /// the dangerous cases are subclasses: <c>Building_WorkTableAutonomous</c> and its
        /// descendant <c>Building_MechGestator</c> cast the bill's owner back to their own class
        /// (<c>Bill_Autonomous.WorkTable</c>, <c>Bill_Mech.Gestator</c>), so a group anchored on
        /// one throws an InvalidCastException every frame rather than degrading.
        ///
        /// <c>Building_WorkTable_HeatPush</c> is whitelisted because it is behaviourally identical
        /// for our purposes — its only override is <c>UsedThisTick</c>, pushing heat — and because
        /// excluding it would exclude every stove and smithy, which is the case players want this
        /// mod for in the first place.
        ///
        /// The cost is that a modded bench subclassing <c>Building_WorkTable</c> for cosmetics is
        /// excluded until whitelisted. That is the right way round: a missing gizmo is a feature
        /// request, a per-frame exception is a bug report.
        /// </summary>
        private static readonly HashSet<Type> GroupableBenchClasses = new HashSet<Type>
        {
            typeof(Building_WorkTable),
            typeof(Building_WorkTable_HeatPush),
        };

        /// <summary>Whether a Thing is a work table we can safely group.</summary>
        public static bool IsGroupableBench(Thing thing)
        {
            return thing is Building_WorkTable && GroupableBenchClasses.Contains(thing.GetType());
        }

        /// <summary>Whether a def's benches can be grouped, for comp injection at startup.</summary>
        public static bool IsGroupableDef(ThingDef def)
        {
            return def?.thingClass != null && GroupableBenchClasses.Contains(def.thingClass);
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
