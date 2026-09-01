using WorkbenchGroups.Core;

namespace WorkbenchGroups.Tests;

[TestFixture]
public class OvershootPolicyTests
{
    // "Do X times": the remaining count must strictly exceed the pawns already working, or the
    // last iteration gets claimed twice. The boundary (repeatCount == inFlight) is the whole
    // point of the feature and is pinned in both directions.
    [TestCase(5, 0, ExpectedResult = true)]
    [TestCase(5, 4, ExpectedResult = true)]
    [TestCase(5, 5, ExpectedResult = false)]
    [TestCase(5, 6, ExpectedResult = false)]
    [TestCase(1, 0, ExpectedResult = true)]
    [TestCase(1, 1, ExpectedResult = false)]
    [TestCase(0, 0, ExpectedResult = false)]
    public bool RepeatCount_blocks_once_every_remaining_iteration_is_claimed(int repeatCount, int inFlight)
    {
        return OvershootPolicy.MayStartAnother(
            RepeatModeCode.RepeatCount, repeatCount, 0, 0, inFlight, paused: false, suspended: false);
    }

    // "Do until you have X": work underway counts towards the target as if already produced.
    [TestCase(0, 20, 0, ExpectedResult = true)]
    [TestCase(19, 20, 0, ExpectedResult = true)]
    [TestCase(19, 20, 1, ExpectedResult = false)]
    [TestCase(18, 20, 1, ExpectedResult = true)]
    [TestCase(18, 20, 2, ExpectedResult = false)]
    [TestCase(20, 20, 0, ExpectedResult = false)]
    [TestCase(25, 20, 0, ExpectedResult = false)]
    public bool TargetCount_counts_in_flight_work_towards_the_target(int produced, int target, int inFlight)
    {
        return OvershootPolicy.MayStartAnother(
            RepeatModeCode.TargetCount, 0, produced, target, inFlight, paused: false, suspended: false);
    }

    [Test]
    public void Forever_never_blocks_a_second_worker()
    {
        // Unbounded orders are what players put bulk work on. Blocking a second pawn here would
        // make a linked group slower than two independent benches, inverting the point of the mod.
        Assert.That(
            OvershootPolicy.MayStartAnother(
                RepeatModeCode.Forever, 0, 0, 0, inFlight: 7, paused: false, suspended: false),
            Is.True);
    }

    [Test]
    public void Suspended_beats_everything()
    {
        Assert.That(
            OvershootPolicy.MayStartAnother(
                RepeatModeCode.Forever, 99, 0, 99, inFlight: 0, paused: false, suspended: true),
            Is.False);
    }

    [Test]
    public void Paused_blocks_target_count_but_not_repeat_count()
    {
        // Vanilla only latches `paused` for TargetCount bills; the other modes clear it. Mirroring
        // that here keeps a stale latch from silently freezing a "do 5 times" order.
        Assert.That(
            OvershootPolicy.MayStartAnother(
                RepeatModeCode.TargetCount, 0, 0, 10, 0, paused: true, suspended: false),
            Is.False, "TargetCount should honour the pause latch");

        Assert.That(
            OvershootPolicy.MayStartAnother(
                RepeatModeCode.RepeatCount, 5, 0, 0, 0, paused: true, suspended: false),
            Is.True, "RepeatCount should ignore the pause latch");
    }

    [TestCase(-1)]
    [TestCase(-99)]
    public void Negative_in_flight_is_clamped_rather_than_granting_extra_iterations(int inFlight)
    {
        // A tracking bug must degrade to vanilla behaviour, never to "make more than asked".
        Assert.That(
            OvershootPolicy.MayStartAnother(
                RepeatModeCode.RepeatCount, 0, 0, 0, inFlight, paused: false, suspended: false),
            Is.False);
    }
}
