using FreeFlight.CabinControl.Core.Operations;

namespace FreeFlight.CabinControl.App.Services;

public sealed class LocalOperationsClock : IOperationsClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public string SourceLabel => "LOCAL TIME";
}
