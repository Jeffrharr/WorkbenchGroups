using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// The four <c>RecipeDef</c> fields <c>BillUtility.MakeNewBill</c> branches on, lifted out of
    /// Verse so the rule that reads them can be tested offline.
    ///
    /// Copied rather than referenced on purpose: the gate is a statement about recipes, and a
    /// struct of primitives makes it obvious that nothing else about the def can influence the
    /// answer.
    /// </summary>
    public readonly struct RecipeShape
    {
        public readonly bool UsesUnfinishedThing;
        public readonly bool MechResurrection;
        public readonly int GestationCycles;
        public readonly int FormingTicks;

        public RecipeShape(
            bool usesUnfinishedThing,
            bool mechResurrection,
            int gestationCycles,
            int formingTicks)
        {
            UsesUnfinishedThing = usesUnfinishedThing;
            MechResurrection = mechResurrection;
            GestationCycles = gestationCycles;
            FormingTicks = formingTicks;
        }

        /// <summary>A recipe with none of the special markers — the common case.</summary>
        public static RecipeShape Plain => new RecipeShape(false, false, 0, 0);
    }

    /// <summary>
    /// Decides whether a bench's recipes are ones this mod can share, without knowing anything
    /// about the bench's C# class.
    ///
    /// This replaces an earlier whitelist of two concrete bench types. The whitelist was wrong in
    /// both directions: it excluded every modded bench with a custom <c>thingClass</c> — which is
    /// most of the interesting ones — and it had already silently excluded stoves, because those
    /// are <c>Building_WorkTable_HeatPush</c> and nobody noticed until a live test failed.
    ///
    /// The insight that lets the class drop out entirely is that <c>BillUtility.MakeNewBill</c>
    /// picks the <c>Bill</c> subclass from the <c>RecipeDef</c> alone:
    ///
    /// <code>
    /// if (recipe.UsesUnfinishedThing) return new Bill_ProductionWithUft(...);
    /// if (recipe.mechResurrection)    return new Bill_ResurrectMech(...);
    /// if (recipe.gestationCycles > 0) return new Bill_ProductionMech(...);
    /// if (recipe.formingTicks > 0)    return new Bill_Autonomous(...);
    /// return new Bill_Production(...);
    /// </code>
    ///
    /// Only a plain <c>Bill_Production</c> can live in a shared stack (see
    /// <c>BenchEligibility.IsShareableBill</c>), so this one predicate answers both questions the
    /// mod has to ask — whether a bill may join a shared stack, and whether a bench has any
    /// recipe for which grouping would do anything. Both are answered without naming a class, so
    /// modded benches are admitted automatically and mech gestators and subcore encoders are
    /// excluded automatically, because of what they make.
    ///
    /// The four fields are pinned by a Cecil test, because the gate is now only as correct as this
    /// list is current: a fifth branch added to <c>MakeNewBill</c> in a future RimWorld would let
    /// a new bill type through unnoticed.
    /// </summary>
    public static class RecipeGate
    {
        /// <summary>Whether a recipe would produce a plain <c>Bill_Production</c>.</summary>
        public static bool MakesPlainProductionBill(RecipeShape shape)
        {
            return !shape.UsesUnfinishedThing
                && !shape.MechResurrection
                && shape.GestationCycles <= 0
                && shape.FormingTicks <= 0;
        }

        /// <summary>
        /// Whether a bench has any recipe worth grouping it for.
        ///
        /// Deliberately "at least one" rather than "all", and that choice is the whole design.
        /// "All" is the tempting rule — it guarantees no unshareable bill can ever appear on a
        /// grouped bench — but measured against the real def database it excludes every crafting
        /// bench in the game. Apparel, weapons, armour and sculptures all use unfinished things,
        /// so tailoring benches, smithies, the machining table, the fabrication bench, the
        /// sculpting table and the crafting spot would all lose the gizmo. That trades one gap for
        /// a far larger one.
        ///
        /// So the recipe test moves to where the danger actually is: the bill.
        /// <c>Patch_BillStack_AddBill</c> refuses a non-shareable bill entry into a shared stack,
        /// which is the precise condition, and this rule only asks whether grouping the bench
        /// could ever be useful. A bench with no plain recipe at all — a mech gestator, a subcore
        /// encoder — gets no gizmo, because every bill it could hold would be refused.
        ///
        /// An empty or missing list is not groupable: nothing to share, and it keeps abstract and
        /// placeholder defs that happen to use the work table class out of the injector.
        /// </summary>
        public static bool AnyMakePlainProductionBill(IList<RecipeShape> shapes)
        {
            if (shapes == null)
            {
                return false;
            }

            foreach (RecipeShape shape in shapes)
            {
                if (MakesPlainProductionBill(shape))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
