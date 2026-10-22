using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ADM.Desktop.Presentation;
using ADM.Desktop.Views;
using ADM.Domain;
namespace ADM.Desktop.Verification;

internal static class UiVerification
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm)
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui"); Directory.CreateDirectory(directory);
        var checks = new List<string>(); var trace = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            Require(window.WindowStyle == WindowStyle.SingleBorderWindow && !window.Topmost && !window.AllowsTransparency && window.Icon is not null,
                "Windows owns the frame, icon and input bounds; ADM is not a transparent or topmost overlay.");
            vm.Muted = true; window.Width = 490; window.Height = 860; await Layout(window);
            Require(vm.IsChat && Descendants<Button>(window).Any(button => Equals(button.Content, "Profiles")), "Four clear tabs start with Chat and include Profiles.");
            Require(!Descendants<Button>(window).Any(button => Equals(button.Content, "View all services")), "The Services destination and service rail are removed.");
            await Navigate(window.ProfilesNav); await Layout(window); VerifyAccessibleControls(window);
            Require(vm.IsProfiles && window.ProfilesPage.IsVisible, "Profiles navigation opens the dedicated editor.");
            Require(vm.Profiles.Operations.Count == 2 && vm.Profiles.Operations.All(operation => operation.Value != FlightOperationType.FinalArrival) && vm.Live!.OperationChoices.Count == 2,
                "Final arrival is removed from both handling and profile choices.");
            Require(vm.Profiles.Rows.Count == 6 && vm.Profiles.Rows.All(row => row.Kind != ServiceKind.Deboarding), "Deboarding remains absent from the profile editor.");
            Capture(window, directory, "m8-profiles-dark");
            vm.Profiles.SelectedScope = vm.Profiles.Scopes.Single(scope => scope.Key == "type"); await Layout(window);
            var setting = Descendants<ComboBox>(window.ProfilesPage).Single(control => AutomationProperties.GetName(control) == "Catering service setting");
            setting.SelectedItem = OverrideMode.Disabled; await Layout(window);
            Require(vm.Profiles.PreviewPlan.Services[ServiceKind.Catering].Rule.Policy == ServicePolicy.Disabled, "The single service choice edits the selected aircraft profile.");
            var order = Descendants<Expander>(window.ProfilesPage).Single(control => Equals(control.Header, "Service order"));
            order.IsExpanded = true; await Layout(window);
            var fuel = Descendants<ComboBox>(window.ProfilesPage).Single(control => AutomationProperties.GetName(control) == "Refuelling service phase");
            fuel.SelectedItem = 7; await Layout(window); Require(vm.Profiles.SequenceError.Length > 0 && !vm.SaveProfileCommand.CanExecute(null), "Unsafe fuel-before-boarding ordering cannot be saved.");
            fuel.SelectedItem = 3; await Layout(window); order.IsExpanded = false;
            await Navigate(Descendants<Button>(window.ProfilesPage).Single(button => Equals(button.Content, "Save profile")));
            for (int wait = 0; wait < 100 && !vm.SaveProfileCommand.CanExecute(null); wait++) await Task.Delay(20);
            Require(vm.Live!.SavedProfiles.ContainsKey("BAW"), "Saving from Profiles persists a validated future profile.");
            var add = Descendants<Expander>(window.ProfilesPage).Single(control => Equals(control.Header, "Add an airline")); add.IsExpanded = true; await Layout(window);
            Descendants<TextBox>(add).Single(control => AutomationProperties.GetName(control) == "New airline ICAO code").Text = "XYZ";
            Descendants<TextBox>(add).Single(control => AutomationProperties.GetName(control) == "New airline name").Text = "My test airline";
            await Navigate(Descendants<Button>(add).Single(button => Equals(button.Content, "Create airline draft"))); await Layout(window);
            Require(vm.Profiles.SelectedAirline.Icao == "XYZ", "Add an airline creates an independent editable profile."); add.IsExpanded = false;
            await Navigate(window.SettingsNav); await Layout(window);
            Require(vm.IsSettings && !Descendants<ComboBox>(window.SettingsPage).Any(control => AutomationProperties.GetName(control) == "Preview airline"), "Settings no longer duplicates the profile editor.");
            Require(!Descendants<CheckBox>(window.SettingsPage).Any(control => Equals(control.Content, "Do not disturb")), "Redundant sound and automation controls are removed.");
            VerifyAccessibleControls(window); Capture(window, directory, "m8-settings-dark");
            var focusProbe = new Window { Width = 120, Height = 100, Left = 10, Top = 10, ShowInTaskbar = false, Title = "ADM isolated focus check" };
            try
            {
                focusProbe.Show(); focusProbe.Activate(); await Task.Delay(100);
                window.Activate(); await Task.Delay(100);
                Require(window.IsActive, "The input test activates ADM before testing focus release.");
                window.SettingsPage.ThemePicker.IsDropDownOpen = true; await Layout(window);
                focusProbe.Activate(); await Task.Delay(100); await Layout(focusProbe);
                Require(!window.SettingsPage.ThemePicker.IsDropDownOpen, "Dropdowns close when ADM yields focus to another window.");
            }
            finally { focusProbe.Close(); window.Activate(); }
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            NativeInputChecks.AssertNoOutsideInput(hwnd);
            Require(true, "Native hit tests outside ADM's visible bounds do not belong to ADM.");
            window.SettingsPage.RealismSettings.IsExpanded = true; await Layout(window);
            Descendants<Slider>(window.SettingsPage).Single(control => AutomationProperties.GetName(control) == "Simulated delay probability percent").Value = 35;
            Require(vm.CapturePreferences().DelayProbabilityPercent == 35, "Optional delay limits retain their saved binding."); window.SettingsPage.RealismSettings.IsExpanded = false;
            window.SettingsPage.ThemePicker.SelectedItem = "Light"; await Layout(window); Require(vm.Theme == "Light", "Light theme applies through the Settings selector."); Capture(window, directory, "m8-settings-light");
            await Navigate(window.FlightNav); await Layout(window);
            Require(!Descendants<CheckBox>(window.FlightPage).Any() && !Descendants<Button>(window.FlightPage).Any(button => Equals(button.Content, "Reconcile current states")), "Flight removes the recovery console and setup checklists.");
            var manual = Descendants<Expander>(window.FlightPage).Single(control => Equals(control.Header, "Enter a flight manually")); manual.IsExpanded = true; await Layout(window);
            foreach (var field in new[] { ("Manual ATC callsign", "BAW482"), ("Manual origin airport", "EGLL"), ("Manual destination airport", "EHAM"), ("Manual ICAO aircraft type", "A20N") })
                Descendants<TextBox>(manual).Single(control => AutomationProperties.GetName(control) == field.Item1).Text = field.Item2;
            await Navigate(Descendants<Button>(manual).Single(button => Equals(button.Content, "Use manual flight")));
            for (int wait = 0; wait < 100 && !vm.Live.FlightLabel.Contains("BAW482", StringComparison.Ordinal); wait++) await Task.Delay(20);
            Require(vm.Profiles.SelectedAirline.Icao == "BAW" && vm.Profiles.AircraftType == "A20N", "Imported flight context selects its airline and aircraft in Profiles.");
            Require(vm.Live.FlightLabel.Contains("BAW482", StringComparison.Ordinal) && !vm.Live.HasOperation, "Manual flight entry remains an optional fallback without dispatching services.");
            manual.IsExpanded = false; await Layout(window); Capture(window, directory, "m8-flight-light");
            await Navigate(window.ChatNav); window.Width = 420; window.Height = 640; await Layout(window);
            Require(window.Chat.Composer.ActualWidth > 180 && window.ChatNav.ActualHeight >= 44, "The minimum phone window keeps composer and navigation usable.");
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.Chat.Composer), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            vm.Demo.Draft = "Are we ready?"; window.Chat.Composer.RaiseEvent(enter); Require(enter.Handled && vm.Demo.Draft == "", "Enter sends through the shared conversation command.");
            vm.Demo.Reset(); vm.Demo.AdvanceCommand.Execute(null); Require(vm.Demo.Stage == DemoStage.BoardingApproval, "Handling still waits for boarding approval.");
            vm.Demo.Send("Yes"); vm.Demo.AdvanceCommand.Execute(null); Require(vm.Demo.Stage == DemoStage.ClosureApproval, "Boarding still waits for separate closure approval.");
            vm.Demo.Send("Yes"); vm.Demo.AdvanceCommand.Execute(null); Require(vm.Demo.Stage == DemoStage.Complete, "The sample handover leaves pushback with the pilot.");
            window.Width = 1000; window.Height = 860; await Layout(window); Capture(window, directory, "m8-tablet-chat");
            var original = window.Content;
            try
            {
                vm.Theme = "Dark"; window.Width = 420; window.Height = 640;
                var fixture = new ChatFixture(); var chat = new LiveChatView { DataContext = fixture, Margin = new Thickness(22) }; window.Content = chat; await Layout(window);
                Require(!Descendants<Button>(chat).Single(button => Equals(button.Content, "Save aircraft setup")).IsEnabled, "First-use setup needs its single review acknowledgement.");
                fixture.SetupReviewed = true; await Navigate(Descendants<Button>(chat).Single(button => Equals(button.Content, "Save aircraft setup"))); await Layout(window);
                Require(!fixture.NeedsSetup, "The setup acknowledgement dismisses the setup card.");
                Require(Descendants<TextBlock>(chat).Any(text => text.Name == "SystemUpdate" && text.Text == "Water started" && text.IsVisible), "Quiet service lines appear without dispatcher attribution.");
                Require(Descendants<Button>(chat).Single(button => Equals(button.Content, "Retry connection")).IsVisible, "A problem displays a specific recovery button directly in Chat.");
                await Navigate(Descendants<Button>(chat).Single(button => Equals(button.Content, "Retry connection"))); Require(fixture.RecoveryClicks == 1, "The recovery button invokes exactly one bound action.");
                Capture(window, directory, "m8-chat-recovery-dark"); vm.Theme = "Light"; await Layout(window); Capture(window, directory, "m8-chat-recovery-light");
            }
            finally { window.Content = original; }
            vm.Live.AutoConnect = false; await vm.Live.StartAutomaticAsync(); await Layout(window);
            Require(vm.IsLive && vm.Live.HasRecovery && vm.Live.RecoveryButton == "Retry connection" && !vm.Live.HasOperation, "A real disconnected live workspace offers connection recovery without starting a sample.");
            var player = new MediaPlayer(); var decoded = new TaskCompletionSource<bool>(); player.MediaOpened += (_, _) => decoded.TrySetResult(true); player.MediaFailed += (_, args) => decoded.TrySetException(args.ErrorException);
            try { player.Open(new Uri(ADM.Desktop.Services.NotificationPlayer.AssetPath(AppContext.BaseDirectory))); await decoded.Task.WaitAsync(TimeSpan.FromSeconds(8)); Require(player.NaturalDuration.HasTimeSpan, "The selected local notification sound decodes without playing it."); } finally { player.Close(); }
            Require(trace.Errors.Count == 0, "No WPF binding errors across navigation, themes, profiles or recovery.");
            File.WriteAllLines(Path.Combine(directory, "ui-results.txt"), checks);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(trace); }
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + "\n" + string.Join('\n', trace.Errors)); checks.Add("PASS: " + message); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task Navigate(Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
        if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider provider)
            throw new InvalidOperationException("Button does not expose the Invoke accessibility pattern.");
        provider.Invoke();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static void VerifyAccessibleControls(DependencyObject root)
    {
        if (root is Control control && control.IsVisible && control.Focusable &&
            control is Button or TextBox or ComboBox or CheckBox or Slider)
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(control);
            if (peer is null || string.IsNullOrWhiteSpace(peer.GetName()))
                throw new InvalidOperationException($"Accessible name is missing for {control.GetType().Name}.");
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) VerifyAccessibleControls(VisualTreeHelper.GetChild(root, index));
    }
    private static async Task Layout(Window window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static void Capture(Window window, string directory, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private sealed class ChatFixture : ObservableObject
    {
        private bool _setup = true, _reviewed;
        public bool NeedsSetup { get => _setup; private set { Set(ref _setup, value); Raise(nameof(HasRecovery)); } }
        public bool SetupReviewed { get => _reviewed; set { Set(ref _reviewed, value); CompleteSetupCommand.Refresh(); } }
        public bool ShowWelcome => false;
        public string WelcomeMessage => "";
        public bool HasOperation => true;
        public bool CanChat => true;
        public string ChatStatus => "Offline UI fixture";
        public string Draft { get; set; } = "";
        public bool HasRecovery => !NeedsSetup;
        public string RecoveryTitle => "Let's reconnect";
        public string RecoveryDetail => "Start MSFS and let GSX load. I'll check the connection again.";
        public string RecoveryButton => "Retry connection";
        public bool HasGsxShortcut => false;
        public int RecoveryClicks { get; private set; }
        public RelayCommand CompleteSetupCommand { get; }
        public RelayCommand RecoveryCommand { get; }
        public RelayCommand OpenGsxCommand { get; } = new(_ => { });
        public RelayCommand ClearChatCommand { get; } = new(_ => { });
        public RelayCommand SendCommand { get; } = new(_ => { });
        public RelayCommand QuickReplyCommand { get; } = new(_ => { });
        public ObservableCollection<DemoMessage> Messages { get; } = [new("Sarah", "I'll take care of the services.", DateTimeOffset.UtcNow, false), new("", "Water started", DateTimeOffset.UtcNow, false) { IsSystem = true }];
        public ObservableCollection<string> QuickReplies { get; } = [];
        public ChatFixture() { CompleteSetupCommand = new(_ => NeedsSetup = false, _ => SetupReviewed); RecoveryCommand = new(_ => RecoveryClicks++); }
    }
}
