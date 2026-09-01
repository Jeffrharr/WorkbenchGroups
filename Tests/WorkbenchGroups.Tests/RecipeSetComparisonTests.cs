using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class RecipeSetComparisonTests
{
    [Test]
    public void Order_does_not_matter()
    {
        // The case this rule exists for: an electric and a fueled stove list the same recipes,
        // often in a different order, and players expect those two to link.
        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "CookMealSimple", "CookMealFine", "ButcherCorpseFlesh" },
                new[] { "ButcherCorpseFlesh", "CookMealSimple", "CookMealFine" }),
            Is.True);
    }

    [Test]
    public void A_missing_recipe_breaks_the_match()
    {
        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "CookMealSimple", "CookMealFine" },
                new[] { "CookMealSimple" }),
            Is.False);
    }

    [Test]
    public void An_extra_recipe_breaks_the_match()
    {
        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "CookMealSimple" },
                new[] { "CookMealSimple", "CookMealLavish" }),
            Is.False);
    }

    [Test]
    public void Same_length_but_disjoint_does_not_match()
    {
        // Guards the cheap length check from being mistaken for the whole comparison.
        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "MakeApparel", "MakeStuff" },
                new[] { "CookMealSimple", "CookMealFine" }),
            Is.False);
    }

    [Test]
    public void Duplicates_are_counted_not_collapsed()
    {
        // Treating these as equal would hide a genuine duplicate-recipe def error behind our
        // feature, so the multiset comparison is deliberate rather than incidental.
        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "CookMealSimple", "CookMealSimple" },
                new[] { "CookMealSimple", "CookMealFine" }),
            Is.False);

        Assert.That(
            RecipeSetComparison.SameRecipeSet(
                new[] { "CookMealSimple", "CookMealSimple" },
                new[] { "CookMealSimple", "CookMealSimple" }),
            Is.True);
    }

    [Test]
    public void Two_benches_with_no_recipes_match_each_other()
    {
        Assert.That(RecipeSetComparison.SameRecipeSet(new string[0], new string[0]), Is.True);
    }

    [Test]
    public void Null_is_treated_as_empty_rather_than_throwing()
    {
        // AllRecipes can legitimately be null on a def; a link gizmo must not throw while drawing.
        Assert.That(RecipeSetComparison.SameRecipeSet(null!, new string[0]), Is.True);
        Assert.That(RecipeSetComparison.SameRecipeSet(null!, new[] { "CookMealSimple" }), Is.False);
    }
}
