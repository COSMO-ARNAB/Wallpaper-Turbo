using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using WallpaperTurbo.Core.Updates.Interfaces;

namespace WallpaperTurbo.Updater.Services;

public sealed class AuthenticodeValidator : ISignatureValidator
{
    private readonly string _expectedPublisher;

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