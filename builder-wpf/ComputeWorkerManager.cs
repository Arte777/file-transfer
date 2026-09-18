using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NexusBuilder
{
    /// <summary>
    /// Управляет безопасным получением, проверкой целостности по SHA-256
    /// и кэшированием официальных релизов внешних Compute компонентов
    /// для включения в сборки NEXUS Builder.
    /// Загрузка производится ИСКЛЮЧИТЕЛЬНО из официальных репозиториев проектов.
    /// Исполняемые файлы НИКОГДА не запускаются во время сборки.
    /// </summary>
    public static class ComputeWorkerManager
    {
        private static readonly HttpClient _httpClient;

        static ComputeWorkerManager()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NEXUS-Builder/8.0 (Windows NT; x64)");
        }

        public static string GetComputeDirectory()
        {
            string templateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", "app_template"
            );
            string computeDir = Path.Combine(templateDir, "Compute");
            Directory.CreateDirectory(computeDir);
            return computeDir;
        }

        /// <summary>
        /// Проверяет наличие или безопасно подготавливает официальный worker для заданного режима.
        /// </summary>
        public static async Task<(bool success, string workerPath, string message)> PrepareWorkerAsync(
            string mode,
            Action<string>? logCallback = null,
            Action<int>? progressCallback = null,
            CancellationToken ct = default)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            bool isMonero = m == "monero" || m == "xmr" || m.Contains("monero");
            bool isEtc = m == "ethereum-classic" || m == "ethereum classic" || m == "etc" || m.Contains("etc");

            if (!isMonero && !isEtc)
            {
                return (true, "", "Режим вычислений не требует внешнего воркера.");
            }

            string computeDir = GetComputeDirectory();
            string workerFileName = isMonero ? "xmrig.exe" : "lolMiner.exe";
            string targetWorkerPath = Path.Combine(computeDir, workerFileName);

            // 1. Ручное или ранее сохраненное кэшированное размещение
            if (File.Exists(targetWorkerPath))
            {
                logCallback?.Invoke($"✅ Локальный компонент '{workerFileName}' уже присутствует в кэше шаблона: {targetWorkerPath}");
                return (true, targetWorkerPath, "Использован существующий компонент из локального кэша шаблона.");
            }

            logCallback?.Invoke($"🔍 Локальный компонент '{workerFileName}' отсутствует в шаблоне. Поиск официального релиза...");

            if (isMonero)
            {
                return await PrepareMoneroWorkerAsync(targetWorkerPath, logCallback, progressCallback, ct);
            }
            else
            {
                return await PrepareEtcWorkerAsync(targetWorkerPath, logCallback, progressCallback, ct);
            }
        }

        /// <summary>
        /// Официальная загрузка и верификация XMRig из github.com/xmrig/xmrig
        /// </summary>
        private static async Task<(bool success, string workerPath, string message)> PrepareMoneroWorkerAsync(
            string targetWorkerPath,
            Action<string>? logCallback,
            Action<int>? progressCallback,
            CancellationToken ct)
        {
            try
            {
                logCallback?.Invoke("🌐 Обращение к официальному API релизов XMRig (github.com/xmrig/xmrig)...");

                string apiUrl = "https://api.github.com/repos/xmrig/xmrig/releases/latest";
                using var resp = await _httpClient.GetAsync(apiUrl, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, "", $"Не удалось получить информацию об официальном релизе XMRig (HTTP {(int)resp.StatusCode}). Проверьте интернет-соединение.");
                }

                string releaseJson = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(releaseJson);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? (tagElem.GetString() ?? "") : "";
                if (!root.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                {
                    return (false, "", "В официальном релизе XMRig отсутствуют файлы ассетов.");
                }

                string shaSumsUrl = "";
                string winZipUrl = "";
                string winZipName = "";
                long winZipSize = 0;

                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? (u.GetString() ?? "") : "";
                    long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                    if (name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase))
                    {
                        shaSumsUrl = downloadUrl;
                    }
                    else if (name.EndsWith("-windows-x64.zip", StringComparison.OrdinalIgnoreCase) && !name.Contains("gcc"))
                    {
                        winZipUrl = downloadUrl;
                        winZipName = name;
                        winZipSize = size;
                    }
                }

                if (string.IsNullOrEmpty(winZipUrl) || string.IsNullOrEmpty(shaSumsUrl))
                {
                    return (false, "", "Не удалось найти официальный Windows-ассет или файл контрольных сумм SHA256SUMS в релизе XMRig.");
                }

                // 2. Получение официального файла контрольных сумм
                logCallback?.Invoke($"📥 Загрузка официального манифеста контрольных сумм: {Path.GetFileName(shaSumsUrl)}...");
                string shaSumsContent = await _httpClient.GetStringAsync(shaSumsUrl, ct);

                string expectedSha256 = "";
                foreach (var line in shaSumsContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains(winZipName, StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 1 && parts[0].Length == 64)
                        {
                            expectedSha256 = parts[0].Trim().ToLowerInvariant();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(expectedSha256))
                {
                    return (false, "", $"Контрольная сумма для ассета '{winZipName}' не найдена в официальном файле SHA256SUMS.");
                }

                logCallback?.Invoke($"🔒 Ожидаемый официальный SHA-256 ({winZipName}): {expectedSha256}");

                // 3. Загрузка архива во временный файл
                string tempZip = Path.Combine(Path.GetTempPath(), $"xmrig_{Guid.NewGuid():N}.zip");
                try
                {
                    logCallback?.Invoke($"📥 Загрузка официального релиза {tagName}: {winZipName} (размер: ~{winZipSize / 1024} КБ)...");
                    using (var downloadResp = await _httpClient.GetAsync(winZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        downloadResp.EnsureSuccessStatusCode();
                        long totalBytes = downloadResp.Content.Headers.ContentLength ?? winZipSize;
                        using var contentStream = await downloadResp.Content.ReadAsStreamAsync(ct);
                        using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                        byte[] buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        DateTime lastLog = DateTime.Now;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, bytesRead, ct);
                            totalRead += bytesRead;

                            if ((DateTime.Now - lastLog).TotalMilliseconds > 300 && totalBytes > 0)
                            {
                                int pct = (int)((totalRead * 100) / totalBytes);
                                if (pct > 100) pct = 100;
                                progressCallback?.Invoke(pct);
                                lastLog = DateTime.Now;
                            }
                        }
                    }

                    // 4. Проверка контрольной суммы SHA-256
                    logCallback?.Invoke("🛡️ Проверка целостности загруженного файла (SHA-256)...");
                    string actualSha256 = "";
                    using (var sha = SHA256.Create())
                    {
                        using var stream = File.OpenRead(tempZip);
                        byte[] hashBytes = await sha.ComputeHashAsync(stream, ct);
                        actualSha256 = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }

                    if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, "", $"❌ НЕСОВПАДЕНИЕ КОНТРОЛЬНОЙ СУММЫ SHA-256!\nОжидалось: {expectedSha256}\nФактически: {actualSha256}\nФайл поврежден или подменен. Установка отменена в целях безопасности.");
                    }

                    logCallback?.Invoke("✅ Контрольная сумма SHA-256 полностью совпадает с официальным релизом!");

                    // 5. Распаковка ТОЛЬКО необходимого исполняемого файла (без запуска!)
                    logCallback?.Invoke("📦 Извлечение xmrig.exe в локальный кэш шаблона...");
                    using (var zipArchive = ZipFile.OpenRead(tempZip))
                    {
                        ZipArchiveEntry? targetEntry = null;
                        foreach (var entry in zipArchive.Entries)
                        {
                            if (entry.Name.Equals("xmrig.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                targetEntry = entry;
                                break;
                            }
                        }

                        if (targetEntry == null)
                        {
                            return (false, "", "В официальном архиве XMRig не найден файл 'xmrig.exe'.");
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(targetWorkerPath)!);
                        targetEntry.ExtractToFile(targetWorkerPath, true);
                    }

                    // 6. Сохранение метаданных версии
                    string versionFile = Path.Combine(Path.GetDirectoryName(targetWorkerPath)!, "version_monero.json");
                    var vInfo = new
                    {
                        worker = "xmrig.exe",
                        version = tagName,
                        source = winZipUrl,
                        sha256 = actualSha256,
                        verified = true,
                        installedAt = DateTime.UtcNow.ToString("o")
                    };
                    File.WriteAllText(versionFile, JsonSerializer.Serialize(vInfo, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

                    logCallback?.Invoke($"🎉 Компонент Monero ({tagName}) успешно установлен в шаблон: {targetWorkerPath}");
                    return (true, targetWorkerPath, $"XMRig {tagName} успешно загружен и верифицирован по SHA-256.");
                }
                finally
                {
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                }
            }
            catch (Exception ex)
            {
                return (false, "", "Ошибка при подготовке воркера Monero: " + ex.Message);
            }
        }

        /// <summary>
        /// Проверка официального источника lolMiner (github.com/Lolliedieb/lolMiner-releases)
        /// Если официальный релиз не предоставляет файл контрольных сумм,
        /// автоматическая загрузка отклоняется в целях безопасности.
        /// </summary>
        private static async Task<(bool success, string workerPath, string message)> PrepareEtcWorkerAsync(
            string targetWorkerPath,
            Action<string>? logCallback,
            Action<int>? progressCallback,
            CancellationToken ct)
        {
            try
            {
                logCallback?.Invoke("🌐 Обращение к официальному API релизов lolMiner (github.com/Lolliedieb/lolMiner-releases)...");

                string apiUrl = "https://api.github.com/repos/Lolliedieb/lolMiner-releases/releases/latest";
                using var resp = await _httpClient.GetAsync(apiUrl, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    return (false, "", $"Не удалось получить информацию об официальном релизе lolMiner (HTTP {(int)resp.StatusCode}). Проверьте интернет-соединение.");
                }

                string releaseJson = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(releaseJson);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? (tagElem.GetString() ?? "") : "";
                if (!root.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                {
                    return (false, "", "В официальном релизе lolMiner отсутствуют файлы ассетов.");
                }

                string checksumUrl = "";
                string winZipUrl = "";
                string winZipName = "";

                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                    string downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? (u.GetString() ?? "") : "";

                    if (name.IndexOf("sha256", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("checksum", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        checksumUrl = downloadUrl;
                    }
                    else if (name.EndsWith("_Win64.zip", StringComparison.OrdinalIgnoreCase) && !name.Contains("cln"))
                    {
                        winZipUrl = downloadUrl;
                        winZipName = name;
                    }
                }

                // В соответствии с требованием безопасности: если официальный релиз не предоставляет checksum,
                // не скачивать файл вслепую, а уведомить пользователя.
                if (string.IsNullOrEmpty(checksumUrl))
                {
                    string warn = $"Официальный релиз lolMiner {tagName} на GitHub не публикует файл контрольных сумм (SHA256SUMS).\n\nВ соответствии с политикой безопасности и целостности автоматическая загрузка остановлена.\n\nПожалуйста, поместите проверенный исполняемый файл вручную в:\n{targetWorkerPath}";
                    logCallback?.Invoke("⚠️ Внимание: Официальный релиз lolMiner не содержит опубликованного файла контрольных сумм.");
                    return (false, "", warn);
                }

                // Если контрольная сумма предоставлена — производим загрузку и сверку
                logCallback?.Invoke($"📥 Загрузка манифеста контрольных сумм: {Path.GetFileName(checksumUrl)}...");
                string checksumContent = await _httpClient.GetStringAsync(checksumUrl, ct);

                string expectedSha256 = "";
                foreach (var line in checksumContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains(winZipName, StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 1 && parts[0].Length == 64)
                        {
                            expectedSha256 = parts[0].Trim().ToLowerInvariant();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(expectedSha256))
                {
                    return (false, "", $"Контрольная сумма для '{winZipName}' не найдена в файле {Path.GetFileName(checksumUrl)}.");
                }

                string tempZip = Path.Combine(Path.GetTempPath(), $"lolminer_{Guid.NewGuid():N}.zip");
                try
                {
                    logCallback?.Invoke($"📥 Загрузка {winZipName}...");
                    using (var downloadResp = await _httpClient.GetAsync(winZipUrl, ct))
                    {
                        downloadResp.EnsureSuccessStatusCode();
                        using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                        await downloadResp.Content.CopyToAsync(fs, ct);
                    }

                    using (var sha = SHA256.Create())
                    {
                        using var stream = File.OpenRead(tempZip);
                        byte[] hashBytes = await sha.ComputeHashAsync(stream, ct);
                        string actualSha256 = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            return (false, "", $"Несовпадение контрольной суммы SHA-256 для {winZipName}!");
                        }
                    }

                    using (var zipArchive = ZipFile.OpenRead(tempZip))
                    {
                        ZipArchiveEntry? targetEntry = null;
                        foreach (var entry in zipArchive.Entries)
                        {
                            if (entry.Name.Equals("lolMiner.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                targetEntry = entry;
                                break;
                            }
                        }

                        if (targetEntry == null)
                        {
                            return (false, "", "Файл 'lolMiner.exe' не найден в архиве.");
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(targetWorkerPath)!);
                        targetEntry.ExtractToFile(targetWorkerPath, true);
                    }

                    // Сохранение метаданных версии
                    string versionFile = Path.Combine(Path.GetDirectoryName(targetWorkerPath)!, "version_etc.json");
                    var vInfo = new
                    {
                        worker = "lolMiner.exe",
                        version = tagName,
                        source = winZipUrl,
                        sha256 = expectedSha256,
                        verified = true,
                        installedAt = DateTime.UtcNow.ToString("o")
                    };
                    File.WriteAllText(versionFile, JsonSerializer.Serialize(vInfo, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

                    logCallback?.Invoke($"🎉 Компонент ETC ({tagName}) успешно установлен в шаблон: {targetWorkerPath}");
                    return (true, targetWorkerPath, $"lolMiner {tagName} успешно установлен.");
                }
                finally
                {
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                }
            }
            catch (Exception ex)
            {
                return (false, "", "Ошибка при подготовке воркера ETC: " + ex.Message);
            }
        }
    }
}
