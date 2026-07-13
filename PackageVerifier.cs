using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace ChatGPTUpdater;

internal static class PackageVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrustedChatGPTPackage(string path, Version expectedVersion, out string error)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < 1024 * 1024)
            {
                error = LocalizationService.Get("VerifyTooSmall");
                return false;
            }

            using var archive = ZipFile.OpenRead(path);
            var manifestEntry = archive.Entries.SingleOrDefault(entry =>
                entry.FullName.Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
            {
                error = LocalizationService.Get("VerifyNoManifest");
                return false;
            }

            using var manifestStream = manifestEntry.Open();
            var document = XDocument.Load(manifestStream, LoadOptions.None);
            var identity = document.Descendants().SingleOrDefault(x => x.Name.LocalName == "Identity");
            if (identity is null)
            {
                error = LocalizationService.Get("VerifyNoIdentity");
                return false;
            }

            var name = (string?)identity.Attribute("Name");
            var publisher = (string?)identity.Attribute("Publisher");
            var architecture = (string?)identity.Attribute("ProcessorArchitecture");
            var versionText = (string?)identity.Attribute("Version");

            if (!string.Equals(name, UpdaterService.PackageName, StringComparison.Ordinal) ||
                !string.Equals(publisher, UpdaterService.Publisher, StringComparison.Ordinal) ||
                !string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
                !Version.TryParse(versionText, out var version) || version != expectedVersion)
            {
                error = LocalizationService.Get("VerifyIdentityMismatch");
                return false;
            }

            if (!VerifyEmbeddedSignature(path))
            {
                error = LocalizationService.Get("VerifySignature");
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool VerifyEmbeddedSignature(string fileName)
    {
        var fileInfo = new WinTrustFileInfo(fileName);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var data = new WinTrustData(fileInfoPointer);
        var dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            Marshal.StructureToPtr(data, dataPointer, false);
            return WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, dataPointer) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustData>(dataPointer);
            Marshal.FreeHGlobal(dataPointer);
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public int StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            StructSize = Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public int StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;

        public WinTrustData(IntPtr fileInfo)
        {
            StructSize = Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2; // WTD_UI_NONE
            RevocationChecks = 0;
            UnionChoice = 1; // WTD_CHOICE_FILE
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0x1000; // WTD_CACHE_ONLY_URL_RETRIEVAL
            UiContext = 0;
        }
    }
}
