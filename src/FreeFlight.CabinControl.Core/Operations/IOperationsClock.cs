namespace FreeFlight.CabinControl.Core.Operations;

/// <summary>
/// Supplies the clock used by gate and cabin operations. The desktop preview uses
/// local system time; the X-Plane bridge can later provide simulator time through
/// the same contract.
/// </summary>
public interface IOperationsClock
{
    DateTimeOffset Now { get; }

    string SourceLabel { get; }
}
