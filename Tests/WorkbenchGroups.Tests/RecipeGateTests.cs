using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

/// <summary>
/// The gate that replaced the two-entry bench whitelist. Each case names the vanilla bill type
/// the shape would produce, because that — not the bench's class — is what the rule is really
/// about.
/// </summary>
[TestFixture]
public class RecipeGateTests
{
    [Test]
    public void A_plain_recipe_makes_a_plain_production_bill()
    {
        // Bill_Production. Almost every recipe in the game, and the only kind we can share.
        Assert.That(RecipeGate.MakesPlainProductionBill(RecipeShape.Plain), Is.True);
    }

    [Test]
    public void An_unfinished_thing_recipe_is_refused()
    {
        // Bill_ProductionWithUft — the painful one. The unfinished item is bound to the bill and
        // resolved through billStack.billGiver, so sharing strands it on a non-anchor bench.
        Assert.That(
            RecipeGate.MakesPlainProductionBill(new RecipeShape(true, false, 0, 0)),
            Is.False);
    }

    [Test]
    public void A_mech_resurrection_recipe_is_refused()
    {
        // Bill_ResurrectMech.
        Assert.That(
            RecipeGate.MakesPlainProductionBill(new RecipeShape(false, true, 0, 0)),
            Is.False);
    }

    [Test]
    public void A_gestation_recipe_is_refused()
    {
        // Bill_ProductionMech, on a mech gestator — excluded by what it makes, with no reference
        // to Building_MechGestator anywhere in the rule.
        Assert.That(
            RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, 1, 0)),
            Is.False);
    }

    [Test]
    public void A_forming_recipe_is_refused()
    {
        // Bill_Autonomous, on a subcore encoder.
        Assert.That(
            RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, 0, 1)),
            Is.False);
    }

    [TestCase(0, ExpectedResult = true)]
    [TestCase(-1, ExpectedResult = true)]
    public bool Non_positive_tick_and_cycle_counts_are_not_special(int value)
    {
        // MakeNewBill tests `> 0`, so a def that leaves these at 0 — or at some negative sentinel
        // — still makes a plain Bill_Production. The rule has to agree with the comparison
        // vanilla actually makes, not with "is it set".
        return RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, value, value));
    }

    [Test]
    public void A_bench_with_a_plain_recipe_is_groupable()
    {
        Assert.That(
            RecipeGate.AnyMakePlainProductionBill(
                new[] { RecipeShape.Plain, RecipeShape.Plain }),
            Is.True);
    }

    [Test]
    public void One_plain_recipe_among_unshareable_ones_is_enough()
    {
        // The machining table: guns leave an unfinished item behind, components do not. Refusing
        // the whole bench would cost the mod every crafting bench in the game, so the bench is
        // admitted and the gun bills are refused individually at AddBill time.
        Assert.That(
            RecipeGate.AnyMakePlainProductionBill(
                new[]
                {
                    new RecipeShape(true, false, 0, 0),
                    RecipeShape.Plain,
                    new RecipeShape(true, false, 0, 0),
                }),
            Is.True);
    }

    [Test]
    public void A_bench_whose_every_recipe_is_unshareable_is_not_groupable()
    {
        // The mech gestator, excluded by what it makes rather than by its class name.
        Assert.That(
            RecipeGate.AnyMakePlainProductionBill(
                new[] { new RecipeShape(false, false, 1, 0), new RecipeShape(false, false, 0, 1) }),
            Is.False);
    }

    [Test]
    public void A_bench_with_no_recipes_is_not_groupable()
    {
        Assert.That(RecipeGate.AnyMakePlainProductionBill(new RecipeShape[0]), Is.False);
    }

    [Test]
    public void A_missing_recipe_list_is_not_groupable()
    {
        Assert.That(RecipeGate.AnyMakePlainProductionBill(null!), Is.False);
    }
}
