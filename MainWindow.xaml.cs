using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace ChatGPTUpdater;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifetime; the cancellation source is disposed when the window closes.")]
public partial class MainWindow
{
    private readonly UpdaterService _updater = new();
    private readonly SystemThemeIconService _systemThemeIcon;
    private CancellationTokenSource? _cancellation;
    private bool _canLaunchInstalled;

    public MainWindow()
    {
        SystemThemeWatcher.Watch(this);
        InitializeComponent();
        _systemThemeIcon = new SystemThemeIconService(this);
        FlowDirection = LocalizationService.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync(bool installIfMissing = false)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        RetryButton.Visibility = Visibility.Collapsed;
        LaunchButton.Visibility = Visibility.Collapsed;
        InstallButton.Visibility = Visibility.Collapsed;
        StatusText.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
        ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.IsIndeterminate = true;
        DetailText.Text = string.Empty;

        var progress = new Progress<UpdaterProgress>(UpdateProgress);

        try
        {
            var result = await _updater.RunAsync(progress, _cancellation.Token, installIfMissing);
            _canLaunchInstalled = result.InstalledVersion is not null;

            if (result.Action == UpdaterAction.OfferInstall)
            {
                StatusText.Text = LocalizationService.Get("StatusNotInstalled");
                DetailText.Text = LocalizationService.Get("DetailInstallPrompt");
                ProgressBar.Visibility = Visibility.Collapsed;
                InstallButton.Visibility = Visibility.Visible;
                return;
            }

            if (result.Action == UpdaterAction.LaunchInstalled)
            {
                StatusText.Text = LocalizationService.Get("StatusUpToDate");
                DetailText.Text = LocalizationService.Get("DetailLaunching");
                ProgressBar.Value = 100;
                ProgressBar.IsIndeterminate = false;
                UpdaterService.LaunchChatGPT();
                await Task.Delay(500);
                Close();
                return;
            }

            var isFirstInstall = result.InstalledVersion is null;
            StatusText.Text = LocalizationService.Get(
                isFirstInstall ? "StatusPackageReadyToInstall" : "StatusPackageReady");
            DetailText.Text = LocalizationService.Get(
                isFirstInstall ? "DetailInstallInstallerOpened" : "DetailInstallerOpened");
            ProgressBar.Value = 100;
            ProgressBar.IsIndeterminate = false;
            UpdaterService.OpenInstaller(result.PackagePath!);
            await Task.Delay(500);
            Close();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Get("StatusUpdateCheckFailed");
            StatusText.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
            DetailText.Text = ex.Message;
            ProgressBar.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Visible;
            LaunchButton.Visibility = _canLaunchInstalled || await UpdaterService.IsInstalledAsync()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void UpdateProgress(UpdaterProgress value)
    {
        StatusText.Text = value.Status;
        DetailText.Text = value.Detail ?? string.Empty;

        if (value.InstalledVersion is not null)
        {
            InstalledVersionText.Text = value.InstalledVersion.ToString();
            _canLaunchInstalled = true;
        }

        if (value.AvailableVersion is not null)
            AvailableVersionText.Text = value.AvailableVersion.ToString();

        if (value.Percent is { } percent)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = Math.Clamp(percent, 0, 100);
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
        }
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private async void InstallButton_Click(object sender, RoutedEventArgs e) => await RunAsync(installIfMissing: true);

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        UpdaterService.LaunchChatGPT();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _systemThemeIcon.Dispose();
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
