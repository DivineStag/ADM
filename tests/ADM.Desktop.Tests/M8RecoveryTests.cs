using System.Text.Json;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class M8RecoveryTests
{
    private static RecoveryFacts Ready => new(true, true, true, true, false, false, false, null, false, false);
    [Fact]
    public void Normal_handling_does_not_show_redundant_fix_controls()
    {
        Assert.Equal(RecoveryAction.None, RecoveryGuide.Choose(Ready).Action);
        Assert.Equal(RecoveryAction.None, RecoveryGuide.Choose(Ready with { NeedsSetup = true, Connected = false }).Action);
        Assert.Equal(RecoveryAction.None, RecoveryGuide.Choose(Ready with { Completed = true, Armed = false }).Action);
    }
    [Fact]
    public void Disconnection_offers_retry_instead_of_a_service_restart()
    {
        var step = RecoveryGuide.Choose(Ready with { Connected = false, Restartable = ServiceKind.Refuelling });
        Assert.Equal(RecoveryAction.Reconnect, step.Action); Assert.Equal("Retry connection", step.Button); Assert.Null(step.Service);
    }
    [Theory]
    [InlineData(ServiceKind.Refuelling, "Restart fuel")]
    [InlineData(ServiceKind.Catering, "Restart catering")]
    public void Only_a_known_available_uncertain_service_gets_a_specific_restart_button(ServiceKind kind, string label)
    {
        var step = RecoveryGuide.Choose(Ready with { Restartable = kind });
        Assert.Equal(RecoveryAction.RestartService, step.Action); Assert.Equal(kind, step.Service); Assert.Equal(label, step.Button);
        Assert.Contains("does not confirm completion", step.Detail);
    }
    [Fact]
    public void Paused_unknown_delivery_offers_review_without_promising_to_replay_it()
    {
        Assert.Equal(RecoveryAction.Resume, RecoveryGuide.Choose(Ready with { Armed = false }).Action);
        Assert.Equal(RecoveryAction.OpenGsx, RecoveryGuide.Choose(Ready with { HasError = true }).Action);
        Assert.Equal(RecoveryAction.EnterFlight, RecoveryGuide.Choose(Ready with { HasFlight = false, HasOperation = false }).Action);
        Assert.Equal(RecoveryAction.Prepare, RecoveryGuide.Choose(Ready with { HasError = true, HasOperation = false }).Action);
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GSX_input_offers_the_provider_UI_before_any_new_request(bool prompt, bool menu)
    {
        Assert.Equal(RecoveryAction.OpenGsx, RecoveryGuide.Choose(Ready with { PromptOpen = prompt, MenuOpen = menu, Restartable = ServiceKind.Refuelling }).Action);
    }
    [Fact]
    public void Missing_runtime_repairs_ADM_without_requiring_a_pilot_to_install_the_developer_SDK()
    {
        var step = RecoveryGuide.Choose(Ready with { SimulatorRuntimeAvailable = false, Connected = false });
        Assert.Equal(RecoveryAction.RepairInstall, step.Action); Assert.Equal("Repair installation", step.Button);
        Assert.DoesNotContain("SDK", step.Detail);
    }
    [Fact]
    public void Export_contains_only_allowlisted_diagnostic_fields()
    {
        var report = new SupportReport("0.8.0.0", DateTimeOffset.UnixEpoch, "Connected", "Connected", "Ground services", false, false, "None",
            new Dictionary<ServiceKind, ServiceState> { [ServiceKind.Water] = ServiceState.Active });
        using var json = JsonDocument.Parse(SupportReportJson.Serialize(report));
        Assert.Equal(new[] { "AppVersion", "CreatedAt", "GsxConnection", "MenuOpen", "PromptOpen", "Services", "SimulatorConnection", "Stage", "SuggestedAction" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal("Active", json.RootElement.GetProperty("Services").GetProperty("Water").GetString());
        Assert.DoesNotContain("C:\\", SupportReportJson.Serialize(report));
    }
}
