using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ChatGPTUpdater;

internal static partial class MicrosoftStoreClient
{
    private const string DeliveryEndpoint =
        "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx";
    private const string SecuredDeliveryEndpoint = DeliveryEndpoint + "/secured";

    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Addressing = "http://www.w3.org/2005/08/addressing";
    private static readonly XNamespace Security =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace Utility =
        "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private static readonly XNamespace UpdateAuthorization =
        "http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization";
    private static readonly XNamespace ClientService =
        "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService";

    // These identifiers describe Windows Update platform components already present on
    // supported Windows 10/11 systems. Supplying them keeps the FE3 response focused on
    // the requested Store product instead of returning unrelated platform metadata.
    private static readonly int[] InstalledNonLeafUpdateIds =
    [
        1, 2, 3, 11, 19, 544, 549, 2359974, 2359977, 5169044, 8788830,
        23110993, 23110994, 54341900, 54343656, 59830006, 59830007, 59830008,
        60484010, 62450018, 62450019, 62450020, 66027979, 66053150, 97657898,
        98822896, 98959022, 98959023, 98959024, 98959025, 98959026, 104433538,
        104900364, 105489019, 117765322, 129905029, 130040031, 132387090,
        132393049, 133399034, 138537048, 140377312, 143747671, 158941041,
        158941042, 158941043, 158941044, 159123858, 159130928, 164836897,
        164847386, 164848327, 164852241, 164852246, 164852252, 164852253
    ];

    private static readonly int[] CachedUpdateIds =
    [
        10, 17, 2359977, 5143990, 5169043, 5169047, 8806526, 9125350,
        9154769, 10809856, 23110995, 23110996, 23110999, 23111000, 23111001,
        23111002, 23111003, 23111004, 24513870, 28880263, 30077688, 30486944,
        30526991, 30528442, 30530496, 30530501, 30530504, 30530962, 164852262,
        164853061, 164853063, 164853071, 164853072, 164853075, 168118980,
        168118981, 168118983, 168118984, 168180375, 168180376, 168180378,
        168180379, 168270830, 168270831, 168270833, 168270834, 168270835
    ];

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        CheckCertificateRevocationList = true
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public static async Task<StorePackage> GetLatestAsync(string productId, CancellationToken cancellationToken)
    {
        var categoryId = await GetWindowsUpdateCategoryIdAsync(productId, cancellationToken);
        var cookie = await GetCookieAsync(cancellationToken);
        var syncResponse = await PostSoapAsync(
            DeliveryEndpoint,
            CreateSyncUpdatesRequest(cookie, categoryId),
            cancellationToken);

        var candidates = ParseCandidates(syncResponse)
            .OrderByDescending(candidate => candidate.Version)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(LocalizationService.Get("ErrorNoMatchingPackage"));

        foreach (var candidate in candidates)
        {
            var url = await GetPackageUrlAsync(candidate, cancellationToken);
            if (url is not null)
                return new StorePackage(candidate.FileName, candidate.Architecture, url, candidate.Version);
        }

        throw new InvalidOperationException(LocalizationService.Get("ErrorNoStorePackages"));
    }

    private static async Task<string> GetWindowsUpdateCategoryIdAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/{Uri.EscapeDataString(productId)}" +
            "?market=US&locale=en-us&deviceFamily=Windows.Desktop");

        using var response = await HttpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("Payload", out var payload) ||
            !payload.TryGetProperty("Skus", out var skus) ||
            skus.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(LocalizationService.Get("ErrorNoStorePackages"));
        }

        foreach (var sku in skus.EnumerateArray())
        {
            if (!sku.TryGetProperty("FulfillmentData", out var fulfillmentElement))
                continue;

            var fulfillmentJson = fulfillmentElement.GetString();
            if (string.IsNullOrWhiteSpace(fulfillmentJson))
                continue;

            using var fulfillment = JsonDocument.Parse(fulfillmentJson);
            if (fulfillment.RootElement.TryGetProperty("WuCategoryId", out var categoryElement) &&
                categoryElement.GetString() is { Length: > 0 } categoryId)
            {
                return categoryId;
            }
        }

        throw new InvalidOperationException(LocalizationService.Get("ErrorNoStorePackages"));
    }

    private static async Task<string> GetCookieAsync(CancellationToken cancellationToken)
    {
        var response = await PostSoapAsync(
            DeliveryEndpoint,
            CreateCookieRequest(),
            cancellationToken);
        var document = XDocument.Parse(response);
        return Descendants(document, "EncryptedData").FirstOrDefault()?.Value
            ?? throw new InvalidOperationException(LocalizationService.Get("ErrorNoStorePackages"));
    }

    private static async Task<Uri?> GetPackageUrlAsync(
        StoreUpdateCandidate candidate,
        CancellationToken cancellationToken)
    {
        var response = await PostSoapAsync(
            SecuredDeliveryEndpoint,
            CreateExtendedInfoRequest(candidate.UpdateId, candidate.RevisionNumber),
            cancellationToken);
        var document = XDocument.Parse(response);

        foreach (var value in Descendants(document, "Url").Select(element => element.Value))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Host.Equals("tlu.dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
        }

        return null;
    }

    private static async Task<string> PostSoapAsync(
        string endpoint,
        XDocument document,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                document.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8,
                "application/soap+xml")
        };
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static StoreUpdateCandidate[] ParseCandidates(string response)
    {
        var decoded = WebUtility.HtmlDecode(response);
        var document = XDocument.Parse(decoded, LoadOptions.PreserveWhitespace);
        var candidates = new Dictionary<string, StoreUpdateCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var metadata in Descendants(document, "AppxMetadata"))
        {
            var moniker = Attribute(metadata, "PackageMoniker");
            var match = ChatGPTPackageMonikerRegex().Match(moniker);
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
                continue;

            var updateInfo = metadata.Ancestors()
                .FirstOrDefault(element => element.Name.LocalName == "UpdateInfo");
            if (updateInfo is null || !Descendants(updateInfo, "SecuredFragment").Any())
                continue;

            var identity = Descendants(updateInfo, "UpdateIdentity").FirstOrDefault();
            var updateId = identity is null ? string.Empty : Value(identity, "UpdateID");
            var revision = identity is null ? string.Empty : Value(identity, "RevisionNumber");
            if (string.IsNullOrWhiteSpace(updateId) || string.IsNullOrWhiteSpace(revision))
                continue;

            var candidate = new StoreUpdateCandidate(
                moniker + ".msix",
                "x64",
                version,
                updateId,
                revision);
            candidates.TryAdd(updateId, candidate);
        }

        return candidates.Values.ToArray();
    }

    private static string Attribute(XElement element, string localName)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value
           ?? string.Empty;

    private static string Value(XElement element, string localName)
        => Attribute(element, localName) is { Length: > 0 } attributeValue
            ? attributeValue
            : element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value
              ?? string.Empty;

    private static IEnumerable<XElement> Descendants(XContainer container, string localName)
        => container.Descendants().Where(element => element.Name.LocalName == localName);

    private static XDocument CreateCookieRequest()
    {
        var now = DateTimeOffset.UtcNow;
        return SoapEnvelope(
            "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/GetCookie",
            DeliveryEndpoint,
            now,
            now.AddDays(27),
            new XElement(ClientService + "GetCookie",
                new XElement(ClientService + "oldCookie"),
                new XElement(ClientService + "lastChange", Timestamp(now.AddYears(-2))),
                new XElement(ClientService + "currentTime", Timestamp(now.AddMilliseconds(7))),
                new XElement(ClientService + "protocolVersion", "1.40")),
            includeEmptyUser: true);
    }

    private static XDocument CreateSyncUpdatesRequest(string cookie, string categoryId)
    {
        var now = DateTimeOffset.UtcNow;
        return SoapEnvelope(
            "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/SyncUpdates",
            DeliveryEndpoint,
            now,
            now.AddMinutes(5),
            new XElement(ClientService + "SyncUpdates",
                new XElement(ClientService + "cookie",
                    new XElement(ClientService + "Expiration", "2045-03-11T02:02:48Z"),
                    new XElement(ClientService + "EncryptedData", cookie)),
                new XElement(ClientService + "parameters",
                    new XElement(ClientService + "ExpressQuery", false),
                    IdCollection("InstalledNonLeafUpdateIDs", InstalledNonLeafUpdateIds),
                    IdCollection("OtherCachedUpdateIDs", CachedUpdateIds),
                    new XElement(ClientService + "SkipSoftwareSync", false),
                    new XElement(ClientService + "NeedTwoGroupOutOfScopeUpdates", true),
                    new XElement(ClientService + "FilterAppCategoryIds",
                        new XElement(ClientService + "CategoryIdentifier",
                            new XElement(ClientService + "Id", categoryId))),
                    new XElement(ClientService + "TreatAppCategoryIdsAsInstalled", true),
                    new XElement(ClientService + "AlsoPerformRegularSync", false),
                    new XElement(ClientService + "ComputerSpec"),
                    new XElement(ClientService + "ExtendedUpdateInfoParameters",
                        new XElement(ClientService + "XmlUpdateFragmentTypes",
                            new XElement(ClientService + "XmlUpdateFragmentType", "Extended")),
                        new XElement(ClientService + "Locales",
                            new XElement(ClientService + "string", "en-US"),
                            new XElement(ClientService + "string", "en"))),
                    new XElement(ClientService + "ClientPreferredLanguages",
                        new XElement(ClientService + "string", "en-US")),
                    new XElement(ClientService + "ProductsParameters",
                        new XElement(ClientService + "SyncCurrentVersionOnly", false),
                        new XElement(ClientService + "DeviceAttributes", DeviceAttributes()),
                        new XElement(ClientService + "CallerAttributes", "Interactive=1;IsSeeker=0;"),
                        new XElement(ClientService + "Products")))),
            includeRetailTicket: true);
    }

    private static XDocument CreateExtendedInfoRequest(string updateId, string revisionNumber)
    {
        var now = DateTimeOffset.UtcNow;
        return SoapEnvelope(
            "http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService/GetExtendedUpdateInfo2",
            SecuredDeliveryEndpoint,
            now,
            now.AddMinutes(5),
            new XElement(ClientService + "GetExtendedUpdateInfo2",
                new XElement(ClientService + "updateIDs",
                    new XElement(ClientService + "UpdateIdentity",
                        new XElement(ClientService + "UpdateID", updateId),
                        new XElement(ClientService + "RevisionNumber", revisionNumber))),
                new XElement(ClientService + "infoTypes",
                    new XElement(ClientService + "XmlUpdateFragmentType", "FileUrl"),
                    new XElement(ClientService + "XmlUpdateFragmentType", "FileDecryption")),
                new XElement(ClientService + "deviceAttributes", DeviceAttributes())),
            includeRetailTicket: true);
    }

    private static XDocument SoapEnvelope(
        string action,
        string endpoint,
        DateTimeOffset created,
        DateTimeOffset expires,
        XElement body,
        bool includeEmptyUser = false,
        bool includeRetailTicket = false)
    {
        var ticket = new XElement(UpdateAuthorization + "WindowsUpdateTicketsToken",
            new XAttribute(Utility + "id", "ClientMSA"),
            new XElement(UpdateAuthorization + "TicketType",
                new XAttribute("Name", "MSA"),
                new XAttribute("Version", "1.0"),
                new XAttribute("Policy", "MBI_SSL"),
                includeEmptyUser
                    ? new XElement(UpdateAuthorization + "User")
                    : includeRetailTicket ? "Retail" : null));

        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "a", Addressing),
                new XElement(Soap + "Header",
                    new XElement(Addressing + "Action",
                        new XAttribute(Soap + "mustUnderstand", "1"), action),
                    new XElement(Addressing + "MessageID", "urn:uuid:" + Guid.NewGuid()),
                    new XElement(Addressing + "To",
                        new XAttribute(Soap + "mustUnderstand", "1"), endpoint),
                    new XElement(Security + "Security",
                        new XAttribute(Soap + "mustUnderstand", "1"),
                        new XElement(Utility + "Timestamp",
                            new XElement(Utility + "Created", Timestamp(created)),
                            new XElement(Utility + "Expires", Timestamp(expires))),
                        ticket)),
                new XElement(Soap + "Body", body)));
    }

    private static XElement IdCollection(string name, IEnumerable<int> values)
        => new(ClientService + name, values.Select(value => new XElement(ClientService + "int", value)));

    private static string DeviceAttributes()
    {
        var version = Environment.OSVersion.Version;
        var osVersion = $"{version.Major}.{version.Minor}.{version.Build}.{Math.Max(version.Revision, 0)}";
        return "BranchReadinessLevel=CB;CurrentBranch=rs_prerelease;OEMModel=Virtual Machine;" +
               "FlightRing=WIS;AttrDataVer=21;SystemManufacturer=Microsoft Corporation;" +
               "InstallLanguage=en-US;OSUILocale=en-US;InstallationType=Client;" +
               "FlightingBranchName=external;FirmwareVersion=Hyper-V UEFI Release v2.5;" +
               "SystemProductName=Virtual Machine;OSSkuId=48;FlightContent=Branch;App=WU;" +
               $"OEMName_Uncleaned=Microsoft Corporation;AppVer={osVersion};OSArchitecture=AMD64;" +
               "SystemSKU=None;UpdateManagementGroup=2;IsFlightingEnabled=1;IsDeviceRetailDemo=0;" +
               $"TelemetryLevel=3;OSVersion={osVersion};DeviceFamily=Windows.Desktop;";
    }

    private static string Timestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed record StoreUpdateCandidate(
        string FileName,
        string Architecture,
        Version Version,
        string UpdateId,
        string RevisionNumber);

    [GeneratedRegex(
        @"^OpenAI\.Codex_(?<version>\d+\.\d+\.\d+\.\d+)_x64__2p2nqsd0c76g0$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ChatGPTPackageMonikerRegex();
}
