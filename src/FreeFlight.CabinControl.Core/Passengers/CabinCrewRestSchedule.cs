namespace FreeFlight.CabinControl.Core.Passengers;

public readonly record struct CabinCrewRestAssignment(
    bool IsActive,
    int RestGroup,
    int RestingCrewCount,
    TimeSpan Remaining);

public static class CabinCrewRestSchedule
{
    public static TimeSpan RestBlockDuration { get; } = TimeSpan.FromHours(3.5d);

    public static CabinCrewRestAssignment Evaluate(
        DateTimeOffset cruiseStartedAt,
        DateTimeOffset currentTime,
        int crewCount)
    {
        if (crewCount < 2 || currentTime < cruiseStartedAt)
        {
            return default;
        }

        var elapsed = currentTime - cruiseStartedAt;
        var blockTicks = RestBlockDuration.Ticks;
        var blockIndex = elapsed.Ticks / blockTicks;
        var elapsedInBlock = TimeSpan.FromTicks(elapsed.Ticks % blockTicks);
        return new CabinCrewRestAssignment(
            true,
            (int)(blockIndex % 2L) + 1,
            crewCount / 2,
            RestBlockDuration - elapsedInBlock);
    }

    public static bool IsCrewMemberResting(
        int zeroBasedCrewIndex,
        int crewCount,
        CabinCrewRestAssignment assignment)
    {
        if (!assignment.IsActive || zeroBasedCrewIndex < 0 || zeroBasedCrewIndex >= crewCount)
        {
            return false;
        }

        var splitIndex = crewCount / 2;
        return assignment.RestGroup == 1
            ? zeroBasedCrewIndex < splitIndex
            : zeroBasedCrewIndex >= splitIndex;
    }
}
