using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class ClassExclusionListTests
{
    [Test]
    public void Entries_are_split_and_trimmed()
    {
        Assert.That(
            ClassExclusionList.Parse(" Building_Foo , Building_Bar "),
            Is.EqualTo(new[] { "Building_Foo", "Building_Bar" }));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    [TestCase(",,")]
    public void Nothing_meaningful_parses_to_nothing(string? raw)
    {
        Assert.That(ClassExclusionList.Parse(raw!), Is.Empty);
    }

    [Test]
    public void Newlines_and_semicolons_separate_too()
    {
        // A pasted list arrives however the player's source formatted it; accepting all three
        // shapes costs nothing and saves a support thread about a setting that "does not work".
        Assert.That(
            ClassExclusionList.Parse("Building_Foo\nBuilding_Bar;Building_Baz"),
            Is.EqualTo(new[] { "Building_Foo", "Building_Bar", "Building_Baz" }));
    }

    [Test]
    public void A_bare_class_name_matches()
    {
        Assert.That(
            ClassExclusionList.Excludes(
                new[] { "Building_Foo" }, "SomeMod.Building_Foo", "Building_Foo"),
            Is.True);
    }

    [Test]
    public void A_qualified_class_name_matches()
    {
        // Both forms are accepted because both are what the player has in front of them: the
        // stack trace prints the qualified name, the mod's XML prints the bare one.
        Assert.That(
            ClassExclusionList.Excludes(
                new[] { "SomeMod.Building_Foo" }, "SomeMod.Building_Foo", "Building_Foo"),
            Is.True);
    }

    [Test]
    public void Matching_ignores_case()
    {
        Assert.That(
            ClassExclusionList.Excludes(
                new[] { "building_foo" }, "SomeMod.Building_Foo", "Building_Foo"),
            Is.True);
    }

    [Test]
    public void A_partial_name_does_not_match()
    {
        // Substring matching would be a footgun: "Building_Work" would silently take out every
        // bench in the game and the player would have no idea why nothing links any more.
        Assert.That(
            ClassExclusionList.Excludes(
                new[] { "Building_Fo" }, "SomeMod.Building_Foo", "Building_Foo"),
            Is.False);
    }

    [Test]
    public void An_unrelated_entry_does_not_match()
    {
        Assert.That(
            ClassExclusionList.Excludes(
                new[] { "Building_Bar" }, "SomeMod.Building_Foo", "Building_Foo"),
            Is.False);
    }

    [Test]
    public void An_empty_list_excludes_nothing()
    {
        Assert.That(
            ClassExclusionList.Excludes(new string[0], "SomeMod.Building_Foo", "Building_Foo"),
            Is.False);
    }
}
