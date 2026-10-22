using System.Text.Json;
using ADM.Contracts;
using ADM.Domain;
using ADM.Integrations.Gsx;
using ADM.Integrations.Sim;

namespace ADM.IntegrationProof;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        int seconds = 30;
        ServiceKind? requested = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--seconds" && ++index < args.Length && int.TryParse(args[index], out int duration) && duration is >= 5 and <= 1800) seconds = duration;
            else if (args[index] == "--request-service" && ++index < args.Length && Enum.TryParse<ServiceKind>(args[index], false, out var kind) &&
                Enum.IsDefined(kind) && kind is not (ServiceKind.Boarding or ServiceKind.Deboarding)) requested = kind;
            else
            {
                Console.Error.WriteLine("Usage: ADM.IntegrationProof [--seconds 5..1800] [--request-service Water|Lavatory|Refuelling|Catering|Cleaning]");
                return 1;
            }
        }
        Directory.CreateDirectory("artifacts/integration-proof");
        string logPath = Path.GetFullPath(Path.Combine("artifacts/integration-proof", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl"));
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        object sync = new();
        void Record(string category, object payload)
        {
            string line = JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, category, payload });
            lock (sync) { log.WriteLine(line); Console.WriteLine(line); }
        }
        Record("setup", new { session = "M3 manual integration proof", sdkCompiled = ManagedSimConnectSession.SdkAvailable, requested = requested?.ToString(), seconds });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            Guid sessionId = Guid.NewGuid();
            await using var gateway = new GsxRemoteGateway(sessionId, diagnostic: message => Record("gsx", message));
            var telemetry = new SimConnectTelemetry(diagnostic: message => Record("simconnect", message));
            SimulatorSnapshot? latest = null;
            bool simObserved = false, attempted = false, accepted = false, observedTransition = false, gsxObserved = false;
            object stateLock = new();
            var simTask = ConsumeSimulator();
            var gsxTask = ConsumeGsx();
            gateway.Start();
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var capabilities = await gateway.ProbeAsync(stop.Token);
                    if (gateway.Connection == ConnectionState.Connected && capabilities.RequestableServices.Count > 0) gsxObserved = true;
                    SimulatorSnapshot? snapshot;
                    lock (stateLock) snapshot = latest;
                    if (requested is { } service && !attempted && gateway.Connection == ConnectionState.Connected &&
                        snapshot is { Connection: ConnectionState.Connected, AircraftTitle.Length: > 0 } &&
                        DateTimeOffset.UtcNow - snapshot.ObservedAt < TimeSpan.FromSeconds(3) &&
                        gateway.Observations.TryGetValue(service, out var observation) && observation.CanTrigger && !observation.Waiting)
                    {
                        lock (stateLock) attempted = true;
                        var receipt = await gateway.RequestServiceAsync(new(Guid.NewGuid(), sessionId, service, 1, Guid.NewGuid(), "M3 explicit manual proof"), stop.Token);
                        accepted = receipt.Status == ReceiptStatus.Accepted;
                        Record("receipt", receipt);
                    }
                    await Task.Delay(250, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            await Task.WhenAll(simTask, gsxTask);
            Record("flightData", new
            {
                simulatorTitle = latest?.AircraftTitle,
                gsxTitle = gateway.Snapshot.FlightPlan?.SourceAircraftTitle,
                gateway.Snapshot.AircraftIcaoType,
                gateway.Snapshot.AirportIcao,
                plan = gateway.Snapshot.FlightPlan,
                gateway.Snapshot.AwaitingInput,
                gateway.Snapshot.MenuOpen
            });
            bool passed = gsxObserved && simObserved && (requested is null || accepted && observedTransition);
            Record("summary", new
            {
                passed,
                gsxObserved,
                simObserved,
                attempted,
                accepted,
                observedTransition,
                logPath,
                note = "Acceptance is not completion. No automatic workflow, cancellation, closure or pushback was enabled."
            });
            return passed ? 0 : 2;

            async Task ConsumeSimulator()
            {
                try
                {
                    await foreach (var snapshot in telemetry.WatchAsync(stop.Token))
                    {
                        lock (stateLock) { latest = snapshot; simObserved |= snapshot.Connection == ConnectionState.Connected; }
                        Record("telemetry", new
                        {
                            snapshot.ObservedAt,
                            connection = snapshot.Connection.ToString(),
                            snapshot.OnGround,
                            snapshot.GroundSpeedKnots,
                            snapshot.ParkingBrakeSet,
                            snapshot.EnginesRunning,
                            source = snapshot.Source.ToString()
                        });
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
            async Task ConsumeGsx()
            {
                try
                {
                    await foreach (var observation in gateway.WatchAsync(stop.Token))
                    {
                        lock (stateLock)
                            if (attempted && observation.Kind == requested && observation.State is ServiceState.Requested or ServiceState.Active or ServiceState.Completed)
                                observedTransition = true;
                        Record("service", new { service = observation.Kind.ToString(), state = observation.State.ToString(), observation.Metadata });
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Record("failure", new { error = exception.GetType().Name, exception.Message }); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
