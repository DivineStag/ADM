using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class ShellAndProfileTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
    private sealed class Sound : INotificationPlayer
    {
        public event EventHandler<string>? PlaybackFailed;
        public List<double> Volumes { get; } = [];
        public List<double> OutgoingVolumes { get; } = [];
        public void Play(double volume) => Volumes.Add(volume);
        public void PlayOutgoing(double volume) => OutgoingVolumes.Add(volume);
        public void Fail() => PlaybackFailed?.Invoke(this, "No audio device");
    }
    private static MainViewModel Create(Sound sound)
    {
        var personas = PersonaCatalogue.LoadBundled();
        return new(new Clock(), new FoundationServicePlanResolver(new Clock()), sound, new TransientUiPreferencesStore(), new LocalConversationEngine(personas), personas);
    }

    [Fact]
    public void Profiles_replace_services_and_navigation_has_four_clear_destinations()
    {
        var vm = Create(new());
        Assert.Equal(new[] { "Chat", "Profiles", "Flight", "Settings" }, Enum.GetNames<AppPage>());
        vm.NavigateCommand.Execute(AppPage.Profiles); Assert.True(vm.IsProfiles); Assert.False(vm.ShowDemoChat);
        vm.NavigateCommand.Execute(AppPage.Chat); Assert.True(vm.IsChat);
    }

    [Theory]
    [InlineData(true, false, 50)]
    [InlineData(false, true, 50)]
    [InlineData(false, false, 0)]
    public void Mute_dnd_and_zero_volume_suppress_sound(bool muted, bool dnd, double volume)
    {
        var sound = new Sound();
        var vm = Create(sound);
        vm.Muted = muted;
        vm.DoNotDisturb = dnd;
        vm.Volume = volume;
        vm.PreviewSoundCommand.Execute(null);
        vm.Demo.AdvanceCommand.Execute(null);
        vm.Demo.Send("How are you?");
        Assert.Empty(sound.Volumes);
        Assert.Empty(sound.OutgoingVolumes);
    }

    [Fact]
    public void Only_important_messages_ping_and_audio_failure_is_visible()
    {
        var sound = new Sound();
        var vm = Create(sound);
        vm.Volume = 30;
        vm.Demo.Send("Are we ready?");
        Assert.Empty(sound.Volumes);
        Assert.Equal(0.3, Assert.Single(sound.OutgoingVolumes));
        vm.Demo.AdvanceCommand.Execute(null);
        Assert.Equal(0.3, Assert.Single(sound.Volumes));
        sound.Fail();
        Assert.Equal("No audio device", vm.SoundStatus);
    }

    [Fact]
    public void Preview_profile_edits_resolve_exact_type_over_family_without_changing_demo()
    {
        var vm = Create(new());
        var original = vm.Demo.Services.Select(row => row.State).ToArray();
        Guid id = vm.Demo.DemoId;
        var row = vm.Profiles.Rows.Single(row => row.Kind == ServiceKind.Catering);
        row.Airline = ServicePolicy.Disabled;
        row.Family = OverrideMode.Automatic;
        row.Type = OverrideMode.Ask;
        var resolved = vm.Profiles.PreviewPlan.Services[ServiceKind.Catering];
        Assert.Equal(PolicyLayer.AircraftType, resolved.Source);
        Assert.Equal(ServicePolicy.Ask, resolved.Rule.Policy);
        var captured = vm.Profiles.PreviewPlan;
        row.Type = OverrideMode.Inherit;
        Assert.Equal(ServicePolicy.Automatic, vm.Profiles.PreviewPlan.Services[ServiceKind.Catering].Rule.Policy);
        Assert.Equal(ServicePolicy.Ask, captured.Services[ServiceKind.Catering].Rule.Policy);
        Assert.Equal(id, vm.Demo.DemoId);
        Assert.Equal(original, vm.Demo.Services.Select(service => service.State));
    }

    [Fact]
    public void Operation_and_airline_previews_keep_edits_separate()
    {
        var vm = Create(new());
        var editor = vm.Profiles;
        editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Airline = ServicePolicy.Disabled;
        editor.SelectedOperation = editor.Operations[0];
        Assert.Equal(ServicePolicy.Automatic, editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Airline);
        editor.SelectedOperation = editor.Operations[1];
        Assert.Equal(ServicePolicy.Disabled, editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Airline);
        editor.SelectedAirline = editor.Airlines[2];
        Assert.Equal(ServicePolicy.Automatic, editor.Rows.Single(row => row.Kind == ServiceKind.Catering).Airline);
        Assert.DoesNotContain(editor.Operations, operation => operation.Value == FlightOperationType.FinalArrival);
    }

    [Fact]
    public void Saved_service_order_waits_for_the_next_operation_and_survives_preferences_roundtrip()
    {
        var vm = Create(new()); Guid snapshot = vm.Demo.Checkpoint.State.Session.Plan.SnapshotId;
        vm.Profiles.Rows.Single(row => row.Kind == ServiceKind.Catering).Phase = 4;
        Assert.Equal(4, vm.Profiles.PreviewPlan.ServicePhases[ServiceKind.Catering]);
        vm.Profiles.SaveSequenceCommand.Execute(null);
        Assert.Equal(snapshot, vm.Demo.Checkpoint.State.Session.Plan.SnapshotId);
        Assert.Equal(ServiceState.Active, vm.Demo.Services.Single(row => row.Kind == ServiceKind.Catering).State);
        vm.Demo.Reset();
        Assert.Equal(4, vm.Demo.Checkpoint.State.Session.Plan.ServicePhases[ServiceKind.Catering]);
        Assert.Equal(ServiceState.Planned, vm.Demo.Services.Single(row => row.Kind == ServiceKind.Catering).State);
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "adm-sequence-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(directory, "appearance.json");
        try
        {
            var store = new JsonUiPreferencesStore(path); store.Save(vm.CapturePreferences());
            var loaded = store.Load(); Assert.Null(loaded.Warning);
            var editor = new ProfileEditorViewModel(new FoundationServicePlanResolver(new Clock()), loaded.Preferences.ServiceSequences);
            Assert.Equal(4, editor.SavedPhases("BAW", FlightOperationType.Turnaround)[ServiceKind.Catering]);
            Assert.Equal(3, editor.SavedPhases("RYR", FlightOperationType.Turnaround)[ServiceKind.Catering]);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory); }
    }
    [Fact]
    public void Invalid_phase_is_visible_cannot_be_saved_and_leaves_active_flight_unchanged()
    {
        var vm = Create(new()); Guid snapshot = vm.Demo.Checkpoint.State.Session.Plan.SnapshotId;
        vm.Profiles.Rows.Single(row => row.Kind == ServiceKind.Refuelling).Phase = 7;
        Assert.NotEmpty(vm.Profiles.SequenceError); Assert.False(vm.Profiles.SaveSequenceCommand.CanExecute(null));
        vm.Profiles.SaveSequenceCommand.Execute(null);
        Assert.Empty(vm.Profiles.SavedSequences); Assert.Equal(snapshot, vm.Demo.Checkpoint.State.Session.Plan.SnapshotId);
        vm.Profiles.Rows.Single(row => row.Kind == ServiceKind.Refuelling).Phase = 3;
        Assert.Empty(vm.Profiles.SequenceError); Assert.True(vm.Profiles.SaveSequenceCommand.CanExecute(null));
    }
}
