using ADM.Domain;

namespace ADM.Desktop.Presentation;

public enum DemoStage { Preparation, BoardingApproval, Boarding, ClosureApproval, Releasing, Complete, NeedsReview }
public sealed record DemoMessage(string Sender, string Text, DateTimeOffset SentAt, bool IsPilot)
{
    public bool IsSystem { get; init; }
    public string Time => SentAt.ToLocalTime().ToString("HH:mm");
    public string AccessibleText => $"{Sender}, {Time}. {Text}";
}
public sealed record DemoApproval(ApprovalKind Kind, DateTimeOffset ApprovedAt, Guid DemoId);
public sealed record DemoSector(string Number, string Route, string Detail, bool IsCurrent);
public sealed record PendingServiceAction(ServiceRow Service, string Action, string Explanation);

public sealed class ServiceRow(ServiceKind kind, ServiceState state, string detail, int? phase = null) : ObservableObject
{
    private ServiceState _state = state;
    private string _detail = detail;
    public ServiceKind Kind { get; } = kind;
    public string Name => Kind == ServiceKind.Refuelling ? "Refuelling" : Kind.ToString();
    public string Number => (phase ?? ServiceSequence.DefaultPhases[Kind]).ToString("00");
    public ServiceState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            foreach (string property in new[] { nameof(Status), nameof(CanSkip), nameof(CanCancel), nameof(CanResume), nameof(CanObserveCancellation), nameof(Progress), nameof(ShowProgress), nameof(ShowDetail) }) Raise(property);
        }
    }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public string Status => State switch
    {
        ServiceState.Completed => "Complete · simulated",
        ServiceState.Active => "In progress · simulated",
        ServiceState.CancelPending => "Cancellation pending",
        ServiceState.Cancelled => "Cancelled · simulated",
        ServiceState.NotRequired => "Not required",
        ServiceState.Skipped => "Skipped in demo",
        ServiceState.Unknown => "Unknown · review needed",
        _ => "Planned"
    };
    public double Progress => State switch { ServiceState.Completed => 100, ServiceState.Active => 64, _ => 0 };
    public bool ShowProgress => State == ServiceState.Active;
    public bool ShowDetail => State is not (ServiceState.Completed or ServiceState.NotRequired);
    public bool CanSkip => Kind != ServiceKind.Boarding && State is ServiceState.Planned or ServiceState.Eligible;
    public bool CanCancel => State is ServiceState.Active or ServiceState.Requested;
    public bool CanResume => State is ServiceState.Cancelled or ServiceState.Skipped;
    public bool CanObserveCancellation => State == ServiceState.CancelPending;
}
