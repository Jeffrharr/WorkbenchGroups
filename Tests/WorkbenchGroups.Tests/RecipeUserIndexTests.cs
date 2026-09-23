using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

/// <summary>
/// Covers the replacement for <c>ThingDef.AllRecipes</c>.
///
/// The bar these tests hold is not "does it produce a sensible list" but "does it produce the
/// same list vanilla's getter would have". The whole point of the index is that it reproduces a
/// vanilla answer without touching the vanilla cache, so a divergence here is a silent behaviour
/// change in which benches are groupable — the kind that only shows up as a missing gizmo in
/// someone else's modlist.
/// </summary>
[TestFixture]
public class RecipeUserIndexTests
{
    private static RecipeEntry Recipe(string name, params string[] users) =>
        new RecipeEntry(name, RecipeShape.Plain, users);

    private static BenchRecipe Own(string name) => new BenchRecipe(name, RecipeShape.Plain);

    private static string[] NamesOn(
        Dictionary<string, List<BenchRecipe>> index,
        string bench,
        params BenchRecipe[] own) =>
        RecipeUserIndex.RecipesOn(index, bench, own).Select(r => r.DefName).ToArray();

    [Test]
    public void RecipeNamingABench_LandsOnThatBench()
    {
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove") });

        Assert.That(NamesOn(index, "Stove"), Is.EqualTo(new[] { "MakeStew" }));
    }

    [Test]
    public void RecipeNamingSeveralBenches_LandsOnEachOfThem()
    {
        // The common modded shape: one recipe added to every stove variant at once.
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove", "FueledStove") });

        Assert.That(NamesOn(index, "Stove"), Is.EqualTo(new[] { "MakeStew" }));
        Assert.That(NamesOn(index, "FueledStove"), Is.EqualTo(new[] { "MakeStew" }));
    }

    [Test]
    public void BenchNobodyNames_HasOnlyItsOwnRecipes()
    {
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove") });

        Assert.That(NamesOn(index, "TailoringBench", Own("SewShirt")),
            Is.EqualTo(new[] { "SewShirt" }));
    }

    [Test]
    public void OwnRecipesComeFirst_ThenNominatedOnes_AsVanillaAppendsThem()
    {
        // Vanilla's getter copies def.recipes first, then walks the recipe database. Callers only
        // ask set-shaped questions today, but a list that differs in order from the one it
        // replaces is exactly the difference that surfaces later in somebody's UI.
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove"), Recipe("MakeSoup", "Stove") });

        Assert.That(NamesOn(index, "Stove", Own("Grill"), Own("Roast")),
            Is.EqualTo(new[] { "Grill", "Roast", "MakeStew", "MakeSoup" }));
    }

    [Test]
    public void RecipeBothDeclaredAndNominated_AppearsTwice_JustAsVanillaWould()
    {
        // Vanilla does not deduplicate: a recipe listed in def.recipes whose recipeUsers also
        // names that def is added twice. Reproduced rather than corrected, because the index is
        // meant to be indistinguishable from the getter, not better than it.
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove") });

        Assert.That(NamesOn(index, "Stove", Own("MakeStew")),
            Is.EqualTo(new[] { "MakeStew", "MakeStew" }));
    }

    [Test]
    public void ShapesSurviveTheInversion()
    {
        // The shape is what the eligibility gate actually judges; carrying the name but losing the
        // shape would make every bench look groupable.
        var uft = new RecipeEntry("SewShirt", new RecipeShape(true, false, 0, 0), new[] { "TailoringBench" });
        var index = RecipeUserIndex.Build(new[] { uft });

        var shapes = RecipeUserIndex.RecipesOn(index, "TailoringBench", null)
            .Select(r => r.Shape).ToList();

        Assert.That(shapes, Has.Count.EqualTo(1));
        Assert.That(shapes[0].UsesUnfinishedThing, Is.True);
        Assert.That(RecipeGate.MakesPlainProductionBill(shapes[0]), Is.False);
    }

    [Test]
    public void NullRecipeUsers_ContributesNothing()
    {
        // recipeUsers is null on the great majority of recipes, so this is the common path.
        var index = RecipeUserIndex.Build(new[] { new RecipeEntry("MakeStew", RecipeShape.Plain, null!) });

        Assert.That(index, Is.Empty);
    }

    [Test]
    public void EmptyRecipeUsersList_ContributesNothing()
    {
        // recipeUsers is null far more often than it is empty, but XML can produce either.
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew") });

        Assert.That(index, Is.Empty);
    }

    [Test]
    public void NullOrEmptyUserName_IsSkippedRatherThanIndexed()
    {
        // A malformed patch can leave a null in recipeUsers. Indexing it would create a bucket
        // no bench can ever match, which is harmless, but an empty-string bucket could collide
        // with a def whose name failed to resolve.
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", null!, "", "Stove") });

        Assert.That(index.Keys, Is.EqualTo(new[] { "Stove" }));
    }

    [Test]
    public void NullRecipeDatabase_BuildsAnEmptyIndexRatherThanThrowing()
    {
        Assert.That(RecipeUserIndex.Build(null), Is.Empty);
    }

    [Test]
    public void LookupWithNullIndex_StillReturnsTheBenchsOwnRecipes()
    {
        // Ordering insurance: if the index is ever consulted before it is built, the bench should
        // degrade to its declared recipes rather than to nothing. Nothing groupable would be a
        // silent, total loss of the gizmo.
        Assert.That(NamesOn(null!, "Stove", Own("Grill")), Is.EqualTo(new[] { "Grill" }));
    }

    [Test]
    public void UnknownBench_ReturnsEmptyRatherThanNull()
    {
        var index = RecipeUserIndex.Build(new[] { Recipe("MakeStew", "Stove") });

        Assert.That(RecipeUserIndex.RecipesOn(index, "NoSuchBench", null), Is.Empty);
    }
}
