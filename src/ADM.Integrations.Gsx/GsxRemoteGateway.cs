using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Integrations.Gsx;

public sealed record GsxRemoteOptions
{
    public string IniPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtuali", "CouatlAddons.ini");
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Opt-in M3 adapter. Serializes sends; never retries a command after uncertain delivery or reconnect.</summary>
public sealed class GsxRemoteGateway : IGsxGateway, IGsxLiveState, IAsyncDisposable
{
    private readonly Guid _sessionId;
    private readonly GsxRemoteOptions _options;
    private readonly Func<IGsxSocket> _socketFactory;
    private readonly Action<string> _diagnostic;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly object _sync = new();
    private readonly Channel<GsxEvent> _events = Channel.CreateUnbounded<GsxEvent>();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<CommandReceipt>> _pending = new();
    private readonly Dictionary<(Guid Session, ServiceKind Kind, int Version), (ServiceCommand Command, Task<CommandReceipt> Receipt)> _intents = [];
    private readonly HashSet<ServiceKind> _uncertainServices = [];
    private IGsxSocket? _socket;
    private Task? _loop;
    private int _disposed;
    private volatile ConnectionState _connection = ConnectionState.Offline;
    private bool _hello;
    private bool _running;
    private bool _hasSnapshot;
    private bool _menuShown;
    private bool _promptActive;
    private string? _handlerAirport;
    private string? _handlerStand;
    private long _generation;
    private DateTimeOffset _receivedAt;
    private ImmutableDictionary<string, string?> _context = ImmutableDictionary<string, string?>.Empty;
    private ImmutableHashSet<string> _features = [];
    private FlightPlanSnapshot? _flightPlan;
    private string? _aircraftType;
    private string? _aircraftSetupKey;
    private ImmutableDictionary<ServiceKind, RemoteServiceObservation> _observations = ImmutableDictionary<ServiceKind, RemoteServiceObservation>.Empty;
    private readonly Dictionary<ServiceKind, Evidence> _completed = [];
    public GsxRemoteGateway(Guid sessionId, GsxRemoteOptions? options = null, Func<IGsxSocket>? socketFactory = null, Action<string>? diagnostic = null)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(sessionId));
        _sessionId = sessionId; _options = options ?? new(); _socketFactory = socketFactory ?? (() => new GsxSocket()); _diagnostic = diagnostic ?? (_ => { });
        if (_options.ConnectTimeout <= TimeSpan.Zero || _options.CommandTimeout <= TimeSpan.Zero || _options.ReconnectDelay <= TimeSpan.Zero)
            throw new ArgumentException("Integration timeouts must be positive.", nameof(options));
    }
    public void Start() { lock (_sync) { ObjectDisposedException.ThrowIf(_disposed != 0, this); _loop ??= Task.Run(RunAsync); } }
    public ConnectionState Connection { get => _connection; private set => _connection = value; }
    public ImmutableDictionary<ServiceKind, RemoteServiceObservation> Observations { get { lock (_sync) return _observations; } }
    public GsxLiveSnapshot Snapshot
    {
        get
        {
            lock (_sync) return new(Connection, _generation, _receivedAt, _observations.ToImmutableDictionary(pair => pair.Key,
                pair => new GsxServiceStatus(pair.Value.State == ServiceState.Planned && _completed.ContainsKey(pair.Key) ? ServiceState.Completed : pair.Value.State, pair.Value.CanTrigger, pair.Value.Waiting,
                    pair.Value.State == ServiceState.Planned && _completed.TryGetValue(pair.Key, out var completed) ? completed :
                    new(EvidenceSource.Gsx, $"Couatl wire-1 {pair.Value.Id}={pair.Value.SemanticState}/{pair.Value.StateRaw}", pair.Value.ObservedAt))
                { IsIdleUnavailable = pair.Value is { SemanticState: "unavailable", StateRaw: 2 } or { SemanticState: "bypassed", StateRaw: 3 } }),
                _context.GetValueOrDefault("airport") ?? _handlerAirport, _context.GetValueOrDefault("parking") ?? _handlerStand, _context.GetValueOrDefault("aircraft"))
            { FlightPlan = _flightPlan, AircraftIcaoType = _aircraftType, AircraftSetupKey = _aircraftSetupKey, AwaitingInput = _promptActive, MenuOpen = _menuShown };
        }
    }
    public Task<GsxCapabilities> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(new GsxCapabilities("Couatl Remote API v2", _hello ? "wire-1" : null, false,
            Ready ? _observations.Keys.ToImmutableHashSet() : [], [], []));
    }
    private bool Ready => _hello && _running && _hasSnapshot && _features.Contains("state") && _features.Contains("services");
    public IAsyncEnumerable<GsxEvent> WatchAsync(CancellationToken cancellationToken) => _events.Reader.ReadAllAsync(cancellationToken);

    public Task<CommandReceipt> RequestServiceAsync(ServiceCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (command.SessionId != _sessionId || command.CommandId == Guid.Empty || command.PlanSnapshotId == Guid.Empty ||
                command.IntentVersion < 1 || !Enum.IsDefined(command.Kind) || string.IsNullOrWhiteSpace(command.PolicySource))
                return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "Invalid command/session/policy identity."));
            var key = (command.SessionId, command.Kind, command.IntentVersion);
            if (_intents.TryGetValue(key, out var previous)) return previous.Command == command ? previous.Receipt.WaitAsync(cancellationToken) :
                Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "Intent version already used by another command."));
            if (_intents.Values.Any(intent => intent.Command.CommandId == command.CommandId))
                return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "Command ID reused."));
            // Record the task before it can send. Caller cancellation stops waiting, not evidence collection.
            var task = Task.Run(() => SendServiceAsync(command));
            _intents.Add(key, (command, task));
            return task.WaitAsync(cancellationToken);
        }
    }
    public Task<CommandReceipt> CancelServiceAsync(ServiceCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "The documented API has no service cancellation verb. Use GSX manually and reconcile state."));
    }
    public Task<CommandReceipt> ExecuteVerifiedActionAsync(ActionCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandReceipt(command.CommandId, ReceiptStatus.Rejected, "No authoritative door/equipment release action is verified for this aircraft."));
    }
    private async Task<CommandReceipt> SendServiceAsync(ServiceCommand command)
    {
        await _send.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            IGsxSocket? socket;
            lock (_sync)
            {
                socket = _socket;
                if (!Ready || socket is null || _uncertainServices.Contains(command.Kind) || !_observations.TryGetValue(command.Kind, out var observation) || !observation.CanTrigger || observation.Waiting)
                    return new(command.CommandId, ReceiptStatus.Rejected, "GSX is offline, unverified, blocked, or this service is not triggerable.");
            }
            var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[command.CommandId] = completion;
            _diagnostic($"SEND {command.CommandId:D} session={command.SessionId:D} service={command.Kind} snapshot={command.PlanSnapshotId:D} policy={command.PolicySource}");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(_options.CommandTimeout);
                await socket.SendAsync(GsxProtocol.Trigger(command.CommandId, command.Kind), timeout.Token).ConfigureAwait(false);
                var receipt = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (receipt.Status == ReceiptStatus.Unknown) lock (_sync) _uncertainServices.Add(command.Kind);
                return receipt;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException or ObjectDisposedException)
            {
                lock (_sync) _uncertainServices.Add(command.Kind);
                return new(command.CommandId, ReceiptStatus.Unknown, "Delivery/result uncertain; reconcile before any new intent. " + exception.GetType().Name);
            }
            finally { _pending.TryRemove(command.CommandId, out _); }
        }
        finally { _send.Release(); }
    }
    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                int port = GsxPortDiscovery.Read(_options.IniPath);
                await using var socket = _socketFactory();
                lock (_sync) { _socket = socket; Connection = ConnectionState.Reconnecting; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(_options.ConnectTimeout);
                _diagnostic($"Connecting to ws://127.0.0.1:{port}/");
                await socket.ConnectAsync(new($"ws://127.0.0.1:{port}/"), timeout.Token).ConfigureAwait(false);
                string? hello = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (hello is null || GsxProtocol.Read(hello).Type != "hello") throw new InvalidDataException("Expected GSX hello.");
                Handle(GsxProtocol.Read(hello));
                await _send.WaitAsync(_stop.Token).ConfigureAwait(false);
                try { await socket.SendAsync(GsxProtocol.Subscribe, _stop.Token).ConfigureAwait(false); }
                finally { _send.Release(); }
                while (!_stop.IsCancellationRequested)
                {
                    string? text = await socket.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                    if (text is null) break;
                    var frame = GsxProtocol.Read(text);
                    Handle(frame);
                    if (frame.Type == "event" && frame.Running == true)
                    {
                        await _send.WaitAsync(_stop.Token).ConfigureAwait(false);
                        try { await socket.SendAsync("{\"type\":\"command\",\"verb\":\"state.get\"}", _stop.Token).ConfigureAwait(false); }
                        finally { _send.Release(); }
                    }
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException or JsonException or InvalidOperationException or KeyNotFoundException or NotSupportedException or ArgumentException or FormatException)
            { _diagnostic("GSX connection unavailable: " + exception.GetType().Name + ": " + exception.Message); }
            finally
            {
                lock (_sync) { _socket = null; _hello = false; _running = false; _hasSnapshot = false; _features = []; Connection = ConnectionState.ReconciliationRequired; InvalidateObservations(); }
                foreach (var pair in _pending) pair.Value.TrySetResult(new(pair.Key, ReceiptStatus.Unknown, "Connection interrupted; do not replay."));
            }
            try { await Task.Delay(_options.ReconnectDelay, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }
    private void Handle(RemoteFrame frame)
    {
        lock (_sync)
        {
            _receivedAt = DateTimeOffset.UtcNow;
            if (frame.MenuShown is { } shown) _menuShown = shown;
            if (frame.PromptActive is { } prompt) _promptActive = prompt;
            foreach (var pair in frame.Context)
            {
                if (_hasSnapshot && _context.TryGetValue(pair.Key, out string? previous) && !string.IsNullOrWhiteSpace(previous) && previous != pair.Value) { _generation++; _completed.Clear(); }
                _context = _context.SetItem(pair.Key, pair.Value);
            }
            if (frame.Type == "hello") { _generation++; _hello = true; _features = frame.Capabilities ?? []; _diagnostic("Verified Couatl hello / wire protocol 1."); }
            if (frame.HasHandlerData && _features.Contains("handlerData"))
            {
                var handler = frame.HandlerData;
                string? airport = handler is { ValueKind: JsonValueKind.Object } location ? GsxFlightReader.CurrentAirport(location) : null;
                string? stand = handler is { ValueKind: JsonValueKind.Object } gate ? GsxFlightReader.CurrentStand(gate) : null;
                if (_handlerAirport is not null && airport != _handlerAirport || _handlerStand is not null && stand != _handlerStand) { _generation++; _completed.Clear(); }
                _handlerAirport = airport; _handlerStand = stand;
                _flightPlan = handler is { ValueKind: JsonValueKind.Object } data ? GsxFlightReader.Read(data, frame.ObservedAt) : null;
                _aircraftType = handler is { ValueKind: JsonValueKind.Object } aircraft ? GsxFlightReader.AircraftType(aircraft) : null;
                _aircraftSetupKey = handler is { ValueKind: JsonValueKind.Object } setup ? GsxFlightReader.SetupKey(setup) : null;
            }
            if (frame.Running is { } running)
            {
                if (_running && !running) _generation++;
                _running = running;
                if (!running)
                {
                    _hasSnapshot = false; InvalidateObservations();
                    foreach (var pair in _pending) pair.Value.TrySetResult(new(pair.Key, ReceiptStatus.Unknown, "GSX engine went offline; reconcile before another intent."));
                }
            }
            if (frame.Services is { } services && _running)
            {
                if (frame.Type == "snapshot") _hasSnapshot = true;
                var next = services.ToImmutableDictionary(item => item.Kind);
                foreach (var removed in _completed.Keys.Except(next.Keys).ToArray()) _completed.Remove(removed);
                foreach (var removed in _observations.Keys.Except(next.Keys)) Publish(removed, ServiceState.Unknown, frame.ObservedAt, "service removed");
                foreach (var observation in services)
                {
                    if (observation.State == ServiceState.Completed)
                        _completed[observation.Kind] = new(EvidenceSource.Gsx, $"Couatl wire-1 {observation.Id}=completed/6", observation.ObservedAt);
                    else if (observation.State is ServiceState.Requested or ServiceState.Active or ServiceState.Unknown)
                        _completed.Remove(observation.Kind);
                    if (!_observations.TryGetValue(observation.Kind, out var prior) || prior.State != observation.State || prior.SemanticState != observation.SemanticState || prior.Waiting != observation.Waiting)
                        Publish(observation.Kind, observation.State, observation.ObservedAt, $"{observation.Id}={observation.SemanticState}/{observation.StateRaw}; waiting={observation.Waiting}");
                }
                _observations = next;
            }
            if (frame.ResultId is { } id && Guid.TryParse(id, out var commandId) && _pending.TryGetValue(commandId, out var completion))
            {
                _diagnostic($"RESULT {commandId:D} ok={frame.Ok} error={frame.Error}");
                completion.TrySetResult(new(commandId, frame.Ok == true ? ReceiptStatus.Accepted : ReceiptStatus.Rejected,
                    frame.Ok == true ? "GSX accepted the request; completion requires a subsequent authoritative observation." : $"GSX rejected request: {frame.Error}"));
            }
            Connection = Ready ? ConnectionState.Connected : ConnectionState.ReconciliationRequired;
        }
    }
    private void InvalidateObservations()
    {
        _completed.Clear();
        _flightPlan = null; _aircraftType = null; _aircraftSetupKey = null;
        _menuShown = false; _promptActive = false;
        _handlerAirport = null; _handlerStand = null;
        foreach (var kind in _observations.Keys) Publish(kind, ServiceState.Unknown, DateTimeOffset.UtcNow, "Connection/engine lost; reconciliation required");
        _observations = ImmutableDictionary<ServiceKind, RemoteServiceObservation>.Empty;
        _context = ImmutableDictionary<string, string?>.Empty;
    }
    private void Publish(ServiceKind kind, ServiceState state, DateTimeOffset at, string reference)
    {
        _diagnostic($"OBSERVED {kind} {state}: {reference}");
        var evidence = new Evidence(EvidenceSource.Gsx, "Couatl Remote API v2/wire-1 " + reference, at);
        _events.Writer.TryWrite(new(new(Guid.NewGuid(), _sessionId, at, EvidenceSource.Gsx, Guid.NewGuid(), evidence), kind, state));
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        Task<CommandReceipt>[] pending;
        lock (_sync) pending = _intents.Values.Select(intent => intent.Receipt).ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        _events.Writer.TryComplete();
        _send.Dispose();
        _stop.Dispose();
    }
}
