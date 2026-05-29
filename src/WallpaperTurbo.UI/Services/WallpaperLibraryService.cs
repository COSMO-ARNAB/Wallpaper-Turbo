using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperTurbo.UI.Services;

public class UserWallpaperManifest
{
    [JsonPropertyName("wallpapers")]
    public List<WallpaperEntry> Wallpapers { get; set; } = new();
}

public class WallpaperLibraryService : IWallpaperLibraryService
{
    private readonly string _localAppDir;
    private readonly string _wallpapersDir;
    private readonly string _manifestPath;
    private readonly string _backupManifestPath;
    private readonly string _appRunnerDir;
    private readonly IThumbnailExtractor _thumbnailExtractor;

    private readonly SemaphoreSlim _manifestLock = new(1, 1);
    private readonly SemaphoreSlim _importQueue = new(1, 1); // Bounded concurrency limit of 1 for disk/decode safety
    
    private readonly List<Task> _activeTasks = new();
    private readonly object _tasksLock = new();

    public WallpaperLibraryService(IThumbnailExtractor thumbnailExtractor)
    {
        _thumbnailExtractor = thumbnailExtractor;

        // Initialize local AppData paths
        _localAppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperTurbo");
        _wallpapersDir = Path.Combine(_localAppDir, "Wallpapers");
        _manifestPath = Path.Combine(_localAppDir, "WallpaperManifest.json");
        _backupManifestPath = Path.Combine(_localAppDir, "WallpaperManifest.backup.json");

        // Resolve AppRunner paths for fallback default manifest loading
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appRunnerCandidate = Path.Combine(baseDir, "WallpaperTurbo.AppRunner.exe");
        if (File.Exists(appRunnerCandidate))
        {
            _appRunnerDir = baseDir;
        }
        else
        {
            string srcPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            string dir1 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows");
            string dir2 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "x64", "Debug", "net8.0-windows");
            string dir3 = Path.Combine(srcPath, "WallpaperTurbo.AppRunner", "bin", "Debug", "net8.0-windows", "win-x64");

            if (File.Exists(Path.Combine(dir1, "WallpaperTurbo.AppRunner.exe"))) _appRunnerDir = dir1;
            else if (File.Exists(Path.Combine(dir2, "WallpaperTurbo.AppRunner.exe"))) _appRunnerDir = dir2;
            else _appRunnerDir = dir3;
        }

        // Guarantee managed folders exist
        Directory.CreateDirectory(_localAppDir);
        Directory.CreateDirectory(_wallpapersDir);
    }

    public string AppRunnerDir => _appRunnerDir;

    public async Task<IReadOnlyList<WallpaperEntry>> GetWallpapersAsync(CancellationToken cancellationToken = default)
    {
        var allWallpapers = new List<WallpaperEntry>();

        // 1. Load Default Installation Wallpapers
        string defaultManifestPath = Path.Combine(_appRunnerDir, "Assets", "WallpaperManifest.json");
        System.Diagnostics.Debug.WriteLine($"[Diagnostic] Loading default manifest path: {defaultManifestPath}");
        if (File.Exists(defaultManifestPath))
        {
            try
            {
                string json = await File.ReadAllTextAsync(defaultManifestPath, cancellationToken);
                var manifest = JsonSerializer.Deserialize<WallpaperManifest>(json);
                if (manifest != null)
                {
                    foreach (var wp in manifest.Wallpapers)
                    {
                        // Map relative video path directly to absolute installation path for hover & extract operations
                        string originalVideoRelative = wp.Video;
                        wp.Video = Path.Combine(_appRunnerDir, wp.Video);
                        
                        // We store extracted thumbnails locally because installation folder is read-only
                        string localThumbDir = Path.Combine(_localAppDir, "Thumbnails");
                        Directory.CreateDirectory(localThumbDir);
                        string localThumbPath = Path.Combine(localThumbDir, $"{wp.Id}.jpg");
                        
                        System.Diagnostics.Debug.WriteLine($"[Diagnostic] Default Wallpaper: ID={wp.Id}, Title={wp.Title}, Video={wp.Video}, TargetThumb={localThumbPath}");

                        if (File.Exists(localThumbPath))
                        {
                            wp.Thumbnail = localThumbPath;
                            wp.IsFallbackThumbnail = false;
                            System.Diagnostics.Debug.WriteLine($"[Diagnostic] Unique thumbnail found for default wallpaper '{wp.Title}' at '{localThumbPath}'");
                        }
                        else
                        {
                            // Trigger background frame extraction, assigning temporary fallback placeholder
                            wp.Thumbnail = "pack://application:,,,/Assets/Branding/wallpaper-turbo.ico";
                            wp.IsFallbackThumbnail = true;
                            
                            var wpCopy = wp;
                            System.Diagnostics.Debug.WriteLine($"[Thumbnail Start] Initiating default thumbnail extraction for '{wpCopy.Title}' (Reason: No cached thumbnail file found on disk)...");
                            TrackTask(Task.Run(async () =>
                            {
                                await _importQueue.WaitAsync();
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Gen] Running video frame extraction for '{wpCopy.Title}' from source '{wpCopy.Video}'...");
                                    string tempThumb = await _thumbnailExtractor.ExtractThumbnailAsync(wpCopy.Video, localThumbDir, CancellationToken.None);
                                    
                                    if (File.Exists(tempThumb))
                                    {
                                        string finalPath = Path.Combine(localThumbDir, $"{wpCopy.Id}.jpg");
                                        if (File.Exists(finalPath)) File.Delete(finalPath);
                                        File.Move(tempThumb, finalPath);
                                        
                                        wpCopy.Thumbnail = finalPath;
                                        wpCopy.IsFallbackThumbnail = false;
                                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Success] Successfully saved unique thumbnail for '{wpCopy.Title}' to '{finalPath}'");
                                    }
                                    else
                                    {
                                        wpCopy.IsFallbackThumbnail = true;
                                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Extraction finished but output file not found on disk for '{wpCopy.Title}'. Keeping fallback.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    wpCopy.IsFallbackThumbnail = true;
                                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Failed to extract thumbnail for default wallpaper '{wpCopy.Title}': {ex.Message}. Fallback Assigned.");
                                }
                                finally
                                {
                                    _importQueue.Release();
                                }
                            }));
                        }
                        
                        allWallpapers.Add(wp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Diagnostic] Failed to load default manifest: {ex.Message}");
            }
        }

        // 2. Load User Managed Wallpapers with Health Recovery / Quarantine
        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_manifestPath))
            {
                UserWallpaperManifest? userManifest = null;
                try
                {
                    string json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
                    userManifest = JsonSerializer.Deserialize<UserWallpaperManifest>(json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"User manifest is corrupted. Attempting recovery from backup: {ex.Message}");
                    if (File.Exists(_backupManifestPath))
                    {
                        try
                        {
                            string backupJson = await File.ReadAllTextAsync(_backupManifestPath, cancellationToken);
                            userManifest = JsonSerializer.Deserialize<UserWallpaperManifest>(backupJson);
                        }
                        catch
                        {
                            System.Diagnostics.Debug.WriteLine("Backup manifest corrupted as well.");
                        }
                    }
                }

                if (userManifest != null)
                {
                    foreach (var wp in userManifest.Wallpapers)
                    {
                        // Safe isolation check: quarantine entry if video file is missing
                        if (!File.Exists(wp.Video))
                        {
                            System.Diagnostics.Debug.WriteLine($"Quarantining corrupted entry '{wp.Title}' because video file is missing.");
                            continue;
                        }

                        // Source timestamp cache invalidation check
                        bool needsRegen = false;
                        string dir = Path.GetDirectoryName(wp.Video)!;
                        string metadataFile = Path.Combine(dir, "metadata.json");
                        if (File.Exists(metadataFile))
                        {
                            try
                            {
                                string metaJson = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                                var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);
                                if (meta != null && meta.TryGetValue("SourceLastWriteTimeUtc", out var storedTimeObj))
                                {
                                    string storedTimeStr = storedTimeObj?.ToString() ?? string.Empty;
                                    if (DateTime.TryParse(storedTimeStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var storedTime))
                                    {
                                        var currentWriteTime = File.GetLastWriteTimeUtc(wp.Video);
                                        // If mismatch > 1 second, invalidate cached thumbnail
                                        if (Math.Abs((currentWriteTime - storedTime).TotalSeconds) > 1.0)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[Cache Invalidate] Media file modified for '{wp.Title}'. Invalidation triggered.");
                                            needsRegen = true;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Cache Invalidation Warning] Failed to inspect metadata for '{wp.Title}': {ex.Message}");
                            }
                        }

                        // Auto-recovery: if thumbnail is missing or invalidated, regenerate in background
                        if (!File.Exists(wp.Thumbnail) || wp.Thumbnail.StartsWith("pack://") || needsRegen)
                        {
                            string fallbackReason = !File.Exists(wp.Thumbnail) ? "Thumbnail file not found on disk" : (needsRegen ? "Source video write timestamp modified" : "Thumbnail path is pack placeholder");
                            System.Diagnostics.Debug.WriteLine($"[Thumbnail Start] Auto-recovery triggered for '{wp.Title}' (Reason: {fallbackReason}).");
                            
                            bool hasExisting = File.Exists(wp.Thumbnail) && !wp.Thumbnail.StartsWith("pack://");
                            if (!hasExisting)
                            {
                                wp.Thumbnail = "pack://application:,,,/Assets/Branding/wallpaper-turbo.ico"; // Temporary default placeholder
                                wp.IsFallbackThumbnail = true;
                            }
                            else
                            {
                                wp.IsFallbackThumbnail = false;
                            }
                            
                            // Queue thumbnail regeneration in sequentially bounded background queue (concurrency = 1)
                            var wpCopy = wp;
                            TrackTask(Task.Run(async () =>
                            {
                                // 30s timeout prevents permanent queue stall if a previous extraction hangs
                                bool acquired = await _importQueue.WaitAsync(TimeSpan.FromSeconds(30));
                                if (!acquired)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Skipped] Import queue timed out (30s) for '{wpCopy.Title}'. Skipping to prevent permanent stall.");
                                    return;
                                }
                                DiagnosticsService.OnDecodeQueued();
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Gen] Running auto-recovery extraction for user wallpaper '{wpCopy.Title}'...");
                                    string thumb = await _thumbnailExtractor.ExtractThumbnailAsync(wpCopy.Video, dir, CancellationToken.None);
                                    
                                    if (File.Exists(thumb))
                                    {
                                        wpCopy.Thumbnail = thumb;
                                        wpCopy.IsFallbackThumbnail = false;
                                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Success] Auto-recovery extraction succeeded for '{wpCopy.Title}'. Saved to: '{thumb}'");
                                        
                                        await UpdateUserManifestThumbnailAsync(wpCopy.Id, thumb);
                                        await WriteMetadataJsonAsync(dir, wpCopy, CancellationToken.None);
                                    }
                                    else
                                    {
                                        wpCopy.IsFallbackThumbnail = true;
                                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Auto-recovery extraction finished but file not found on disk for '{wpCopy.Title}'.");
                                    }
                                }
                                catch (Exception rex)
                                {
                                    wpCopy.IsFallbackThumbnail = true;
                                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Auto-recovery thumbnail extraction failed for '{wpCopy.Title}': {rex.Message}");
                                }
                                finally
                                {
                                    DiagnosticsService.OnDecodeCompleted();
                                    _importQueue.Release();
                                }
                            }));
                        }
                        else
                        {
                            wp.IsFallbackThumbnail = false;
                        }

                        allWallpapers.Add(wp);
                    }
                }
            }
        }
        finally
        {
            _manifestLock.Release();
        }

        return allWallpapers;
    }

    public async Task<WallpaperEntry> ImportWallpaperAsync(string sourceFilePath, Action<WallpaperEntry> onThumbnailCompleted, CancellationToken cancellationToken = default)
    {
        // 1. Lightweight Media Validation
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Import failed: Source file not found at '{sourceFilePath}'");
        }

        string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        string[] allowedExtensions = { ".mp4", ".webm", ".mkv", ".gif", ".jpg", ".jpeg", ".png" };
        if (!Array.Exists(allowedExtensions, e => e == ext))
        {
            throw new NotSupportedException($"Import failed: Format '{ext}' is not supported. Supported extensions: .mp4, .webm, .mkv, .gif, .jpg, .jpeg, .png");
        }

        var fileInfo = new FileInfo(sourceFilePath);
        if (fileInfo.Length <= 0)
        {
            throw new InvalidDataException("Import failed: Source file is empty (0 bytes).");
        }

        // Lightweight non-blocking read stream check to verify the file is not locked or corrupted
        try
        {
            using (var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] temp = new byte[1];
                _ = fs.Read(temp, 0, 1);
            }
        }
        catch (Exception ex)
        {
            throw new IOException($"Import failed: Source file is locked or corrupted. Detail: {ex.Message}", ex);
        }

        string name = Path.GetFileNameWithoutExtension(sourceFilePath);

        // 2. Intelligent Deduplication Check
        string fileHash = await CalculateFileHeaderHashAsync(sourceFilePath, cancellationToken);
        var existing = await CheckForDuplicateAsync(fileInfo.Length, fileHash, cancellationToken);
        if (existing != null)
        {
            return existing; // Idempotent duplicate bypass
        }

        // 3. Setup transaction variables for clean rollback
        string guid = Guid.NewGuid().ToString();
        string targetDir = Path.Combine(_wallpapersDir, guid);
        string targetVideoPath = Path.Combine(targetDir, $"wallpaper{ext}");

        try
        {
            // Transaction Start
            Directory.CreateDirectory(targetDir);

            // Copy file asynchronously to target folder
            using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            using (var destinationStream = new FileStream(targetVideoPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            }

            // Create transient manifest entry using App default icon as a placeholder
            string placeholderThumb = "pack://application:,,,/Assets/Branding/wallpaper-turbo.ico";
            var newWp = new WallpaperEntry
            {
                Id = guid,
                Title = name,
                Video = targetVideoPath,
                Thumbnail = placeholderThumb,
                Author = "Local User",
                IsFallbackThumbnail = true,
                Tags = new List<string> { "Imported", ext.TrimStart('.') }
            };

            // Atomically write metadata.json inside the folder
            await WriteMetadataJsonAsync(targetDir, newWp, cancellationToken);

            // Add transient entry to manifest immediately for fluid UX
            await SaveUserWallpaperToManifestAsync(newWp, cancellationToken);

            // 4. Queue async background thumbnail extraction (runs inside a bounded queue)
            System.Diagnostics.Debug.WriteLine($"[Thumbnail Start] Initiating imported wallpaper thumbnail extraction for '{newWp.Title}'...");
            TrackTask(Task.Run(async () =>
            {
                // 30s timeout prevents permanent stall if prior extraction hangs
                bool acquired = await _importQueue.WaitAsync(TimeSpan.FromSeconds(30));
                if (!acquired)
                {
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Skipped] Import queue timed out (30s) for '{newWp.Title}'.");
                    return;
                }
                DiagnosticsService.OnDecodeQueued();
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Gen] Running extraction for imported wallpaper '{newWp.Title}'...");
                    string finalThumb = await _thumbnailExtractor.ExtractThumbnailAsync(targetVideoPath, targetDir, CancellationToken.None);
                    
                    if (File.Exists(finalThumb))
                    {
                        newWp.Thumbnail = finalThumb;
                        newWp.IsFallbackThumbnail = false;
                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Success] Imported wallpaper thumbnail generation succeeded for '{newWp.Title}'. Path: '{finalThumb}'");

                        // Update manifest atomically
                        await UpdateUserManifestThumbnailAsync(guid, finalThumb);

                        // Notify UI ViewModels of completion
                        onThumbnailCompleted?.Invoke(newWp);
                    }
                    else
                    {
                        newWp.IsFallbackThumbnail = true;
                        System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Extraction finished but file not found on disk for '{newWp.Title}'. Fallback assigned.");
                    }
                }
                catch (Exception ex)
                {
                    newWp.IsFallbackThumbnail = true;
                    System.Diagnostics.Debug.WriteLine($"[Thumbnail Failure] Background generation failed for imported wallpaper '{newWp.Title}': {ex.Message}. Fallback assigned.");
                }
                finally
                {
                    DiagnosticsService.OnDecodeCompleted();
                    _importQueue.Release();
                }
            }));

            return newWp;
        }
        catch (Exception)
        {
            // Transaction Rollback: clean up folders and files on failures to guarantee zero orphan assets
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }
            }
            catch { }

            throw;
        }
    }

    private async Task<WallpaperEntry?> CheckForDuplicateAsync(long length, string hash, CancellationToken cancellationToken)
    {
        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_manifestPath)) return null;

            string json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<UserWallpaperManifest>(json);
            if (manifest != null)
            {
                foreach (var wp in manifest.Wallpapers)
                {
                    if (File.Exists(wp.Video))
                    {
                        var info = new FileInfo(wp.Video);
                        if (info.Length == length)
                        {
                            string currentHash = await CalculateFileHeaderHashAsync(wp.Video, cancellationToken);
                            if (currentHash == hash)
                            {
                                return wp;
                            }
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            _manifestLock.Release();
        }
        return null;
    }

    private async Task<string> CalculateFileHeaderHashAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                using var sha = SHA256.Create();
                byte[] hashBytes = sha.ComputeHash(buffer, 0, bytesRead);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }, cancellationToken);
    }

    private async Task WriteMetadataJsonAsync(string directory, WallpaperEntry entry, CancellationToken cancellationToken)
    {
        string metadataPath = Path.Combine(directory, "metadata.json");
        
        string videoWriteTime = string.Empty;
        try
        {
            if (File.Exists(entry.Video))
            {
                videoWriteTime = File.GetLastWriteTimeUtc(entry.Video).ToString("o");
            }
        }
        catch { }

        var metadata = new Dictionary<string, object>
        {
            { "SchemaVersion", 1 }, // Versioned metadata schema
            { "Id", entry.Id },
            { "Title", entry.Title },
            { "VideoPath", entry.Video },
            { "ThumbnailPath", entry.Thumbnail },
            { "Author", entry.Author },
            { "ImportedDate", DateTime.UtcNow.ToString("o") },
            { "SourceLastWriteTimeUtc", videoWriteTime },
            { "Tags", entry.Tags }
        };

        string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
    }

    private async Task SaveUserWallpaperToManifestAsync(WallpaperEntry entry, CancellationToken cancellationToken)
    {
        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            UserWallpaperManifest manifest = new();
            if (File.Exists(_manifestPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
                    manifest = JsonSerializer.Deserialize<UserWallpaperManifest>(json) ?? new UserWallpaperManifest();
                }
                catch
                {
                    manifest = new UserWallpaperManifest();
                }
            }

            manifest.Wallpapers.Add(entry);

            await SaveManifestAtomicAsync(manifest, cancellationToken);
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private async Task UpdateUserManifestThumbnailAsync(string guid, string thumbnailPath)
    {
        await _manifestLock.WaitAsync();
        try
        {
            if (File.Exists(_manifestPath))
            {
                string json = await File.ReadAllTextAsync(_manifestPath);
                var manifest = JsonSerializer.Deserialize<UserWallpaperManifest>(json);
                if (manifest != null)
                {
                    var wp = manifest.Wallpapers.FirstOrDefault(w => w.Id == guid);
                    if (wp != null)
                    {
                        wp.Thumbnail = thumbnailPath;
                        await SaveManifestAtomicAsync(manifest, CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update user manifest thumbnail: {ex.Message}");
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private async Task SaveManifestAtomicAsync(UserWallpaperManifest manifest, CancellationToken cancellationToken)
    {
        // 1. Rolling Backup Strategy
        if (File.Exists(_manifestPath))
        {
            try
            {
                File.Copy(_manifestPath, _backupManifestPath, true);
            }
            catch { }
        }

        // 2. Atomic Temporary Write then Move Replace
        string tempFile = Path.Combine(_localAppDir, $"{Guid.NewGuid()}.tmp");
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempFile, json, cancellationToken);

        try
        {
            if (File.Exists(_manifestPath))
            {
                File.Delete(_manifestPath);
            }
            File.Move(tempFile, _manifestPath);
        }
        catch (Exception)
        {
            // Rollback temp file to avoid leaving artifacts
            if (File.Exists(tempFile)) File.Delete(tempFile);
            throw;
        }
    }

    private void TrackTask(Task t)
    {
        lock (_tasksLock)
        {
            _activeTasks.Add(t);
            // Clean up completed tasks
            t.ContinueWith(completed =>
            {
                lock (_tasksLock)
                {
                    _activeTasks.Remove(completed);
                }
            }, TaskScheduler.Default);
        }
    }

    public async Task ShutdownAsync()
    {
        Task[] tasksToAwait;
        lock (_tasksLock)
        {
            tasksToAwait = _activeTasks.ToArray();
        }

        if (tasksToAwait.Length > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Awaiting {tasksToAwait.Length} outstanding background tasks during graceful shutdown...");
            await Task.WhenAll(tasksToAwait).ConfigureAwait(false);
        }
    }

    public async Task<bool> DeleteWallpaperAsync(string guid, CancellationToken cancellationToken = default)
    {
        bool deleted = false;

        // 1. Try deleting from User Manifest
        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_manifestPath))
            {
                string json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
                var manifest = JsonSerializer.Deserialize<UserWallpaperManifest>(json);
                if (manifest != null)
                {
                    var wp = manifest.Wallpapers.FirstOrDefault(w => w.Id == guid);
                    if (wp != null)
                    {
                        manifest.Wallpapers.Remove(wp);
                        await SaveManifestAtomicAsync(manifest, cancellationToken);
                        
                        // Delete user wallpaper folder on disk in the background
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                string targetDir = Path.Combine(_wallpapersDir, guid);
                                if (Directory.Exists(targetDir))
                                {
                                    Directory.Delete(targetDir, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to delete user wallpaper folder for '{guid}': {ex.Message}");
                            }
                        }, CancellationToken.None);

                        deleted = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting from user manifest: {ex.Message}");
        }
        finally
        {
            _manifestLock.Release();
        }

        if (deleted) return true;

        // 2. Try deleting from Default Manifest
        string defaultManifestPath = Path.Combine(_appRunnerDir, "Assets", "WallpaperManifest.json");
        if (File.Exists(defaultManifestPath))
        {
            try
            {
                await _manifestLock.WaitAsync(cancellationToken);
                
                string json = await File.ReadAllTextAsync(defaultManifestPath, cancellationToken);
                var manifest = JsonSerializer.Deserialize<WallpaperManifest>(json);
                if (manifest != null)
                {
                    var wp = manifest.Wallpapers.FirstOrDefault(w => w.Id == guid);
                    if (wp != null)
                    {
                        manifest.Wallpapers.Remove(wp);
                        
                        // Save default manifest atomically
                        string tempFile = Path.Combine(_localAppDir, $"{Guid.NewGuid()}.tmp");
                        string outputJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(tempFile, outputJson, cancellationToken);
                        
                        if (File.Exists(defaultManifestPath)) File.Delete(defaultManifestPath);
                        File.Move(tempFile, defaultManifestPath);

                        // Try to delete default wallpaper video and local thumbnail if they exist
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                string absoluteVideo = Path.Combine(_appRunnerDir, wp.Video);
                                if (File.Exists(absoluteVideo))
                                {
                                    File.Delete(absoluteVideo);
                                }

                                string localThumbDir = Path.Combine(_localAppDir, "Thumbnails");
                                string localThumbPath = Path.Combine(localThumbDir, $"{wp.Id}.jpg");
                                if (File.Exists(localThumbPath))
                                {
                                    File.Delete(localThumbPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Could not delete default assets for '{guid}': {ex.Message}");
                            }
                        });

                        deleted = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting from default manifest: {ex.Message}");
            }
            finally
            {
                _manifestLock.Release();
            }
        }

        return deleted;
    }
}
