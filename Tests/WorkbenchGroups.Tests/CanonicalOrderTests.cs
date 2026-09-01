using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class CanonicalOrderTests
{
    [Test]
    public void A_rotated_list_is_restored_to_the_authored_order()
    {
        // The ordinary case: round robin rotated the list, the player switches back to FIFO.
        string[] restored = CanonicalOrder.Restore(
            canonical: new[] { "a", "b", "c" },
            current: new[] { "c", "a", "b" });

        Assert.That(restored, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Bills_deleted_while_rotating_are_dropped()
    {
        string[] restored = CanonicalOrder.Restore(
            canonical: new[] { "a", "b", "c" },
            current: new[] { "c", "a" });

        Assert.That(restored, Is.EqualTo(new[] { "a", "c" }));
    }

    [Test]
    public void Bills_added_while_rotating_are_appended_in_their_current_order()
    {
        // New bills go to the bottom, which is where vanilla's AddBill puts them, so the
        // restore does not surprise a player who just queued something.
        string[] restored = CanonicalOrder.Restore(
            canonical: new[] { "a", "b" },
            current: new[] { "new2", "b", "new1", "a" });

        Assert.That(restored, Is.EqualTo(new[] { "a", "b", "new2", "new1" }));
    }

    [Test]
    public void A_wholly_replaced_list_keeps_its_current_order()
    {
        string[] restored = CanonicalOrder.Restore(
            canonical: new[] { "a", "b" },
            current: new[] { "x", "y", "z" });

        Assert.That(restored, Is.EqualTo(new[] { "x", "y", "z" }));
    }

    [Test]
    public void An_empty_snapshot_leaves_the_list_untouched()
    {
        // Happens for a group that was created directly in round-robin mode, or whose snapshot
        // was lost. Must be a no-op, not a wipe.
        string[] restored = CanonicalOrder.Restore(new string[0], new[] { "a", "b", "c" });

        Assert.That(restored, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Restoring_an_empty_list_yields_an_empty_list()
    {
        Assert.That(CanonicalOrder.Restore(new[] { "a", "b" }, new string[0]), Is.Empty);
    }

    [Test]
    public void Nulls_are_treated_as_empty()
    {
        Assert.That(CanonicalOrder.Restore(null!, null!), Is.Empty);
        Assert.That(CanonicalOrder.Restore(null!, new[] { "a" }), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void The_result_is_always_a_permutation_of_the_current_list()
    {
        // The invariant that matters: whatever the snapshot says, we must never invent, drop or
        // duplicate a live bill, because the caller writes this straight back over the stack.
        string[] current = { "c", "a", "ghost", "b" };
        string[] restored = CanonicalOrder.Restore(new[] { "a", "b", "gone", "c" }, current);

        Assert.That(restored, Is.EquivalentTo(current));
        Assert.That(restored, Is.EqualTo(new[] { "a", "b", "c", "ghost" }));
    }
}
