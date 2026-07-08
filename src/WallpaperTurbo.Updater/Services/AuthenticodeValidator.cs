using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class AuthenticodeValidator : ISignatureValidator
{
    private readonly string _expectedPublisher;

    #region WinTrust PInvoke
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pInfoStruct;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWVTData
    );
    #endregion

    public AuthenticodeValidator(string expectedPublisher = "Wallpaper Turbo")
    {
        _expectedPublisher = expectedPublisher ?? throw new ArgumentNullException(nameof(expectedPublisher));
    }

    public bool IsValidSignature(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        try
        {
            if (!VerifyWinTrust(filePath))
            {
                Debug.WriteLine("[AuthenticodeValidator] WinVerifyTrust failed. The file hash does not match the signature or it is untrusted.");
                return false;
            }

            var (isValid, subjectName, errorMessage) = VerifyAuthenticodeSignature(filePath);

            if (!isValid)
            {
                Debug.WriteLine($"[AuthenticodeValidator] Signature validation failed: {errorMessage}");
                return false;
            }

            if (!IsPublisherTrusted(subjectName))
            {
                Debug.WriteLine($"[AuthenticodeValidator] Publisher mismatch. Expected: {_expectedPublisher}, Found: {subjectName?.Name}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthenticodeValidator] Exception during validation: {ex.Message}");
            return false;
        }
    }

    private static bool VerifyWinTrust(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;

        try
        {
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pInfoStruct = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_IGNORE,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                dwUIContext = 0
            };

            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
            Marshal.StructureToPtr(data, dataPtr, false);

            Guid actionId = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            uint result = WinVerifyTrust(IntPtr.Zero, actionId, dataPtr);
            return result == 0;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure(dataPtr, typeof(WINTRUST_DATA));
                Marshal.FreeHGlobal(dataPtr);
            }

            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure(fileInfoPtr, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(fileInfoPtr);
            }
        }
    }

    private static (bool IsValid, X500DistinguishedName? SubjectName, string ErrorMessage) VerifyAuthenticodeSignature(string filePath)
    {
        try
        {
            var signer = X509Certificate.CreateFromSignedFile(filePath);
            if (signer == null)
            {
                return (false, null, "File is not signed");
            }

            var cert = new X509Certificate2(signer);
            var subjectName = cert.SubjectName;

            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.VerificationTime = DateTime.UtcNow;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            if (!chain.Build(cert))
            {
                var chainErrors = chain.ChainStatus.Length > 0 
                    ? string.Join(", ", System.Array.ConvertAll(chain.ChainStatus, s => s.Status.ToString()))
                    : "Unknown chain error";
                return (false, subjectName, $"Certificate chain validation failed: {chainErrors}");
            }

            foreach (var status in chain.ChainStatus)
            {
                if (status.Status == X509ChainStatusFlags.UntrustedRoot ||
                    status.Status == X509ChainStatusFlags.PartialChain ||
                    status.Status == X509ChainStatusFlags.Cyclic ||
                    status.Status == X509ChainStatusFlags.Revoked ||
                    status.Status == X509ChainStatusFlags.OfflineRevocation)
                {
                    return (false, subjectName, $"Certificate validation error: {status.Status}");
                }
            }

            if (!cert.Verify())
            {
                return (false, subjectName, "Certificate verification failed");
            }

            return (true, subjectName, string.Empty);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return (false, null, $"Cryptographic error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private bool IsPublisherTrusted(X500DistinguishedName? subjectName)
    {
        if (subjectName == null)
            return false;

        string formatted = subjectName.Format(true);
        var lines = formatted.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Equals($"CN={_expectedPublisher}", StringComparison.OrdinalIgnoreCase) ||
                line.Equals($"O={_expectedPublisher}", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("CN=WallpaperTurbo", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("O=WallpaperTurbo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
    }
}
