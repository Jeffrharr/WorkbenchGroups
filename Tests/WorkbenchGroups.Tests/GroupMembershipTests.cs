using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class GroupMembershipTests
{
    [TestCase(0, ExpectedResult = true)]
    [TestCase(1, ExpectedResult = true)]
    [TestCase(2, ExpectedResult = false)]
    public bool A_group_of_one_or_fewer_dissolves(int memberCount)
    {
        return GroupMembership.ShouldDissolve(memberCount);
    }

    [Test]
    public void The_anchor_is_the_lowest_thing_id()
    {
        Assert.That(GroupMembership.TryElectAnchor(new[] { 40, 12, 900 }, out int anchor), Is.True);
        Assert.That(anchor, Is.EqualTo(12));
    }

    [Test]
    public void Anchor_election_does_not_depend_on_the_order_of_candidates()
    {
        // Load order reshuffles this list. If election were positional, a save/load could move
        // the shared bills onto a different bench and the player would watch their orders jump.
        GroupMembership.TryElectAnchor(new[] { 900, 12, 40 }, out int a);
        GroupMembership.TryElectAnchor(new[] { 12, 40, 900 }, out int b);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Electing_from_no_candidates_fails_rather_than_returning_a_sentinel()
    {
        Assert.That(GroupMembership.TryElectAnchor(new int[0], out _), Is.False);
        Assert.That(GroupMembership.TryElectAnchor(null!, out _), Is.False);
    }

    [TestCase(new[] { 5, 5, 5 }, 15, ExpectedResult = true)]
    [TestCase(new[] { 6, 5, 5 }, 15, ExpectedResult = false)]
    [TestCase(new[] { 15 }, 15, ExpectedResult = true)]
    [TestCase(new int[0], 15, ExpectedResult = true)]
    public bool A_merge_is_allowed_only_when_every_bill_fits_in_one_stack(int[] counts, int max)
    {
        return GroupMembership.CanMerge(counts, max, out _);
    }

    [Test]
    public void The_total_is_reported_so_the_refusal_can_name_a_number()
    {
        GroupMembership.CanMerge(new[] { 6, 5, 5 }, 15, out int total);

        Assert.That(total, Is.EqualTo(16));
    }
}
