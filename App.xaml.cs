using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ChatGPTUpdater;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the application lifetime; the mutex is disposed in OnExit.")]
public partial class App : Application
{
    private static readonly JsonSerializerOptions DiagnosticJsonOptions = new()
    {
        WriteIndented = true
    };

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.InitializeFromSystem();

        _mutex = new Mutex(true, @"Local\ChatGPTUpdater-OpenAI.Codex", out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            var messageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = LocalizationService.Get("WindowTitle"),
                Content = LocalizationService.Get("AlreadyRunning")
            };
            _ = await messageBox.ShowDialogAsync();
            Shutdown();
            return;
        }

        if (e.Args is ["--diagnostic", var outputPath])
        {
            try
            {
                var package = await MicrosoftStoreClient.GetLatestAsync(
                    UpdaterService.ProductId,
                    CancellationToken.None);
                File.WriteAllText(outputPath, JsonSerializer.Serialize(package, DiagnosticJsonOptions));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(outputPath, ex.ToString());
                Shutdown(1);
            }
            return;
        }

        if (e.Args is ["--localization-diagnostic", var requestedCulture, var localizationOutputPath])
        {
            try
            {
                LocalizationService.InitializeFromSystem(CultureInfo.GetCultureInfo(requestedCulture));
                var previewWindow = new MainWindow();
                var result = new
                {
                    Requested = requestedCulture,
                    Resolved = LocalizationService.CurrentUICulture.Name,
                    LocalizationService.IsRightToLeft,
                    previewWindow.Title,
                    FlowDirection = previewWindow.FlowDirection.ToString(),
                    Status = LocalizationService.Get("StatusCheckingInstalled"),
                    Progress = LocalizationService.Format("DetailDownloadProgress", "1 MB", "2 MB")
                };
                File.WriteAllText(localizationOutputPath, JsonSerializer.Serialize(result, DiagnosticJsonOptions));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(localizationOutputPath, ex.ToString());
                Shutdown(1);
            }
            return;
        }

        if (e.Args is ["--verification-diagnostic", var packagePath, var expectedVersion, var verificationOutputPath])
        {
            try
            {
                var uiTicks = 0;
                var timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                timer.Tick += (_, _) => uiTicks++;

                var stopwatch = Stopwatch.StartNew();
                timer.Start();
                var verification = await UpdaterService.VerifyPackageAsync(
                    packagePath,
                    Version.Parse(expectedVersion),
                    CancellationToken.None);
                timer.Stop();
                stopwatch.Stop();

                var result = new
                {
                    verification.IsValid,
                    verification.Error,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                    UiDispatcherTicks = uiTicks
                };
                File.WriteAllText(verificationOutputPath, JsonSerializer.Serialize(result, DiagnosticJsonOptions));
                Shutdown(verification.IsValid ? 0 : 1);
            }
            catch (Exception ex)
            {
                File.WriteAllText(verificationOutputPath, ex.ToString());
                Shutdown(1);
            }
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
