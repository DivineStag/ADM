using System.IO;
using System.Windows;
using ADM.Application;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Desktop.Services;
using ADM.Desktop.Verification;
using ADM.Infrastructure;
using ADM.Integrations.Gsx;
using ADM.Integrations.Sim;
using ADM.Operations;
using ADM.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ADM.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private Mutex? _instance;
    private bool _testMode;
    private string? _repairInstaller;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _testMode = e.Args.Contains("--smoke-test", StringComparer.Ordinal) || e.Args.Contains("--ui-test", StringComparer.Ordinal);
            if (!_testMode)
            {
                _instance = new Mutex(true, "ADM.Desktop.SingleInstance", out bool firstInstance);
                if (!firstInstance) { MessageBox.Show("ADM is already open. Use its taskbar icon to return to it.", "Aircraft Dispatch Manager"); Shutdown(); return; }
            }
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.Logging.AddProvider(new LocalFileLoggerProvider(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADM", "logs")));
            builder.Services.AddSingleton<IOperationalClock, SystemOperationalClock>();
            builder.Services.AddSingleton<IServicePlanResolver, FoundationServicePlanResolver>();
            builder.Services.AddSingleton<IAirlineIdentityResolver>(new AirlineIdentityResolver(["RYR", "BAW", "UAL"]));
            builder.Services.AddSingleton<IGsxGateway, FakeGsxGateway>();
            builder.Services.AddSingleton<ISimulatorTelemetry, FakeSimulatorTelemetry>();
            builder.Services.AddSingleton<SampleSessionFactory>();
            builder.Services.AddHostedService<FoundationMonitor>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton(_ => new GitHubUpdates(new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }),
                _testMode ? "" : GitHubUpdates.ConfiguredRepository,
                typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0]));
            builder.Services.AddSingleton<ISupportReportExporter, SupportReportExporter>();
            builder.Services.AddSingleton<IGsxHelpLauncher, GsxHelpLauncher>();
            builder.Services.AddSingleton<IInstallationRepair>(_ => _testMode ? new VerificationInstallationRepair() : new InstallationRepair());
            string dataDirectory = _testMode ? Path.Combine(Environment.CurrentDirectory, "artifacts", "ui", "isolated", Guid.NewGuid().ToString("N")) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADM");
            builder.Services.AddSingleton(provider => new JsonAirlineProfileRepository(Path.Combine(dataDirectory, "profiles"),
                message => provider.GetRequiredService<ILogger<JsonAirlineProfileRepository>>().LogWarning("{ProfileDiagnostic}", message),
                Path.Combine(AppContext.BaseDirectory, "Defaults", "Profiles")));
            builder.Services.AddSingleton<IAirlineProfileRepository>(provider => provider.GetRequiredService<JsonAirlineProfileRepository>());
            builder.Services.AddSingleton(new SqliteDispatchStore(Path.Combine(dataDirectory, "dispatch.db")));
            builder.Services.AddSingleton<IDispatchStore>(provider => provider.GetRequiredService<SqliteDispatchStore>());
            builder.Services.AddSingleton<IFlightPlanCache>(provider => provider.GetRequiredService<SqliteDispatchStore>());
            builder.Services.AddSingleton(new JsonAircraftCapabilityCatalog(Path.Combine(dataDirectory, "aircraft-capabilities.json")));
            builder.Services.AddSingleton<IOperationalRealismEngine, SeededDisruptionEngine>();
            builder.Services.AddSingleton<IAirportGateProfiles>(provider => new JsonAirportGateProfiles(Path.Combine(dataDirectory, "airport-profiles.json"),
                message => provider.GetRequiredService<ILogger<JsonAirportGateProfiles>>().LogWarning("{GateProfileDiagnostic}", message)));
            builder.Services.AddSingleton<IProfileTransfer, JsonProfileTransfer>();
            builder.Services.AddSingleton<IProfileFileDialogs, ProfileFileDialogs>();
            builder.Services.AddSingleton<IFlightPlanImporter, GsxOnlyFlightImporter>();
            builder.Services.AddSingleton<IAssistantSetupStore>(_ => _testMode ? new MemoryAssistantSetupStore() : new JsonAssistantSetupStore(Path.Combine(dataDirectory, "setup.json")));
            builder.Services.AddSingleton<LiveOperationsViewModel>();
            builder.Services.AddSingleton(PersonaCatalogue.LoadBundled());
            builder.Services.AddSingleton<IConversationEngine, LocalConversationEngine>();
            builder.Services.AddSingleton<IGateReadinessCoordinator, GateReadinessCoordinator>();
            builder.Services.AddSingleton<INotificationPlayer, NotificationPlayer>();
            builder.Services.AddSingleton<IUiPreferencesStore>(_ => _testMode
                ? new TransientUiPreferencesStore()
                : new JsonUiPreferencesStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADM", "appearance.json")));
            builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build();
            await _host.StartAsync();
            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            if (viewModel.Live is not null)
            {
                try { await viewModel.Live.InitializeAsync(); viewModel.Profiles.LoadProfiles(viewModel.Live.SavedProfiles); }
                catch (Exception exception)
                {
                    if (_testMode) throw;
                    viewModel.Live.ReportError("Saved operation could not be loaded. Live control remains stopped. " + exception.Message);
                }
            }
            if (viewModel.Live is not null) viewModel.Live.RepairRequested += installer => { _repairInstaller = installer; MainWindow?.Close(); };
            viewModel.Updates.InstallerReady += installer => { _repairInstaller = installer; MainWindow?.Close(); };
            ApplyTheme(viewModel.Theme);
            viewModel.ThemeChanged += (_, _) => ApplyTheme(viewModel.Theme);
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            if (e.Args.Contains("--ui-test", StringComparer.Ordinal))
            {
                bool verificationStarted = false;
                MainWindow.ContentRendered += async (_, _) =>
                {
                    if (verificationStarted) return;
                    verificationStarted = true;
                    try
                    {
                        await UiVerification.RunAsync((MainWindow)MainWindow, viewModel);
                        _host.Services.GetRequiredService<ILogger<App>>().LogInformation("M2 UI verification passed.");
                        MainWindow.Close();
                    }
                    catch (Exception exception)
                    {
                        Directory.CreateDirectory("artifacts/ui");
                        File.WriteAllText("artifacts/ui/ui-failure.txt", exception.ToString());
                        Shutdown(1);
                    }
                };
            }
            if (e.Args.Contains("--smoke-test", StringComparer.Ordinal))
            {
                MainWindow.ContentRendered += async (_, _) =>
                {
                    var logger = _host.Services.GetRequiredService<ILogger<App>>();
                    logger.LogInformation("M2 smoke test: WPF window rendered, viewmodel and fake adapters resolved.");
                    await Task.Delay(1000);
                    MainWindow.Close();
                };
            }
            MainWindow.Show();
            if (!_testMode) viewModel.Updates.CheckCommand.Execute(null);
            if (!_testMode && viewModel.Live is not null) { viewModel.Live.AutoConnect = true; await viewModel.Live.StartAutomaticAsync(); }
        }
        catch (Exception exception)
        {
            if (_testMode)
            {
                Directory.CreateDirectory("artifacts/ui");
                File.WriteAllText("artifacts/ui/ui-failure.txt", exception.ToString());
                Shutdown(1);
                return;
            }
            MessageBox.Show($"ADM could not start.\n{exception.Message}", "ADM startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private sealed class VerificationInstallationRepair : IInstallationRepair
    {
        public bool RuntimeAvailable => true;
        public string? FindInstaller() => null;
    }

    internal void ApplyTheme(string theme) => Resources.MergedDictionaries[0] = new ResourceDictionary
    { Source = new Uri($"Themes/{theme}.xaml", UriKind.Relative) };

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            if (!_testMode)
            {
                try { _host.Services.GetRequiredService<IUiPreferencesStore>().Save(_host.Services.GetRequiredService<MainViewModel>().CapturePreferences()); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { _host.Services.GetRequiredService<ILogger<App>>().LogWarning(exception, "Could not save appearance preferences."); }
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task.Run(() => _host.StopAsync(timeout.Token)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        if (_repairInstaller is not null && !_testMode)
        {
            try
            {
                var repair = new System.Diagnostics.ProcessStartInfo(_repairInstaller) { UseShellExecute = true };
                repair.ArgumentList.Add("/DIR=" + AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                System.Diagnostics.Process.Start(repair);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                MessageBox.Show("ADM closed safely, but setup could not open. Run your downloaded ADM-Setup.exe to continue.", "ADM setup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        _instance?.Dispose();
        base.OnExit(e);
    }
}
