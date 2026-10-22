using ADM.Contracts;

namespace ADM.Infrastructure;

public sealed class SystemOperationalClock : IOperationalClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
