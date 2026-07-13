using System.IO;
using System.Text.RegularExpressions;

namespace ChatGPTUpdater;

internal sealed partial class PackageCache
{
    private static readonly TimeSpan PartialDownloadRetention = TimeSpan.FromDays(7);
    private readonly string _localAppData;

    public PackageCache()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal PackageCache(string localAppData)
    {
        _localAppData = localAppData;
    }

    public string PrepareDirectory()
    {
        var downloadDirectory = Path.Combine(_localAppData, "ChatGPT Updater", "Downloads");
        Directory.CreateDirectory(downloadDirectory);
        return downloadDirectory;
    }

    public static Task CleanupAsync(
        string downloadDirectory,
        Version? installedVersion,
        Version? availableVersion,
        CancellationToken cancellationToken)
        => Task.Run(
            () => Cleanup(
                downloadDirectory,
                installedVersion,
                availableVersion,
                DateTimeOffset.UtcNow,
                cancellationToken),
            cancellationToken);

    internal static void Cleanup(
        string downloadDirectory,
        Version? installedVersion,
        Version? availableVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(downloadDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(downloadDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var match = ManagedPackageRegex().Match(Path.GetFileName(path));
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
                continue;

            var isPartial = match.Groups["partial"].Success;
            var alreadyInstalled = installedVersion is not null && version <= installedVersion;
            var superseded = availableVersion is not null && version != availableVersion;
            var stalePartial = isPartial &&
                               File.GetLastWriteTimeUtc(path) < now.UtcDateTime - PartialDownloadRetention;

            if (!alreadyInstalled && !superseded && !stalePartial)
                continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // App Installer may still hold the package. A later launch retries cleanup.
            }
        }
    }

    [GeneratedRegex(
        @"^OpenAI\.Codex_(?<version>\d+\.\d+\.\d+\.\d+)_x64__2p2nqsd0c76g0\.msix(?<partial>\.download)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ManagedPackageRegex();
}
