using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Infrastructure;
using ADM.Simulation;

namespace ADM.EndToEnd.Tests;

public sealed class M6RealismTests
{
    internal sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Scenario
    {
        public Clock Clock { get; } = new();
        public LocalDispatchSession Runtime { get; }
        public SeededDisruptionEngine Engine { get; } = new();
        public OperationalState State => Runtime.Checkpoint.State;
        public Scenario(int seed, RealismOptions? options = null, AirportGateProfile? gate = null, bool askWater = false)
        {
            Guid id = new(SHA256.HashData(Encoding.UTF8.GetBytes($"ADM M6 acceptance {seed}")).AsSpan(0, 16));
            var plan = LiveWorkflowTests.Plan(FlightOperationType.Turnaround, true);
            if (askWater) plan = plan with { Services = plan.Services.SetItem(ServiceKind.Water, plan.Services[ServiceKind.Water] with { Rule = plan.Services[ServiceKind.Water].Rule with { Policy = ServicePolicy.Ask } }) };
            var state = OperationalFactory.Create(id, Guid.NewGuid(), plan, new(id, PersonaCatalogue.LoadBundled().Select(id), Clock.UtcNow),
                Enum.GetValues<ServiceKind>().ToImmutableHashSet(), [], []) with
            {
                RequiresServiceStartApproval = true,
                RequestFuelAndCateringTogether = true,
                Realism = new(options ?? new(), gate ?? new(1, "GENERIC", 0, true), Clock.UtcNow)
            };
            Runtime = new(state, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), Clock);
            Tick(); Apply(new EvaluateOperation(Meta(EvidenceSource.System))); Runtime.Send("Yes, start services");
        }
        public EventMetadata Meta(EvidenceSource source = EvidenceSource.Simulation)
        {
            var id = Guid.NewGuid(); return new(id, State.Session.Id, Clock.UtcNow, source, id, new(source, "Explicit synthetic M6 acceptance event", Clock.UtcNow));
        }
        public void Apply(OperationEvent input) => Runtime.Accept(input);
        public void Tick()
        {
            var next = Engine.Advance(State, Clock.UtcNow);
            if (next != State.Realism) Apply(new UpdateOperationalRealism(Meta(), next));
        }
        public void CompletePreboarding()
        {
            for (int step = 0; step < 16; step++)
            {
                var service = State.Session.Services.Values.FirstOrDefault(service => service.Kind != ServiceKind.Boarding && service.State == ServiceState.RequestPending);
                if (service is null) return;
                var command = State.Commands.Last(command => command.Service == service.Kind);
                Assert.True(OperationalCommandPolicy.CanDispatch(State, command));
                Apply(new AcknowledgeCommand(Meta(EvidenceSource.System), command.Id, CommandOutcome.Accepted)); Tick();
                Apply(new ObserveService(Meta(), service.Kind, ServiceState.Active));
                Apply(new ObserveService(Meta(), service.Kind, ServiceState.Completed)); Tick();
            }
            throw new InvalidOperationException("Synthetic preboarding work did not settle.");
        }
        public void Finish()
        {
            Runtime.Send("Start boarding");
            var board = State.Commands.Last(command => command.Service == ServiceKind.Boarding);
            Assert.True(OperationalCommandPolicy.CanDispatch(State, board));
            Apply(new AcknowledgeCommand(Meta(EvidenceSource.System), board.Id, CommandOutcome.Accepted));
            Apply(new ObserveService(Meta(), ServiceKind.Boarding, ServiceState.Active));
            Apply(new ObserveService(Meta(), ServiceKind.Boarding, ServiceState.Completed));
            Runtime.Send("Close up and disconnect");
            Assert.Equal(WorkflowStage.Completed, State.Session.Stage);
        }
    }
    [Fact]
    public void Fifty_seeded_turnarounds_show_variety_without_duplicate_false_or_flooding_messages()
    {
        int incidents = 0; var kinds = new HashSet<DisruptionKind>(); var phrases = new HashSet<string>(); var report = new List<object>();
        for (int seed = 0; seed < 50; seed++)
        {
            var run = new Scenario(seed); var persona = run.State.Session.Dispatcher;
            run.CompletePreboarding();
            var incident = run.State.Realism!.Incident;
            if (incident is not null)
            {
                incidents++; kinds.Add(incident.Kind);
                Assert.Equal(3, incident.Effects.Length); Assert.All(incident.Effects, effect => Assert.Equal(incident.Id, effect.IncidentId));
                Assert.InRange((incident.ExpectedResolution - incident.StartedAt).TotalMinutes, 2, 7);
                Assert.NotEqual(QuestionKind.Boarding, run.State.Question?.Kind);
                Assert.Contains("Skip the gate delay", LocalConversationEngine.QuickReplies(run.Runtime.Context));
                Assert.DoesNotContain(run.State.Commands, command => command.Service == ServiceKind.Boarding);
                int count = run.Runtime.Checkpoint.Messages.Length;
                for (int poll = 0; poll < 100; poll++) run.Tick();
                Assert.Equal(count, run.Runtime.Checkpoint.Messages.Length);
                run.Clock.UtcNow = incident.ExpectedResolution.AddSeconds(1); run.Tick();
                Assert.All(run.State.Realism.Incident!.Effects, effect => Assert.False(effect.IsActive));
            }
            Assert.Equal(QuestionKind.Boarding, run.State.Question?.Kind);
            Assert.Equal(GateProvenance.Simulation, run.State.Session.Gate.Provenance);
            run.Finish();
            Assert.Equal(persona, run.State.Session.Dispatcher);
            Assert.Equal(run.State.Commands.Length, run.State.Commands.Select(command => command.Id).Distinct().Count());
            Assert.All(run.State.Commands.GroupBy(command => command.Service), group => Assert.Single(group));
            var messages = run.Runtime.Checkpoint.Messages;
            Assert.Equal(messages.Length, messages.Select(message => message.SemanticKey).Distinct().Count());
            Assert.Single(messages, message => message.SemanticKey == "services.starting");
            Assert.Single(messages, message => message.SemanticKey.StartsWith("boarding.approval", StringComparison.Ordinal));
            Assert.InRange(messages.Count(message => message.SemanticKey.StartsWith("disruption.", StringComparison.Ordinal)), 0, 1);
            Assert.All(messages.Where(message => message.SemanticKey.StartsWith("disruption.", StringComparison.Ordinal)), message => Assert.Contains("simulated", message.Text, StringComparison.OrdinalIgnoreCase));
            Assert.InRange(messages.Count(message => !message.IsSystem), 7, 20);
            Assert.InRange(messages.Count(message => message.IsSystem), 0, 12); // Full start/boarding/closure conversation including pilot replies and three manual checks.
            foreach (var message in messages.Where(message => message.SemanticKey == "services.starting")) phrases.Add(message.Text);
            report.Add(new
            {
                Seed = seed,
                Incident = incident?.Kind.ToString(),
                Minutes = incident is null ? 0 : (incident.ExpectedResolution - incident.StartedAt).TotalMinutes,
                Messages = messages.Count(message => !message.IsSystem),
                QuietUpdates = messages.Count(message => message.IsSystem),
                Commands = run.State.Commands.Length,
                Stage = run.State.Session.Stage.ToString(),
                Dispatcher = persona.Persona.Id
            });
        }
        Assert.InRange(incidents, 5, 22); Assert.True(kinds.Count >= 2); Assert.True(phrases.Count >= 4);
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "ADM.sln"))) root = Directory.GetParent(root)?.FullName;
        Assert.NotNull(root); Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        File.WriteAllText(Path.Combine(root, "artifacts", "m6-fifty-session-evidence.json"), JsonSerializer.Serialize(new { Sessions = 50, Incidents = incidents, Variants = phrases.Count, Runs = report }, new JsonSerializerOptions { WriteIndented = true }));
    }
    [Theory]
    [InlineData(false, 100, 10)]
    [InlineData(true, 0, 10)]
    [InlineData(true, 100, 0)]
    public void Disabled_zero_probability_and_zero_cap_never_generate_incidents(bool enabled, int probability, int maximum)
    {
        var run = new Scenario(3, new(enabled, probability, maximum)); run.CompletePreboarding();
        Assert.True(run.State.Realism!.DecisionMade); Assert.Null(run.State.Realism.Incident);
        var first = run.State.Realism;
        for (int i = 0; i < 50; i++) run.Tick();
        Assert.Equal(first, run.State.Realism);
    }
    [Fact]
    public void Skip_is_atomic_clears_all_linked_effects_and_cannot_clear_real_holds()
    {
        var run = new Scenario(7, new(true, 100, 3)); run.CompletePreboarding();
        var incident = run.State.Realism!.Incident!;
        var real = new OperationalHold(Guid.NewGuid(), HoldKind.RealServiceBlocked, "Real GSX blockage", true, null, null);
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.Gsx), real, Notify: false));
        run.Runtime.Send("Skip the gate delay");
        Assert.Equal(DisruptionStatus.Skipped, run.State.Realism!.Incident!.Status);
        Assert.All(run.State.Realism.Incident.Effects, effect => Assert.False(effect.IsActive));
        Assert.Contains(run.State.Session.Holds, hold => hold.Id == real.Id && hold.IsActive);
        Assert.DoesNotContain(run.State.Session.Holds, hold => hold.IsSimulated && hold.IsActive);
        Assert.NotEqual(QuestionKind.Boarding, run.State.Question?.Kind);
        run.Tick(); Assert.Equal(incident.Id, run.State.Realism.Incident.Id);
    }
    [Fact]
    public void Real_hold_supersedes_simulation_without_a_false_clear_message_or_second_incident()
    {
        var run = new Scenario(8, new(true, 100, 2)); run.CompletePreboarding();
        int messages = run.Runtime.Checkpoint.Messages.Length;
        run.Apply(new ChangeHold(run.Meta(EvidenceSource.Gsx), new(Guid.NewGuid(), HoldKind.RealServiceBlocked, "Real vehicle block", true, null, null), Notify: false));
        run.Tick();
        Assert.Equal(DisruptionStatus.SupersededByRealHold, run.State.Realism!.Incident!.Status);
        Assert.Equal(messages, run.Runtime.Checkpoint.Messages.Length);
        Assert.Contains("Real vehicle block", OperationalReducer.DelayDescription(run.State));
        run.Clock.UtcNow = run.Clock.UtcNow.AddHours(1); run.Tick();
        Assert.Equal(DisruptionStatus.SupersededByRealHold, run.State.Realism.Incident.Status);
    }
    [Fact]
    public void Airport_preparation_is_capped_labelled_and_does_not_replace_a_pilot_gate_attestation()
    {
        var run = new Scenario(1, new(true, 0, 1), new(1, "EGLL", 600, true)); run.CompletePreboarding();
        Assert.Equal(GateReadinessState.Preparing, run.State.Session.Gate.State);
        Assert.NotEqual(QuestionKind.Boarding, run.State.Question?.Kind);
        run.Clock.UtcNow = run.Clock.UtcNow.AddSeconds(60); run.Tick();
        Assert.Equal(GateReadinessState.Ready, run.State.Session.Gate.State);
        Assert.Equal(GateProvenance.Simulation, run.State.Session.Gate.Provenance);
        var meta = run.Meta(EvidenceSource.User);
        run.Apply(new ObserveGate(meta, new(GateReadinessState.Ready, GateProvenance.PilotAttested, meta.Evidence)));
        run.Tick(); Assert.Equal(GateProvenance.PilotAttested, run.State.Session.Gate.Provenance);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Simulation_resolution_does_not_revoke_a_pending_preboarding_service_instruction(bool skip)
    {
        var run = new Scenario(9, new(true, 100, 2), askWater: true); run.CompletePreboarding();
        Assert.Equal(QuestionKind.Service, run.State.Question?.Kind); run.Runtime.Send("Yes");
        var cleaning = run.State.Commands.Last(command => command.Service == ServiceKind.Water);
        if (skip) run.Runtime.Send("Skip the gate delay");
        else { run.Clock.UtcNow = run.State.Realism!.Incident!.ExpectedResolution.AddSeconds(1); run.Tick(); }
        Assert.True(OperationalCommandPolicy.CanDispatch(run.State, cleaning));
        Assert.Contains(ServiceKind.Water, run.State.ServicePermissions);
    }
    [Fact]
    public void Checkpoint_restore_preserves_seed_duration_persona_and_no_incident_reroll()
    {
        var run = new Scenario(17, new(true, 100, 4)); run.CompletePreboarding();
        var saved = DispatchCheckpointCodec.Deserialize(DispatchCheckpointCodec.Serialize(run.Runtime.Checkpoint));
        var restored = new LocalDispatchSession(saved, new LocalConversationEngine(PersonaCatalogue.LoadBundled()), run.Clock);
        Assert.Equal(run.State.Realism!.Incident!.Id, restored.Checkpoint.State.Realism!.Incident!.Id);
        Assert.Equal(run.State.Realism.Incident.ExpectedResolution, restored.Checkpoint.State.Realism.Incident.ExpectedResolution);
        Assert.Equal(run.State.Session.Dispatcher, restored.Checkpoint.State.Session.Dispatcher);
        var next = run.Engine.Advance(restored.Checkpoint.State, run.Clock.UtcNow.AddMinutes(10));
        Assert.Equal(run.State.Realism.Incident.Id, next.Incident!.Id); Assert.Equal(DisruptionStatus.Resolved, next.Incident.Status);
    }
}
