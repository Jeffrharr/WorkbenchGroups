using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class BillOrderingTests
{
    [Test]
    public void Rotating_the_head_of_a_list_moves_it_to_the_tail()
    {
        Assert.That(BillOrdering.TryPlanRotateToTail(4, 0, out int removeAt, out int insertAt), Is.True);
        Assert.That(removeAt, Is.EqualTo(0));

        // After removing one entry the list is 3 long, so 3 is an append — the largest index
        // List.Insert accepts. One more would throw from inside a Harmony postfix on a hot path.
        Assert.That(insertAt, Is.EqualTo(3));
    }

    [Test]
    public void A_bill_that_is_not_in_the_list_is_refused()
    {
        // This is the case that corrupts vanilla's BillStack.Reorder: index -1 makes its Remove a
        // no-op and its Insert add a foreign bill to the stack. Reachable whenever the player
        // deletes a bill while a pawn is walking to the bench.
        Assert.That(BillOrdering.TryPlanRotateToTail(4, -1, out _, out _), Is.False);
    }

    [TestCase(4, 4)]
    [TestCase(4, 99)]
    public void An_index_past_the_end_is_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanRotateToTail(count, index, out _, out _), Is.False);
    }

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    public void Lists_too_short_to_rotate_are_refused(int count, int index)
    {
        Assert.That(BillOrdering.TryPlanRotateToTail(count, index, out _, out _), Is.False);
    }

    [Test]
    public void An_entry_already_at_the_tail_is_left_alone()
    {
        // Not an error, just a no-op: skipping it keeps the tab still when a group is down to one
        // eligible bill, which would otherwise churn visibly on every single craft.
        Assert.That(BillOrdering.TryPlanRotateToTail(3, 2, out _, out _), Is.False);
    }

    [Test]
    public void Applying_the_plan_to_a_real_list_rotates_it()
    {
        List<string> bills = new() { "a", "b", "c" };

        Assert.That(BillOrdering.TryPlanRotateToTail(bills.Count, 0, out int removeAt, out int insertAt), Is.True);
        string moved = bills[removeAt];
        bills.RemoveAt(removeAt);
        bills.Insert(insertAt, moved);

        Assert.That(bills, Is.EqualTo(new[] { "b", "c", "a" }));
    }
}
