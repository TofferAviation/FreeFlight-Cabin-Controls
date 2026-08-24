namespace FreeFlight.CabinControl.Core.Operations;

public enum TurnaroundStage
{
    AwaitingTurnaround,
    Turnaround,
    GateOpen,
    Boarding,
    GateClosing,
    Departure
}

public sealed record FlightTurnaroundSchedule(
    DateTimeOffset TurnaroundStart,
    DateTimeOffset GateOpen,
    DateTimeOffset BoardingStart,
    DateTimeOffset FinalBoarding,
    DateTimeOffset GateClose,
    DateTimeOffset Departure)
{
    public static FlightTurnaroundSchedule Create(
        DateTimeOffset departure,
        int turnaroundMinutes,
        int boardingStartMinutesBeforeDeparture,
        int finalBoardingMinutesBeforeDeparture,
        int gateCloseMinutesBeforeDeparture)
    {
        var turnaroundLead = Math.Max(1, turnaroundMinutes);
        var boardingLead = Math.Clamp(boardingStartMinutesBeforeDeparture, 1, turnaroundLead);
        var gateOpenLead = Math.Clamp(boardingLead + 10, boardingLead, turnaroundLead);
        var gateCloseLead = Math.Clamp(gateCloseMinutesBeforeDeparture, 0, boardingLead);
        var finalBoardingLead = Math.Clamp(
            finalBoardingMinutesBeforeDeparture,
            gateCloseLead,
            boardingLead);

        return new FlightTurnaroundSchedule(
            departure.AddMinutes(-turnaroundLead),
            departure.AddMinutes(-gateOpenLead),
            departure.AddMinutes(-boardingLead),
            departure.AddMinutes(-finalBoardingLead),
            departure.AddMinutes(-gateCloseLead),
            departure);
    }

    public TurnaroundStage GetStage(DateTimeOffset currentTime)
    {
        if (currentTime < TurnaroundStart)
        {
            return TurnaroundStage.AwaitingTurnaround;
        }

        if (currentTime < GateOpen)
        {
            return TurnaroundStage.Turnaround;
        }

        if (currentTime < BoardingStart)
        {
            return TurnaroundStage.GateOpen;
        }

        if (currentTime < GateClose)
        {
            return TurnaroundStage.Boarding;
        }

        return currentTime < Departure
            ? TurnaroundStage.GateClosing
            : TurnaroundStage.Departure;
    }
}
