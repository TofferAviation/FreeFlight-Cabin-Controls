namespace FreeFlight.CabinControl.Core.Passengers;

public readonly record struct CabinCrewRestAssignment(
    bool IsActive,
    int RestGroup,
    int RestingCrewCount,
    TimeSpan Remaining);

public static class CabinCrewRestSchedule
{
    public static TimeSpan FirstRestDuration { get; } = TimeSpan.FromHours(3.5d);

    public static TimeSpan SecondShiftExtraDuty { get; } = TimeSpan.FromHours(2d);

    public static TimeSpan SecondRestDuration { get; } = TimeSpan.FromHours(2d);

    public static TimeSpan ArrivalRestCutoff { get; } = TimeSpan.FromHours(3d);

    public static CabinCrewRestAssignment Evaluate(
        DateTimeOffset cruiseStartedAt,
        DateTimeOffset currentTime,
        int crewCount,
        TimeSpan? timeUntilLanding = null)
    {
        if (crewCount < 2 || currentTime < cruiseStartedAt ||
            timeUntilLanding is { } remainingFlight && remainingFlight <= ArrivalRestCutoff)
        {
            return default;
        }

        var elapsed = currentTime - cruiseStartedAt;
        if (elapsed < FirstRestDuration)
        {
            return new CabinCrewRestAssignment(
                true,
                1,
                crewCount / 2,
                FirstRestDuration - elapsed);
        }

        var secondRestStartsAt = FirstRestDuration + SecondShiftExtraDuty;
        if (elapsed >= secondRestStartsAt && elapsed < secondRestStartsAt + SecondRestDuration)
        {
            return new CabinCrewRestAssignment(
                true,
                2,
                crewCount - (crewCount / 2),
                secondRestStartsAt + SecondRestDuration - elapsed);
        }

        return default;
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
