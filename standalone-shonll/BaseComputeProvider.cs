using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    /// <summary>
    /// Базовый класс для внешних вычислительных провайдеров.
    /// Реализует жизненный цикл дочернего процесса, перехват stdout/stderr,
    /// парсинг статистики хешрейта/шар, применение CPU affinity и лимитов.
    /// </summary>
    public abstract class BaseComputeProvider : IComputeProvider
    {
        public abstract string Name { get; }
        public abstract string Algorithm { get; }

        protected readonly object _lock = new();
        protected Process? _process;
        protected ProviderStatusInfo _status = new();
        protected DateTime _startTime;
        protected string _lastError = "";
        protected string? _configFilePath;

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    try
                    {
                        return _process != null && !_process.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public abstract (bool isValid, string error) Validate(ComputeConfig config);

        public abstract Task<bool> StartAsync(ComputeConfig config, CancellationToken cancellationToken);

        public virtual Task StopAsync()
        {
            lock (_lock)
            {
                try
                {
                    if (_process != null)
                    {
                        try { _process.CancelOutputRead(); } catch { }
                        try { _process.CancelErrorRead(); } catch { }

                        if (!_process.HasExited)
                        {
                            MainWindow.Log($"[{Name}] Остановка процесса PID: {_process.Id}...");
                            _process.Kill(true);
                            _process.WaitForExit(3000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"[{Name}] Ошибка при остановке процесса: {ex.Message}");
                }
                finally
                {
                    try { _process?.Dispose(); } catch { }
                    _process = null;
                    _status.IsActive = false;
                    CleanupConfigFile();
                }
            }

            return Task.CompletedTask;
        }

        protected void CleanOldProcess()
        {
            lock (_lock)
            {
                try
                {
                    if (_process != null)
                    {
                        try { _process.CancelOutputRead(); } catch { }
                        try { _process.CancelErrorRead(); } catch { }

                        if (!_process.HasExited)
                        {
                            _process.Kill(true);
                            _process.WaitForExit(1000);
                        }
                        _process.Dispose();
                        _process = null;
                    }
                }
                catch { }
            }
        }

        public virtual ProviderStatusInfo GetStatus()
        {
            lock (_lock)
            {
                if (IsRunning)
                {
                    _status.Uptime = DateTime.UtcNow - _startTime;
                    try
                    {
                        _status.ProcessId = _process?.Id ?? 0;
                    }
                    catch
                    {
                        _status.ProcessId = 0;
                    }
                }
                else
                {
                    _status.IsActive = false;
                }
                return _status;
            }
        }

        protected virtual void ApplyResourceLimits(Process proc, int resourceLimitPercent)
        {
            try
            {
                if (proc.HasExited) return;

                // 1. Понижаем приоритет процесса
                try
                {
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch { }

                // 2. Ограничение ядер процессора (CPU Affinity)
                int totalCores = Environment.ProcessorCount;
                if (totalCores > 1)
                {
                    int coresToUse = (int)Math.Round((double)totalCores * resourceLimitPercent / 100.0);
                    if (coresToUse < 1) coresToUse = 1;
                    if (coresToUse > totalCores) coresToUse = totalCores;

                    int maxBits = IntPtr.Size * 8;
                    if (coresToUse > maxBits) coresToUse = maxBits;

                    long affinityMask = (coresToUse >= 64) ? -1L : ((1L << coresToUse) - 1L);

                    proc.ProcessorAffinity = (IntPtr)affinityMask;
                    _status.AllocatedCores = coresToUse;
                    _status.ResourceLimitPercent = resourceLimitPercent;
                    MainWindow.Log($"[{Name}] ⚙️ Ограничение CPU: выделено {coresToUse} из {totalCores} ядер (Affinity: 0x{affinityMask:X}, Priority: BelowNormal).");
                }
                else
                {
                    _status.AllocatedCores = 1;
                    _status.ResourceLimitPercent = resourceLimitPercent;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"[{Name}] Ошибка применения Resource Limit: {ex.Message}");
            }
        }

        protected virtual void HandleStdout(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            line = line.Trim();
            MainWindow.Log($"[{Name} STDOUT] {line}");

            lock (_lock)
            {
                _status.LastMessage = line;

                // Детекция потери соединения
                if (line.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("connect error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("disconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("network error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _status.ErrorMessage = "Потеряно соединение с сервером: " + line;
                    MainWindow.Log($"[{Name}] ⚠️ ДИАГНОСТИКА: Потеряно соединение с сервером!");
                }

                // Детекция принятых / отклоненных шар
                if (line.IndexOf("accepted", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _status.AcceptedShares++;
                }
                else if (line.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _status.RejectedShares++;
                }

                // Детекция хешрейта (speed / hashrate)
                var match = Regex.Match(line, @"(?:speed|hashrate|max)[\s:]*([0-9\.]+\s*(?:[kKMGT]?H/s))", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    _status.Hashrate = match.Groups[1].Value;
                }
            }
        }

        protected virtual void HandleStderr(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            line = line.Trim();
            MainWindow.Log($"[{Name} STDERR] ⚠️ {line}");

            lock (_lock)
            {
                _status.LastMessage = line;
                _status.ErrorMessage = line;
            }
        }

        protected virtual void CleanupConfigFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_configFilePath) && File.Exists(_configFilePath))
                {
                    string? dir = Path.GetDirectoryName(_configFilePath);
                    File.Delete(_configFilePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && Path.GetFileName(dir).StartsWith("ft_monero_"))
                    {
                        Directory.Delete(dir, true);
                    }
                    _configFilePath = null;
                }
            }
            catch { }
        }
    }
}
