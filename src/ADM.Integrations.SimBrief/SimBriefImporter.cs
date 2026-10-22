using System.Globalization;
using System.Text.Json;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Integrations.SimBrief;

/// <summary>Explicit latest-OFP fetch only. No account data, full OFP or URL is logged or retained.</summary>
public sealed class SimBriefImporter(HttpClient client) : IFlightPlanImporter
{
    private const int MaximumBytes = 8 * 1024 * 1024;
    public async Task<FlightPlanResult> ImportLatestAsync(string pilotId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pilotId) || pilotId.Length > 100 || pilotId.Any(char.IsControl))
            return new(null, "Enter a SimBrief Pilot ID or Navigraph alias.");
        string value = pilotId.Trim();
        string key = value.All(char.IsAsciiDigit) ? "userid" : "username";
        var uri = new Uri($"https://www.simbrief.com/api/xml.fetcher.php?{key}={Uri.EscapeDataString(value)}&client=ADM&json=v2");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(null, "SimBrief could not supply the latest OFP. Check your alias/ID and generate a flight plan.");
            if (response.Content.Headers.ContentLength > MaximumBytes) return new(null, "SimBrief response exceeded the size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + count > MaximumBytes) return new(null, "SimBrief response exceeded the size limit.");
                buffer.Write(chunk, 0, count);
            }
            return new(Parse(buffer.ToArray()), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(null, "SimBrief request timed out. The existing flight was kept."); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(null, "SimBrief response could not be validated. The existing flight was kept."); }
    }
    public static FlightPlanSnapshot Parse(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        string Read(string group, string name) => root.GetProperty(group).GetProperty(name).GetString()?.Trim().ToUpperInvariant()
            ?? throw new JsonException("Missing flight-plan field.");
        string date = Read("params", "time_generated");
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var generated))
            throw new JsonException("Invalid OFP generation date.");
        return new(Read("atc", "callsign"), Read("origin", "icao_code"), Read("destination", "icao_code"),
            new(Read("aircraft", "icao_code"), null), generated);
    }
}
