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
    /// Two of those five can live in a shared stack — the plain <c>Bill_Production</c>, and
    /// <c>Bill_ProductionWithUft</c> once the unfinished item is routed to the bench the pawn
    /// walked to rather than the bench that owns the list (see <c>UnfinishedItemSharing</c>). The
    /// other three cast the list's owner back to their own bench class and cannot be shared at
    /// all. So one predicate over the shape answers both questions the mod has to ask — whether a
    /// bill may join a shared stack, and whether a bench has any recipe for which grouping would
    /// do anything. Both are answered without naming a class, so modded benches are admitted
    /// automatically and mech gestators and subcore encoders are excluded automatically, because
    /// of what they make.
    ///
    /// The four fields are pinned by a Cecil test, because the gate is now only as correct as this
    /// list is current: a fifth branch added to <c>MakeNewBill</c> in a future RimWorld would let
    /// a new bill type through unnoticed.
    /// </summary>
    public static class RecipeGate
    {
        /// <summary>
        /// Whether a recipe would produce a bill that can live in a shared list — a plain
        /// <c>Bill_Production</c> or a <c>Bill_ProductionWithUft</c>.
        ///
        /// The unfinished-thing marker is deliberately *not* tested here. It was, until the
        /// unfinished item was made to follow the bench the pawn walked to; what disqualifies a
        /// recipe is a bill type that reads the list's owner as its own bench, and only the
        /// remaining three markers do that.
        /// </summary>
        public static bool MakesShareableBill(RecipeShape shape)
        {
            return !shape.MechResurrection
                && shape.GestationCycles <= 0
                && shape.FormingTicks <= 0;
        }

        /// <summary>
        /// Whether a recipe would produce a plain <c>Bill_Production</c> — shareable, and with no
        /// unfinished item to route. Reported by the eligibility census so a bench's verdict can
        /// be read against what it actually makes.
        /// </summary>
        public static bool MakesPlainProductionBill(RecipeShape shape)
        {
            return MakesShareableBill(shape) && !shape.UsesUnfinishedThing;
        }

        /// <summary>
        /// Whether a bench has any recipe worth grouping it for.
        ///
        /// Deliberately "at least one" rather than "all", because a bench can hold a mix: the
        /// machining table makes components (plain), guns (unfinished item) and nothing else, and
        /// a hypothetical bench mixing plain recipes with mech gestation would have to be admitted
        /// on the strength of the half we can share.
        ///
        /// So the recipe test also lands on the bill: <c>Patch_BillStack_AddBill</c> refuses a
        /// non-shareable bill entry into a shared stack, which is the precise condition, and this
        /// rule only asks whether grouping the bench could ever be useful. A bench with no
        /// shareable recipe at all — a mech gestator, a subcore encoder — gets no gizmo, because
        /// every bill it could hold would be refused.
        ///
        /// An empty or missing list is not groupable: nothing to share, and it keeps abstract and
        /// placeholder defs that happen to use the work table class out of the injector.
        /// </summary>
        public static bool AnyMakeShareableBill(IList<RecipeShape> shapes)
        {
            if (shapes == null)
            {
                return false;
            }

            foreach (RecipeShape shape in shapes)
            {
                if (MakesShareableBill(shape))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
