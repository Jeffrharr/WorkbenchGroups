using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

/// <summary>
/// The matcher behind the FinishUftJob transpiler, over plain strings standing in for
/// instructions: "S" is <c>ldfld Bill::billStack</c>, "G" is <c>ldfld BillStack::billGiver</c>.
/// The Cecil test in ApiCompatibilityTests runs the same matcher over the real vanilla IL.
/// </summary>
[TestFixture]
public class IlPairRewriteTests
{
    private static List<int> Find(params string[] code) =>
        IlPairRewrite.FindPairs(code, c => c == "S", c => c == "G");

    [Test]
    public void Finds_each_adjacent_pair()
    {
        // The shape of FinishUftJob today: ldarg.2, S, G used twice.
        Assert.That(Find("a", "S", "G", "x", "a", "S", "G", "c"), Is.EqualTo(new[] { 1, 5 }));
    }

    [Test]
    public void Ignores_a_pair_that_is_not_adjacent()
    {
        // A billStack read used for anything else — say, FirstShouldDoNow — must not be rewritten.
        Assert.That(Find("S", "x", "G"), Is.Empty);
    }

    [Test]
    public void A_lone_first_half_at_the_end_is_not_a_match()
    {
        Assert.That(Find("x", "S"), Is.Empty);
    }

    [Test]
    public void Matches_do_not_overlap()
    {
        // With a pattern whose halves can be the same element, "SSS" is one match, not two: the
        // second would rewrite an instruction the first already consumed.
        var starts = IlPairRewrite.FindPairs(new[] { "S", "S", "S" }, c => c == "S", c => c == "S");
        Assert.That(starts, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void Empty_and_null_code_have_no_matches()
    {
        Assert.That(Find(), Is.Empty);
        Assert.That(IlPairRewrite.FindPairs<string>(null!, c => true, c => true), Is.Empty);
    }

    [TestCase(2, 2, ExpectedResult = true)]
    [TestCase(1, 2, ExpectedResult = false)]
    [TestCase(3, 2, ExpectedResult = false)]
    [TestCase(0, 2, ExpectedResult = false)]
    [TestCase(0, 0, ExpectedResult = false)]
    public bool Rewrites_only_on_the_exact_expected_count(int found, int expected)
    {
        // All or nothing: a partial rewrite would send the pawn to one bench while the haul-off
        // clears another. An expected count of zero is never "apply" — there would be nothing to
        // redirect, and RedirectInstalled would lie.
        return IlPairRewrite.ShouldRewrite(found, expected);
    }
}
