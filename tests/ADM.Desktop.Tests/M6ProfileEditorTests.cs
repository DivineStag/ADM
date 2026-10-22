using System.Collections.Immutable;
using System.IO;
using ADM.Application;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Desktop.Resources;
using ADM.Desktop.Services;
using ADM.Domain;
using ADM.Infrastructure;

namespace ADM.Desktop.Tests;

public sealed class M6ProfileEditorTests
{
    private sealed class Clock : ADM.Contracts.IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Sound : INotificationPlayer
    {
        public event EventHandler<string>? PlaybackFailed { add { } remove { } }
        public void Play(double volume) { }
    }
    private sealed class Dialogs(string input, string output) : IProfileFileDialogs
    {
        public string? ChooseImport() => input;
        public string? ChooseExport(string airlineIcao) => output;
    }
    [Fact]
    public async Task Profile_commands_stage_import_and_export_without_rewriting_the_active_demo()
    {
        string directory = Path.Combine(Path.GetTempPath(), "adm-m6-editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.json"), output = Path.Combine(directory, "output.json");
            var transfer = new JsonProfileTransfer();
            await transfer.WriteAsync(input, SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 30, Realism = new(15, 2) }, CancellationToken.None);
            var personas = PersonaCatalogue.LoadBundled();
            var vm = new MainViewModel(new Clock(), new FoundationServicePlanResolver(new Clock()), new Sound(), new TransientUiPreferencesStore(),
                new LocalConversationEngine(personas), personas, profileTransfer: transfer, profileDialogs: new Dialogs(input, output));
            Guid activePlan = vm.Demo.Checkpoint.State.Session.Plan.SnapshotId;
            vm.ImportProfileCommand.Execute(null);
            for (int i = 0; i < 200 && !vm.ImportProfileCommand.CanExecute(null); i++) await Task.Delay(10);
            Assert.Equal(new RealismTuning(15, 2), vm.Profiles.EditedProfile!.Realism);
            Assert.Equal(1, vm.Profiles.EditedProfile.Revision);
            vm.ExportProfileCommand.Execute(null);
            for (int i = 0; i < 200 && !vm.ExportProfileCommand.CanExecute(null); i++) await Task.Delay(10);
            Assert.Equal(new RealismTuning(15, 2), (await transfer.ReadAsync(output, CancellationToken.None)).Realism);
            Assert.Equal(activePlan, vm.Demo.Checkpoint.State.Session.Plan.SnapshotId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Airline_tuning_is_validated_saved_in_the_candidate_and_kept_separate_between_airlines()
    {
        var editor = new ProfileEditorViewModel(new FoundationServicePlanResolver(new Clock()));
        editor.DelayProbabilityText = "60"; editor.MaximumDelayText = "4";
        Assert.Equal(new RealismTuning(60, 4), editor.EditedProfile!.Realism);
        editor.SelectedAirline = editor.Airlines.Single(item => item.Icao == "RYR"); Assert.Equal("", editor.DelayProbabilityText);
        editor.SelectedAirline = editor.Airlines.Single(item => item.Icao == "BAW"); Assert.Equal("60", editor.DelayProbabilityText);
        editor.DelayProbabilityText = "101"; Assert.NotEmpty(editor.SequenceError);
        editor.DelayProbabilityText = ""; Assert.Equal("", editor.SequenceError); Assert.Null(editor.EditedProfile!.Realism!.ProbabilityPercent);
    }
    [Fact]
    public void Imported_profile_is_staged_with_the_next_local_revision_and_preserves_other_operations()
    {
        var editor = new ProfileEditorViewModel(new FoundationServicePlanResolver(new Clock()));
        var local = SampleSessionFactory.GenericProfile() with { Icao = "BAW", Revision = 4 };
        editor.LoadProfiles(ImmutableDictionary<string, AirlineProfile>.Empty.Add("BAW", local));
        var imported = local with { Revision = 99, Realism = new(10, 2), DisplayName = "Imported preferences" };
        editor.StageImportedProfile(imported);
        Assert.Equal(5, editor.EditedProfile!.Revision); Assert.Equal(imported.Realism, editor.EditedProfile.Realism);
        Assert.Equal(local.Operations[FlightOperationType.FirstDeparture], editor.EditedProfile.Operations[FlightOperationType.FirstDeparture]);
        Assert.Contains("staged", editor.SequenceStatus, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Neutral_resources_and_persona_catalogue_cover_the_new_essential_events()
    {
        Assert.Equal("Operational realism", UiText.RealismTitle); Assert.NotEmpty(UiText.ImportProfile);
        var catalogue = PersonaCatalogue.LoadBundled();
        foreach (var key in new[] { "services.start.approval", "services.starting", "disruption.gate-staff", "disruption.resolved" })
            Assert.True(catalogue.Get(catalogue.Select(Guid.NewGuid()).Id).Templates[key].Length >= 2);
    }
}
