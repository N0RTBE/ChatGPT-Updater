namespace ChatGPTUpdater;

internal sealed record StorePackage(string FileName, string Architecture, Uri Url, Version Version);

internal sealed record UpdaterProgress(
    string Status,
    string? Detail = null,
    double? Percent = null,
    Version? InstalledVersion = null,
    Version? AvailableVersion = null);

internal enum UpdaterAction
{
    LaunchInstalled,
    OfferInstall,
    OpenInstaller
}

internal sealed record UpdaterResult(
    UpdaterAction Action,
    Version? InstalledVersion,
    string? PackagePath = null);
