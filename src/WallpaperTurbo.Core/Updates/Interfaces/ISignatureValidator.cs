namespace WallpaperTurbo.Core.Updates.Interfaces;

public interface ISignatureValidator
{
    bool IsValidSignature(string filePath);
}
