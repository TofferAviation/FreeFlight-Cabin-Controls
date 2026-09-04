namespace FreeFlight.CabinControl.Core.Cabin;

public sealed record CabinLavatoryDefinition(
    string Id,
    string Zone,
    double LongitudinalStation,
    int Capacity = 1);

public sealed record LavatoryQueueSnapshot(
    CabinLavatoryDefinition Lavatory,
    IReadOnlyList<int> Occupants,
    IReadOnlyList<int> WaitingPassengerIds)
{
    public bool IsOccupied => Occupants.Count >= Lavatory.Capacity;

    public int QueueLength => WaitingPassengerIds.Count;
}

public enum LavatoryRequestResult
{
    Entered,
    Queued,
    AlreadyOccupying,
    AlreadyQueued,
    UnknownLavatory
}

public sealed class LavatoryQueueManager
{
    private readonly Dictionary<string, CabinLavatoryDefinition> _lavatories;
    private readonly Dictionary<string, List<int>> _occupants;
    private readonly Dictionary<string, Queue<int>> _queues;
    private readonly Dictionary<int, string> _passengerLocation = [];
    private readonly HashSet<int> _queuedPassengers = [];

    public LavatoryQueueManager(IEnumerable<CabinLavatoryDefinition> lavatories)
    {
        ArgumentNullException.ThrowIfNull(lavatories);
        var definitions = lavatories.ToArray();
        if (definitions.Length == 0)
        {
            throw new ArgumentException("At least one lavatory must be configured.", nameof(lavatories));
        }

        if (definitions.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Capacity <= 0))
        {
            throw new ArgumentException("Lavatories require an ID and a positive capacity.", nameof(lavatories));
        }

        _lavatories = definitions.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        _occupants = definitions.ToDictionary(
            item => item.Id,
            _ => new List<int>(),
            StringComparer.OrdinalIgnoreCase);
        _queues = definitions.ToDictionary(
            item => item.Id,
            _ => new Queue<int>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public LavatoryRequestResult Request(int passengerId, string lavatoryId)
    {
        if (!_lavatories.TryGetValue(lavatoryId, out var lavatory))
        {
            return LavatoryRequestResult.UnknownLavatory;
        }

        if (_passengerLocation.ContainsKey(passengerId))
        {
            return LavatoryRequestResult.AlreadyOccupying;
        }

        if (_queuedPassengers.Contains(passengerId))
        {
            return LavatoryRequestResult.AlreadyQueued;
        }

        var occupants = _occupants[lavatory.Id];
        if (occupants.Count < lavatory.Capacity)
        {
            occupants.Add(passengerId);
            _passengerLocation[passengerId] = lavatory.Id;
            return LavatoryRequestResult.Entered;
        }

        _queues[lavatory.Id].Enqueue(passengerId);
        _queuedPassengers.Add(passengerId);
        return LavatoryRequestResult.Queued;
    }

    public int? Release(int passengerId)
    {
        if (!_passengerLocation.Remove(passengerId, out var lavatoryId))
        {
            return null;
        }

        var occupants = _occupants[lavatoryId];
        occupants.Remove(passengerId);
        var queue = _queues[lavatoryId];
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (!_queuedPassengers.Remove(next))
            {
                continue;
            }

            occupants.Add(next);
            _passengerLocation[next] = lavatoryId;
            return next;
        }

        return null;
    }

    public bool CancelRequest(int passengerId)
    {
        if (!_queuedPassengers.Remove(passengerId))
        {
            return false;
        }

        foreach (var pair in _queues.ToArray())
        {
            if (!pair.Value.Contains(passengerId))
            {
                continue;
            }

            _queues[pair.Key] = new Queue<int>(pair.Value.Where(id => id != passengerId));
            return true;
        }

        return true;
    }

    public int GetQueuePosition(int passengerId)
    {
        foreach (var queue in _queues.Values)
        {
            var position = 1;
            foreach (var queued in queue)
            {
                if (queued == passengerId)
                {
                    return position;
                }

                position++;
            }
        }

        return 0;
    }

    public string? GetPassengerLavatory(int passengerId) =>
        _passengerLocation.GetValueOrDefault(passengerId);

    public IReadOnlyList<LavatoryQueueSnapshot> Snapshot() => _lavatories.Values
        .OrderBy(item => item.LongitudinalStation)
        .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
        .Select(item => new LavatoryQueueSnapshot(
            item,
            _occupants[item.Id].ToArray(),
            _queues[item.Id].ToArray()))
        .ToArray();
}

public static class CabinLavatoryCatalog
{
    public static IReadOnlyList<CabinLavatoryDefinition> ForAircraftFamily(bool isNarrowBody) => isNarrowBody
        ?
        [
            new("FWD", "Forward", 0.04d),
            new("AFT-L", "Aft", 0.96d),
            new("AFT-R", "Aft", 0.96d)
        ]
        :
        [
            new("FWD-L", "Forward", 0.05d),
            new("FWD-R", "Forward", 0.05d),
            new("MID-L", "Mid", 0.52d),
            new("MID-R", "Mid", 0.52d),
            new("AFT-L", "Aft", 0.95d),
            new("AFT-R", "Aft", 0.95d)
        ];
}
