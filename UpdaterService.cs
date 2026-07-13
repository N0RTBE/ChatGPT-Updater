using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace ChatGPTUpdater;

internal sealed partial class UpdaterService
{
    public const string ProductId = "9PLM9XGG6VKS";
    public const string PackageName = "OpenAI.Codex";
    public const string PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0";
    public const string Publisher = "CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B";
    private const string AppUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    private readonly PackageCache _packageCache = new();
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        CheckCertificateRevocationList = true
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public async Task<UpdaterResult> RunAsync(
        IProgress<UpdaterProgress> progress,
        CancellationToken cancellationToken,
        bool installIfMissing = false)
    {
        progress.Report(new UpdaterProgress(LocalizationService.Get("StatusCheckingInstalled")));
        var installed = await GetInstalledVersionAsync(cancellationToken);
        var downloadDirectory = _packageCache.PrepareDirectory();
        await PackageCache.CleanupAsync(
            downloadDirectory,
            installed,
            availableVersion: null,
            cancellationToken);
        progress.Report(new UpdaterProgress(
            installed is null ? LocalizationService.Get("StatusNotInstalled") : LocalizationService.Get("StatusCheckingStore"),
            installed is null ? LocalizationService.Get("DetailWillDownload") : null,
            InstalledVersion: installed));

        var package = await MicrosoftStoreClient.GetLatestAsync(ProductId, cancellationToken);
        progress.Report(new UpdaterProgress(
            LocalizationService.Get("StatusComparingVersions"),
            LocalizationService.Format("DetailStoreVersion", package.Version),
            InstalledVersion: installed,
            AvailableVersion: package.Version));

        await PackageCache.CleanupAsync(
            downloadDirectory,
            installed,
            package.Version,
            cancellationToken);

        if (installed is not null && installed >= package.Version)
            return new UpdaterResult(UpdaterAction.LaunchInstalled, installed, package.Version);

        if (installed is null && !installIfMissing)
            return new UpdaterResult(UpdaterAction.OfferInstall, null, package.Version);

        var safeFileName = $"OpenAI.Codex_{package.Version}_x64__2p2nqsd0c76g0.msix";
        var destination = Path.Combine(downloadDirectory, safeFileName);
        var packageIsVerified = false;

        if (File.Exists(destination))
        {
            ReportVerification(progress, destination, installed, package.Version);
            var cachedVerification = await VerifyPackageAsync(destination, package.Version, cancellationToken);
            packageIsVerified = cachedVerification.IsValid;

            if (!packageIsVerified)
                File.Delete(destination);
        }

        if (!packageIsVerified)
        {
            packageIsVerified = await DownloadAsync(
                package,
                destination,
                installed,
                progress,
                cancellationToken);
        }

        if (!packageIsVerified)
        {
            ReportVerification(progress, destination, installed, package.Version);
            var downloadedVerification = await VerifyPackageAsync(destination, package.Version, cancellationToken);
            if (!downloadedVerification.IsValid)
            {
                File.Delete(destination);
                throw new InvalidOperationException(LocalizationService.Format(
                    "ErrorVerificationFailed",
                    downloadedVerification.Error));
            }
        }

        return new UpdaterResult(UpdaterAction.OpenInstaller, installed, package.Version, destination);
    }

    public static async Task<bool> IsInstalledAsync()
    {
        try
        {
            return await GetInstalledVersionAsync(CancellationToken.None) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void LaunchChatGPT()
    {
        var managerType = Type.GetTypeFromCLSID(
            new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),
            throwOnError: true)!;
        var manager = (IApplicationActivationManager)Activator.CreateInstance(managerType)!;
        try
        {
            var result = manager.ActivateApplication(AppUserModelId, null, ActivateOptions.None, out _);
            Marshal.ThrowExceptionForHR(result);
        }
        finally
        {
            Marshal.FinalReleaseComObject(manager);
        }
    }

    public static void OpenInstaller(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static async Task<Version?> GetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageManager = new PackageManager();
            var version = packageManager
                .FindPackagesForUser(string.Empty, PackageFamilyName)
                .Where(package => package.Id.Name.Equals(PackageName, StringComparison.OrdinalIgnoreCase))
                .Select(package => package.Id.Version)
                .Select(value => new Version(value.Major, value.Minor, value.Build, value.Revision))
                .OrderByDescending(value => value)
                .FirstOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            return version;
        }, cancellationToken);
    }

    private static async Task<bool> DownloadAsync(
        StorePackage package,
        string destination,
        Version? installed,
        IProgress<UpdaterProgress> progress,
        CancellationToken cancellationToken)
    {
        ValidateDownloadUri(package.Url);
        var temporary = destination + ".download";

        // A previous run may have finished downloading but failed before the rename.
        // Reuse it after the same full package verification used for the final file.
        if (File.Exists(temporary))
        {
            ReportVerification(progress, temporary, installed, package.Version);
            var temporaryVerification = await VerifyPackageAsync(temporary, package.Version, cancellationToken);
            if (temporaryVerification.IsValid)
            {
                File.Move(temporary, destination, true);
                return true;
            }
        }

        try
        {
            var existingLength = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
            var response = await SendDownloadRequestAsync(package.Url, existingLength, cancellationToken);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                response.Dispose();
                File.Delete(temporary);
                existingLength = 0;
                response = await SendDownloadRequestAsync(package.Url, existingLength, cancellationToken);
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();

                var isResume = response.StatusCode == HttpStatusCode.PartialContent && existingLength > 0;
                if (isResume && response.Content.Headers.ContentRange?.From != existingLength)
                    throw new InvalidDataException(LocalizationService.Get("ErrorBadRange"));

                if (!isResume)
                    existingLength = 0;

                var total = response.Content.Headers.ContentRange?.Length
                    ?? (response.Content.Headers.ContentLength is { } remaining ? existingLength + remaining : null);
                var mode = isResume ? FileMode.Append : FileMode.Create;

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(
                    temporary, mode, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous))
                {
                    var buffer = new byte[1024 * 128];
                    var received = existingLength;

                    while (true)
                    {
                        var count = await input.ReadAsync(buffer, cancellationToken);
                        if (count == 0)
                            break;

                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        received += count;
                        var percent = total is > 0 ? received * 100d / total.Value : (double?)null;
                        progress.Report(new UpdaterProgress(
                            isResume ? LocalizationService.Get("StatusResumeDownload") : LocalizationService.Get("StatusDownload"),
                            total is > 0
                                ? LocalizationService.Format("DetailDownloadProgress", FormatBytes(received), FormatBytes(total.Value))
                                : FormatBytes(received),
                            percent,
                            installed,
                            package.Version));
                    }

                    await output.FlushAsync(cancellationToken);
                }
            }

            // Both streams are closed before the rename. Keeping this outside the
            // await-using scopes prevents our own FileShare.None lock.
            File.Move(temporary, destination, true);
            return false;
        }
        catch
        {
            // Intentionally keep a partial download. The next run resumes it with HTTP Range.
            throw;
        }
    }

    private static void ReportVerification(
        IProgress<UpdaterProgress> progress,
        string path,
        Version? installed,
        Version available)
    {
        progress.Report(new UpdaterProgress(
            LocalizationService.Get("StatusVerifyingPackage"),
            Path.GetFileName(path),
            InstalledVersion: installed,
            AvailableVersion: available));
    }

    internal static Task<PackageVerificationResult> VerifyPackageAsync(
        string path,
        Version expectedVersion,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isValid = PackageVerifier.IsTrustedChatGPTPackage(path, expectedVersion, out var error);
            cancellationToken.ThrowIfCancellationRequested();
            return new PackageVerificationResult(isValid, error);
        }, cancellationToken);

    private static async Task<HttpResponseMessage> SendDownloadRequestAsync(
        Uri url,
        long existingLength,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
            request.Headers.Range = new RangeHeaderValue(existingLength, null);

        return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.Host.Equals("tlu.dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(LocalizationService.Get("ErrorInvalidPackageUrl"));
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units =
        [
            LocalizationService.Get("UnitBytes"),
            LocalizationService.Get("UnitKilobytes"),
            LocalizationService.Get("UnitMegabytes"),
            LocalizationService.Get("UnitGigabytes")
        ];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    internal readonly record struct PackageVerificationResult(bool IsValid, string Error);

    [Flags]
    private enum ActivateOptions : uint
    {
        None = 0
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            IntPtr appUserModelId,
            IntPtr itemArray,
            IntPtr verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            IntPtr appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }
}
