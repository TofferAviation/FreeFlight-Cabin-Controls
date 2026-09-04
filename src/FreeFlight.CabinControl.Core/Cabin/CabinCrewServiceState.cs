using FreeFlight.CabinControl.Core.Passengers;

namespace FreeFlight.CabinControl.Core.Cabin;

public enum CabinCrewServiceTaskType
{
    Idle,
    PreflightChecks,
    PreparingGalley,
    HeatingMeal,
    PreparingServiceCart,
    DeliveringMeal,
    BeverageService,
    CollectingTrays,
    CabinCheck,
    PassengerAssistance,
    SecuredForTakeoff,
    SecuredForLanding,
    CrewRest
}

public sealed record CabinCrewServiceTask(
    CabinCrewServiceTaskType Type,
    string Title,
    string Detail,
    string? ItemName = null,
    string? TargetSeat = null,
    double Progress = 0d)
{
    public double NormalizedProgress => Math.Clamp(Progress, 0d, 1d);
}

public sealed class CabinCrewServiceState
{
    private CabinCrewServiceTask _task = new(
        CabinCrewServiceTaskType.Idle,
        "Available",
        "Waiting for the next cabin duty");

    public CabinCrewServiceState(int crewId, string displayName, string zone)
    {
        if (crewId <= 0) throw new ArgumentOutOfRangeException(nameof(crewId));
        CrewId = crewId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Crew {crewId}" : displayName.Trim();
        Zone = string.IsNullOrWhiteSpace(zone) ? "Cabin" : zone.Trim();
    }

    public int CrewId { get; }

    public string DisplayName { get; }

    public string Zone { get; private set; }

    public CabinPoint Position { get; private set; }

    public CabinCrewServiceTask CurrentTask => _task;

    public bool IsMoving { get; private set; }

    public event Action<CabinCrewServiceState>? Changed;

    public void AssignTask(CabinCrewServiceTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _task = task with { Progress = Math.Clamp(task.Progress, 0d, 1d) };
        Changed?.Invoke(this);
    }

    public void SetZone(string zone)
    {
        Zone = string.IsNullOrWhiteSpace(zone) ? Zone : zone.Trim();
        Changed?.Invoke(this);
    }

    public void SetPosition(CabinPoint position, bool isMoving)
    {
        Position = position;
        IsMoving = isMoving;
        Changed?.Invoke(this);
    }

    public void SetProgress(double progress)
    {
        var normalized = Math.Clamp(progress, 0d, 1d);
        if (Math.Abs(_task.Progress - normalized) < 0.0001d)
        {
            return;
        }

        _task = _task with { Progress = normalized };
        Changed?.Invoke(this);
    }
}

public static class CabinCrewTaskFactory
{
    public static CabinCrewServiceTask HeatingMeal(string itemName, string galley, double progress = 0d) => new(
        CabinCrewServiceTaskType.HeatingMeal,
        "Heating meal",
        $"Preparing {itemName} in {galley}",
        itemName,
        Progress: progress);

    public static CabinCrewServiceTask DeliveringMeal(string itemName, string seat, double progress = 0d) => new(
        CabinCrewServiceTaskType.DeliveringMeal,
        "Delivering meal",
        $"Taking {itemName} to seat {seat}",
        itemName,
        seat,
        progress);

    public static CabinCrewServiceTask ServingDrink(string itemName, string seat, double progress = 0d) => new(
        CabinCrewServiceTaskType.BeverageService,
        "Beverage service",
        $"Serving {itemName} to seat {seat}",
        itemName,
        seat,
        progress);

    public static CabinCrewServiceTask CollectingTrays(string zone, double progress = 0d) => new(
        CabinCrewServiceTaskType.CollectingTrays,
        "Collecting trays",
        $"Clearing service items in {zone}",
        Progress: progress);
}
