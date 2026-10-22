using ADM.Application;
using ADM.Contracts;
using ADM.Domain;
using ADM.Infrastructure;
using ADM.Integrations.Gsx;
using ADM.Integrations.Sim;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ADM.EndToEnd.Tests;

public sealed class FoundationCompositionTests
{
    [Fact]
    public async Task Host_starts_and_stops_offline_without_dispatching_services()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IOperationalClock, SystemOperationalClock>();
        builder.Services.AddSingleton<IServicePlanResolver, FoundationServicePlanResolver>();
        builder.Services.AddSingleton<SampleSessionFactory>();
        builder.Services.AddSingleton<IGsxGateway, FakeGsxGateway>();
        builder.Services.AddSingleton<ISimulatorTelemetry, FakeSimulatorTelemetry>();
        builder.Services.AddHostedService<FoundationMonitor>();
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        var factory = host.Services.GetRequiredService<SampleSessionFactory>();
        var first = factory.Create();
        var second = factory.Create();
        var session = first.Sectors[0].GroundHandling;
        Assert.NotEqual(session.Id, second.Sectors[0].GroundHandling.Id);
        Assert.NotEqual(session.Plan.SnapshotId, second.Sectors[0].GroundHandling.Plan.SnapshotId);
        Assert.Equal(session.Id, session.Dispatcher.SessionId);
        Assert.Equal(GateReadinessState.Unknown, session.Gate.State);
        Assert.Empty(session.Approvals);
        Assert.All(session.Services.Values, service => Assert.Equal(session.Plan.Services[service.Kind].Rule.Policy == ServicePolicy.Disabled ? ServiceState.NotRequired : ServiceState.Planned, service.State));
        var gsx = Assert.IsType<FakeGsxGateway>(host.Services.GetRequiredService<IGsxGateway>());
        var sim = Assert.IsType<FakeSimulatorTelemetry>(host.Services.GetRequiredService<ISimulatorTelemetry>());
        await sim.PublishAsync(new(DateTimeOffset.UtcNow, ConnectionState.Offline, null, null, null, null,
            null, null, null, null, EvidenceSource.Simulation), timeout.Token);
        Assert.Empty(gsx.SentCommands);
        await host.StopAsync(timeout.Token);
    }
}
