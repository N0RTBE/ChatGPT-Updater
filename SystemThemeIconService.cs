using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Appearance;

namespace ChatGPTUpdater;

/// <summary>
/// Keeps the running window, taskbar, and Alt+Tab icon readable against the Windows shell theme.
/// </summary>
internal sealed class SystemThemeIconService : IDisposable
{
    private static readonly Lazy<ImageSource> LightThemeIcon = new(() => CreateMonochromeIcon(0));
    private static readonly Lazy<ImageSource> DarkThemeIcon = new(() => CreateMonochromeIcon(255));

    private readonly Window _window;
    private CancellationTokenSource? _shortcutUpdateCancellation;
    private bool _disposed;

    public SystemThemeIconService(Window window)
    {
        _window = window;
        _window.SourceInitialized += Window_SourceInitialized;
        ApplicationThemeManager.Changed += ApplicationThemeManager_Changed;
        ApplyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SourceInitialized -= Window_SourceInitialized;
        ApplicationThemeManager.Changed -= ApplicationThemeManager_Changed;
        _shortcutUpdateCancellation?.Cancel();
        _shortcutUpdateCancellation?.Dispose();
        _shortcutUpdateCancellation = null;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyIcon();
    }

    private void ApplicationThemeManager_Changed(ApplicationTheme currentTheme, Color systemAccent)
    {
        ApplyIcon();
    }

    private void ApplyIcon()
    {
        if (_disposed)
            return;

        var usesLightTheme = UsesLightSystemTheme();
        var icon = usesLightTheme ? LightThemeIcon.Value : DarkThemeIcon.Value;

        // Setting the same ImageSource before and after HWND creation does not always
        // make Explorer refresh WM_SETICON, so force the native icon property to change.
        _window.Icon = null;
        _window.Icon = icon;

        UpdateShortcutIcon(usesLightTheme);
    }

    private static bool UsesLightSystemTheme()
    {
        return ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow or
                SystemTheme.HCBlack or SystemTheme.HC1 or SystemTheme.HC2 => false,
            SystemTheme.Light or SystemTheme.Flow or SystemTheme.Sunrise or SystemTheme.HCWhite => true,
            _ => ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Dark
        };
    }

    private void UpdateShortcutIcon(bool usesLightTheme)
    {
        _shortcutUpdateCancellation?.Cancel();
        _shortcutUpdateCancellation?.Dispose();
        _shortcutUpdateCancellation = new CancellationTokenSource();
        var cancellationToken = _shortcutUpdateCancellation.Token;

        _ = Task.Run(
            () => ShortcutIconSynchronizer.UpdateExistingShortcuts(usesLightTheme, cancellationToken),
            cancellationToken);
    }

    private static ImageSource CreateMonochromeIcon(byte colorChannel)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.DecodePixelWidth = 256;
        source.UriSource = new Uri(
            "pack://application:,,,/Assets/chatgpt-updater.png",
            UriKind.Absolute);
        source.EndInit();
        source.Freeze();

        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = colorChannel;
            pixels[index + 1] = colorChannel;
            pixels[index + 2] = colorChannel;
        }

        var icon = BitmapSource.Create(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            bitmap.DpiX,
            bitmap.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        icon.Freeze();
        return icon;
    }
}
