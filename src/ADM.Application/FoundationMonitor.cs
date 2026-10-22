using System.Threading.Channels;
using ADM.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ADM.Application;

/// <summary>Normalizes adapter input into one serialized observer. M0 sends no outbound commands.</summary>
public sealed class FoundationMonitor(IGsxGateway gsx, ISimulatorTelemetry simulator,
    ILogger<FoundationMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var events = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        logger.LogInformation("Offline demonstration monitor started with fake integrations. Live handling uses a separate explicit controller.");
        await Task.WhenAll(ObserveGsxAsync(), ObserveSimulatorAsync(), ConsumeAsync()).ConfigureAwait(false);

        async Task ObserveGsxAsync()
        {
            await foreach (var item in gsx.WatchAsync(stoppingToken).ConfigureAwait(false))
                await events.Writer.WriteAsync($"{item.Kind}: {item.State}; source={item.Metadata.Source}", stoppingToken).ConfigureAwait(false);
        }
        async Task ObserveSimulatorAsync()
        {
            await foreach (var item in simulator.WatchAsync(stoppingToken).ConfigureAwait(false))
                await events.Writer.WriteAsync($"Telemetry: {item.Connection}; source={item.Source}", stoppingToken).ConfigureAwait(false);
        }
        async Task ConsumeAsync()
        {
            await foreach (var item in events.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                logger.LogInformation("Adapter observation: {Observation}", item);
        }
    }
}
