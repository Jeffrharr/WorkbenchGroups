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
        // Bill_Production. Almost every recipe in the game.
        Assert.That(RecipeGate.MakesPlainProductionBill(RecipeShape.Plain), Is.True);
        Assert.That(RecipeGate.MakesShareableBill(RecipeShape.Plain), Is.True);
    }

    [Test]
    public void An_unfinished_thing_recipe_is_shareable_but_not_plain()
    {
        // Bill_ProductionWithUft. Refused outright until the resume job was made to follow the
        // bench the pawn walked to (UnfinishedItemSharing); now shareable. Still not "plain",
        // because the eligibility census reports the two separately.
        var uft = new RecipeShape(true, false, 0, 0);
        Assert.That(RecipeGate.MakesShareableBill(uft), Is.True);
        Assert.That(RecipeGate.MakesPlainProductionBill(uft), Is.False);
    }

    [TestCase(false, 0, 0)]
    [TestCase(true, 0, 0)]
    [TestCase(false, 1, 0)]
    [TestCase(false, 0, 1)]
    public void An_unfinished_thing_marker_never_rescues_an_unshareable_one(
        bool resurrection, int gestation, int forming)
    {
        // A recipe carrying an unfinished thing *and* one of the other three markers still makes
        // the other bill type — MakeNewBill tests UsesUnfinishedThing last — so the extra marker
        // must not flip it to shareable. The first case is the control: UFT alone is shareable.
        var shape = new RecipeShape(true, resurrection, gestation, forming);
        bool onlyUft = !resurrection && gestation <= 0 && forming <= 0;
        Assert.That(RecipeGate.MakesShareableBill(shape), Is.EqualTo(onlyUft));
    }

    [Test]
    public void A_mech_resurrection_recipe_is_refused()
    {
        // Bill_ResurrectMech.
        Assert.That(RecipeGate.MakesShareableBill(new RecipeShape(false, true, 0, 0)), Is.False);
        Assert.That(RecipeGate.MakesPlainProductionBill(new RecipeShape(false, true, 0, 0)), Is.False);
    }

    [Test]
    public void A_gestation_recipe_is_refused()
    {
        // Bill_ProductionMech, on a mech gestator — excluded by what it makes, with no reference
        // to Building_MechGestator anywhere in the rule.
        Assert.That(RecipeGate.MakesShareableBill(new RecipeShape(false, false, 1, 0)), Is.False);
        Assert.That(RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, 1, 0)), Is.False);
    }

    [Test]
    public void A_forming_recipe_is_refused()
    {
        // Bill_Autonomous, on a subcore encoder.
        Assert.That(RecipeGate.MakesShareableBill(new RecipeShape(false, false, 0, 1)), Is.False);
        Assert.That(RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, 0, 1)), Is.False);
    }

    [TestCase(0, ExpectedResult = true)]
    [TestCase(-1, ExpectedResult = true)]
    public bool Non_positive_tick_and_cycle_counts_are_not_special(int value)
    {
        // MakeNewBill tests `> 0`, so a def that leaves these at 0 — or at some negative sentinel
        // — still makes a plain Bill_Production. The rule has to agree with the comparison
        // vanilla actually makes, not with "is it set".
        return RecipeGate.MakesPlainProductionBill(new RecipeShape(false, false, value, value))
            && RecipeGate.MakesShareableBill(new RecipeShape(false, false, value, value));
    }

    [Test]
    public void A_bench_with_a_plain_recipe_is_groupable()
    {
        Assert.That(
            RecipeGate.AnyMakeShareableBill(
                new[] { RecipeShape.Plain, RecipeShape.Plain }),
            Is.True);
    }

    [Test]
    public void One_shareable_recipe_among_unshareable_ones_is_enough()
    {
        // A bench mixing a plain recipe with mech gestation is admitted on the strength of the
        // half we can share; the gestation bills are refused individually at AddBill time.
        Assert.That(
            RecipeGate.AnyMakeShareableBill(
                new[]
                {
                    new RecipeShape(false, false, 1, 0),
                    RecipeShape.Plain,
                    new RecipeShape(false, false, 0, 1),
                }),
            Is.True);
    }

    [Test]
    public void A_bench_whose_every_recipe_leaves_an_unfinished_item_is_groupable()
    {
        // The sculpting table: nothing plain, every recipe a Bill_ProductionWithUft. Refused under
        // the old plain-only rule; admitted now that those bills can be shared.
        Assert.That(
            RecipeGate.AnyMakeShareableBill(
                new[] { new RecipeShape(true, false, 0, 0), new RecipeShape(true, false, 0, 0) }),
            Is.True);
    }

    [Test]
    public void A_bench_whose_every_recipe_is_unshareable_is_not_groupable()
    {
        // The mech gestator, excluded by what it makes rather than by its class name.
        Assert.That(
            RecipeGate.AnyMakeShareableBill(
                new[] { new RecipeShape(false, false, 1, 0), new RecipeShape(false, false, 0, 1) }),
            Is.False);
    }

    [Test]
    public void A_bench_with_no_recipes_is_not_groupable()
    {
        Assert.That(RecipeGate.AnyMakeShareableBill(new RecipeShape[0]), Is.False);
    }

    [Test]
    public void A_missing_recipe_list_is_not_groupable()
    {
        Assert.That(RecipeGate.AnyMakeShareableBill(null!), Is.False);
    }
}
