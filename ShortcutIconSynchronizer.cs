using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Text;

namespace ChatGPTUpdater;

/// <summary>
/// Keeps existing Start menu and taskbar shortcuts aligned with the Windows shell theme.
/// Windows uses the shortcut icon for pinned applications instead of the running window icon.
/// </summary>
internal static class ShortcutIconSynchronizer
{
    private const string LightIconResource = "ThemeIcons.ChatGPTUpdater.Light.ico";
    private const string DarkIconResource = "ThemeIcons.ChatGPTUpdater.Dark.ico";
    private const uint ShellChangeUpdateItem = 0x00002000;
    private const uint ShellChangeNotifyPathW = 0x0005;
    private const uint ShellLinkGetPathRaw = 0x0004;
    private const int ReadWriteMode = 2;
    private static readonly Lock SynchronizationLock = new();

    public static void UpdateExistingShortcuts(bool usesLightTheme, CancellationToken cancellationToken)
    {
        try
        {
            lock (SynchronizationLock)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                    return;

                var iconPath = ExtractThemeIcon(usesLightTheme);
                foreach (var shortcutPath in FindShortcutPaths())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UpdateShortcut(shortcutPath, executablePath, iconPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or
                COMException or ArgumentException)
        {
            // Shell integration is best-effort and must never prevent the updater from running.
        }
    }

    private static string ExtractThemeIcon(bool usesLightTheme)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPT Updater",
            "Icons");
        Directory.CreateDirectory(directory);

        var fileName = usesLightTheme
            ? "chatgpt-updater-light.ico"
            : "chatgpt-updater-dark.ico";
        var destinationPath = Path.Combine(directory, fileName);
        var resourceName = usesLightTheme ? LightIconResource : DarkIconResource;

        using var source = typeof(ShortcutIconSynchronizer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded icon resource '{resourceName}' was not found.");
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        var bytes = memory.ToArray();

        if (!File.Exists(destinationPath) || !File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(bytes))
            File.WriteAllBytes(destinationPath, bytes);

        return destinationPath;
    }

    private static IEnumerable<string> FindShortcutPaths()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Internet Explorer",
                "Quick Launch",
                "User Pinned",
                "TaskBar")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (var path in paths)
                yield return path;
        }
    }

    private static void UpdateShortcut(string shortcutPath, string executablePath, string iconPath)
    {
        var shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(shortcutPath, ReadWriteMode);

            var targetPath = new StringBuilder(32768);
            shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, ShellLinkGetPathRaw);
            if (!PathsEqual(targetPath.ToString(), executablePath))
                return;

            var currentIconPath = new StringBuilder(32768);
            shellLink.GetIconLocation(currentIconPath, currentIconPath.Capacity, out var currentIconIndex);
            if (currentIconIndex == 0 && PathsEqual(currentIconPath.ToString(), iconPath))
                return;

            shellLink.SetIconLocation(iconPath, 0);
            persistFile.Save(shortcutPath, true);
            NativeMethods.SHChangeNotify(
                ShellChangeUpdateItem,
                ShellChangeNotifyPathW,
                shortcutPath,
                null);
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
                _ = Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(first)),
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumPath,
            IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maximumPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximumPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int maximumPath,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern void SHChangeNotify(
            uint eventId,
            uint flags,
            string? item1,
            string? item2);
    }
}
