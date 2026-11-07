using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BitwardenCommandPalette.Core.Bw;

/// <summary>Decides whether an executable may be run.</summary>
internal interface IExecutableVerifier
{
    /// <summary>Whether the file at <paramref name="path"/> may be run.</summary>
    /// <param name="path">A fully qualified path.</param>
    /// <returns>True only for a file that passed every check.</returns>
    bool IsTrusted(string path);
}

/// <summary>
/// Accepts a file only if Windows validates its embedded Authenticode signature and the signing
/// certificate carries the expected common name and organisation exactly (ADR 7, ADR 15).
/// </summary>
/// <remarks>
/// Only a passed check is remembered, one entry per file and signer, shared by every instance
/// so <see cref="BwLocator"/> and <see cref="BwCli"/> do not check the same file twice. It is
/// reused while the file's size and last write time are unchanged; a failed check is always
/// repeated, so a transient failure (an updater holding the file) does not stick. A link is
/// refused: callers pass its resolved target (<see cref="ExecutableFile"/>).
/// </remarks>
internal sealed partial class AuthenticodeVerifier : IExecutableVerifier
{
    /// <summary>The publisher of both official Windows builds of bw.</summary>
    public const string BitwardenPublisher = "Bitwarden Inc.";

    // HRESULTs WinVerifyTrust returns when the revocation server cannot be reached.
    private const int RevocationOffline = unchecked((int)0x80092013);
    private const int RevocationFailure = unchecked((int)0x800B010E);

    private static readonly ConcurrentDictionary<(string CommonName, string Organisation, string Path), (long Length, DateTime Written)> Passed = new();

    private readonly string commonName;
    private readonly string organisation;

    /// <summary>Creates a verifier that accepts only files signed by Bitwarden Inc.</summary>
    public AuthenticodeVerifier()
        : this(BitwardenPublisher, BitwardenPublisher)
    {
    }

    /// <summary>Creates a verifier for the given signer.</summary>
    /// <param name="commonName">The exact common name (CN) the certificate must carry.</param>
    /// <param name="organisation">The exact organisation (O) the certificate must carry.</param>
    public AuthenticodeVerifier(string commonName, string organisation)
    {
        ArgumentException.ThrowIfNullOrEmpty(commonName);
        ArgumentException.ThrowIfNullOrEmpty(organisation);
        this.commonName = commonName;
        this.organisation = organisation;
    }

    /// <inheritdoc />
    public bool IsTrusted(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        FileInfo file;
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            file = new FileInfo(path);
            if (!file.Exists || file.LinkTarget is not null)
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var key = (commonName, organisation, file.FullName);
        var stamp = (file.Length, file.LastWriteTimeUtc);
        if (Passed.TryGetValue(key, out var known) && known == stamp)
        {
            return true;
        }

        if (!Verify(file.FullName))
        {
            _ = Passed.TryRemove(key, out _);
            return false;
        }

        Passed[key] = stamp;
        return true;
    }

    private bool Verify(string path) => SignatureIsValid(path) && SignerIs(path, commonName, organisation);

    private static bool SignerIs(string path, string expectedCommonName, string expectedOrganisation)
    {
        try
        {
#pragma warning disable SYSLIB0057 // X509CertificateLoader cannot read the signer of a signed file.
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var certificate = new X509Certificate2(signer);
            string? foundCommonName = null;
            string? foundOrganisation = null;
            foreach (var name in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
            {
                // A multi-valued name (CN=x+O=y) is not how either official build is signed.
                if (name.HasMultipleElements)
                {
                    return false;
                }

                switch (name.GetSingleElementType().Value)
                {
                    case "2.5.4.3":
                        foundCommonName = name.GetSingleElementValue();
                        break;
                    case "2.5.4.10":
                        foundOrganisation = name.GetSingleElementValue();
                        break;
                    default:
                        break;
                }
            }

            return string.Equals(foundCommonName, expectedCommonName, StringComparison.Ordinal)
                && string.Equals(foundOrganisation, expectedOrganisation, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool SignatureIsValid(string path)
    {
        var filePath = Marshal.StringToHGlobalUni(path);
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = filePath }, fileInfo, fDeleteOld: false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 1, // WTD_REVOKE_WHOLECHAIN
                UnionChoice = 1, // WTD_CHOICE_FILE
                File = fileInfo,
                StateAction = 1, // WTD_STATEACTION_VERIFY
                ProviderFlags = 0x80, // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            // ADR 15: an unreachable revocation server does not block; a revoked or invalid
            // signature does.
            return result is 0 or RevocationOffline or RevocationFailure;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(filePath);
        }
    }

    // Loaded from System32 only, never from the application folder where a planted copy could sit.
    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
