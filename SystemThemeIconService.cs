using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace ChatGPTUpdater;

/// <summary>
/// Keeps the running window, taskbar, and Alt+Tab icon readable against the Windows shell theme.
/// </summary>
internal sealed class SystemThemeIconService : IDisposable
{
    private const int WindowSettingChange = 0x001A;
    private const int WindowSystemColorChange = 0x0015;
    private const int WindowThemeChanged = 0x031A;
    private const string PersonalizeRegistryPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Lazy<ImageSource> LightThemeIcon = new(() => CreateMonochromeIcon(0));
    private static readonly Lazy<ImageSource> DarkThemeIcon = new(() => CreateMonochromeIcon(255));

    private readonly Window _window;
    private HwndSource? _source;
    private bool _disposed;

    public SystemThemeIconService(Window window)
    {
        _window = window;
        _window.SourceInitialized += Window_SourceInitialized;
        ApplyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SourceInitialized -= Window_SourceInitialized;
        _source?.RemoveHook(WindowProcedure);
        _source = null;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProcedure);
        ApplyIcon();
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        var immersiveColorChanged = message == WindowSettingChange &&
                                    longParameter != IntPtr.Zero &&
                                    string.Equals(
                                        Marshal.PtrToStringUni(longParameter),
                                        "ImmersiveColorSet",
                                        StringComparison.Ordinal);

        if (message == WindowThemeChanged || message == WindowSystemColorChange || immersiveColorChanged)
        {
            _ = _window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                ApplyIcon);
        }

        return IntPtr.Zero;
    }

    private void ApplyIcon()
    {
        if (_disposed)
            return;

        _window.Icon = UsesLightSystemTheme()
            ? LightThemeIcon.Value
            : DarkThemeIcon.Value;
    }

    private static bool UsesLightSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(PersonalizeRegistryPath, "SystemUsesLightTheme", 1);
            return value is not int integer || integer != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return ApplicationThemeManager.GetAppTheme() != ApplicationTheme.Dark;
        }
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
