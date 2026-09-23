using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class UrgencyOrderTests
{
    private const int NoFloor = 0;
    private const int OneEach = 1;

    private static UrgencyInput Bill(int baseIndex, int count = 0, bool countable = true,
        bool marked = false, int? target = null)
    {
        return new UrgencyInput
        {
            BaseIndex = baseIndex,
            Count = count,
            Countable = countable,
            IsMarked = marked,
            HasTarget = target.HasValue,
            Target = target ?? 0,
        };
    }

    // --- Inertness: the feature must not move anything until switched on ---

    [Test]
    public void With_no_floor_and_no_balance_the_base_order_is_kept()
    {
        UrgencyInput[] bills = { Bill(0, count: 0), Bill(1, count: 9), Bill(2, count: 0) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: false), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void The_base_index_not_the_input_position_decides_the_default_order()
    {
        // The adapter feeds authored positions here under "in order" + floors, so a bill lifted
        // for being short drops back to where the player put it once it is not.
        UrgencyInput[] bills = { Bill(2), Bill(0), Bill(1) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: false), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void An_empty_or_null_list_sorts_to_nothing()
    {
        Assert.That(UrgencyOrder.Sort(new UrgencyInput[0], OneEach, true), Is.Empty);
        Assert.That(UrgencyOrder.Sort(null!, OneEach, true), Is.Empty);
    }

    // --- Tier 1: the marker ---

    [Test]
    public void The_marked_order_goes_first_whatever_else_is_true()
    {
        UrgencyInput[] bills =
        {
            Bill(0, count: 0, target: 10),
            Bill(1, count: 50, target: 10, marked: true),
            Bill(2, count: 0),
        };

        Assert.That(UrgencyOrder.Sort(bills, OneEach, balance: true)[0], Is.EqualTo(1));
    }

    // --- Tier 2: one of each first ---

    [Test]
    public void Short_orders_come_first_and_keep_their_base_order()
    {
        UrgencyInput[] bills = { Bill(0, count: 3), Bill(1, count: 0), Bill(2, count: 5), Bill(3, count: 0) };
        Assert.That(UrgencyOrder.Sort(bills, OneEach, balance: false), Is.EqualTo(new[] { 1, 3, 0, 2 }));
    }

    [Test]
    public void An_order_that_cannot_be_counted_is_never_short()
    {
        // Butchery-style recipes with several products: there is no count to be short of, and
        // sorting them to the front on a zero that means "unknown" would be sorting on garbage.
        UrgencyInput[] bills = { Bill(0, count: 4), Bill(1, count: 0, countable: false) };
        Assert.That(UrgencyOrder.Sort(bills, OneEach, balance: false), Is.EqualTo(new[] { 0, 1 }));
    }

    [TestCase(0, 1, true)]
    [TestCase(1, 1, false)]
    [TestCase(2, 1, false)]
    [TestCase(0, 0, false)]
    [TestCase(2, 3, true)]
    public void Below_floor_is_strictly_less_than(int count, int floor, bool expected)
    {
        Assert.That(UrgencyOrder.IsBelowFloor(Bill(0, count: count), floor), Is.EqualTo(expected));
    }

    // --- Tier 3: Balance ---

    [Test]
    public void Balance_puts_the_emptiest_target_first()
    {
        UrgencyInput[] bills =
        {
            Bill(0, count: 8, target: 10),  // 0.8
            Bill(1, count: 1, target: 10),  // 0.1
            Bill(2, count: 10, target: 20), // 0.5
        };

        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: true), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void Balance_compares_fractions_not_raw_shortfalls()
    {
        // 5 of 100 is far emptier than 3 of 4, although it is 95 short against 1.
        UrgencyInput[] bills = { Bill(0, count: 3, target: 4), Bill(1, count: 5, target: 100) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: true), Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void Balance_sorts_orders_without_a_target_after_every_target_in_base_order()
    {
        UrgencyInput[] bills =
        {
            Bill(0),                        // do forever
            Bill(1, count: 99, target: 10), // over target, still has a ratio
            Bill(2),                        // do X times
            Bill(3, count: 0, target: 10),
        };

        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: true), Is.EqualTo(new[] { 3, 1, 0, 2 }));
    }

    [Test]
    public void Balance_ties_fall_back_to_base_order()
    {
        UrgencyInput[] bills = { Bill(2, count: 1, target: 2), Bill(0, count: 5, target: 10), Bill(1, count: 2, target: 4) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: true), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void A_zero_target_sorts_after_real_ratios_but_before_no_target()
    {
        UrgencyInput[] bills = { Bill(0), Bill(1, count: 0, target: 0), Bill(2, count: 50, target: 10) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: true), Is.EqualTo(new[] { 2, 1, 0 }));
    }

    [Test]
    public void The_ratio_is_ignored_outside_balance()
    {
        UrgencyInput[] bills = { Bill(0, count: 9, target: 10), Bill(1, count: 0, target: 10) };
        Assert.That(UrgencyOrder.Sort(bills, NoFloor, balance: false), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void The_floor_outranks_the_balance_ratio()
    {
        // "At least one of each, then balance": a do-forever order with none in stock is lifted
        // above a target order that is emptier by ratio but already has one.
        UrgencyInput[] bills = { Bill(0, count: 1, target: 100), Bill(1, count: 0) };
        Assert.That(UrgencyOrder.Sort(bills, OneEach, balance: true), Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void Sorting_is_deterministic_for_identical_keys()
    {
        // Two bills the keys cannot tell apart must come out in input order every time, or the
        // adapter would see a non-identity permutation and mutate the list on every scan.
        UrgencyInput[] bills = { Bill(0), Bill(0), Bill(0) };
        Assert.That(UrgencyOrder.Sort(bills, OneEach, balance: true), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void Resorting_a_sorted_list_is_the_identity()
    {
        UrgencyInput[] bills = { Bill(0, count: 0, target: 5), Bill(1, count: 0), Bill(2, count: 4, target: 5) };
        int[] once = UrgencyOrder.Sort(bills, OneEach, balance: true);

        UrgencyInput[] applied = once.Select(i => bills[i]).ToArray();
        Assert.That(UrgencyOrder.IsIdentity(UrgencyOrder.Sort(applied, OneEach, balance: true)), Is.True);
    }

    // --- Counting ---

    [TestCase(0, 0, 1, 0)]
    [TestCase(3, 0, 1, 3)]
    [TestCase(0, 1, 1, 1)]
    [TestCase(2, 2, 4, 10)]
    [TestCase(-5, 1, 1, 1)]
    [TestCase(0, -2, 1, 0)]
    [TestCase(0, 2, 0, 2)]
    [TestCase(int.MaxValue, 5, 100, int.MaxValue)]
    public void The_effective_count_adds_one_iteration_per_worker(
        int stock, int inFlight, int units, int expected)
    {
        Assert.That(UrgencyOrder.EffectiveCount(stock, inFlight, units), Is.EqualTo(expected));
    }

    [Test]
    public void A_worker_on_a_short_order_lifts_it_out_of_the_short_tier()
    {
        // The timing trap the in-flight term exists for: stock still reads zero for the whole
        // craft, and without this the next idle pawn would start a second one.
        int count = UrgencyOrder.EffectiveCount(stock: 0, inFlight: 1, unitsPerIteration: 1);
        Assert.That(UrgencyOrder.IsBelowFloor(Bill(0, count: count), OneEach), Is.False);
    }

    [TestCase(true, 10, 10, true)]
    [TestCase(true, 11, 10, true)]
    [TestCase(true, 9, 10, false)]
    [TestCase(false, 99, 10, false)]
    [TestCase(true, 0, 0, true)]
    public void A_target_is_reached_only_on_a_known_count(bool known, int stock, int target, bool expected)
    {
        Assert.That(UrgencyOrder.IsTargetReached(known, stock, target), Is.EqualTo(expected));
    }

    [TestCase(new[] { 0, 1, 2 }, true)]
    [TestCase(new[] { 1, 0, 2 }, false)]
    [TestCase(new int[0], true)]
    public void Identity_is_recognised(int[] order, bool expected)
    {
        Assert.That(UrgencyOrder.IsIdentity(order), Is.EqualTo(expected));
    }
}

[TestFixture]
public class CountFreshnessTests
{
    [TestCase(100, 100, 60, true)]
    [TestCase(100, 159, 60, true)]
    [TestCase(100, 160, 60, false)]
    [TestCase(-1, 0, 60, false)]
    [TestCase(200, 100, 60, false)]
    [TestCase(100, 100, 0, false)]
    public void A_count_is_reused_only_inside_its_lifetime(int stamped, int now, int ttl, bool fresh)
    {
        Assert.That(CountFreshness.IsFresh(stamped, now, ttl), Is.EqualTo(fresh));
    }
}
