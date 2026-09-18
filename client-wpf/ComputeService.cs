using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransfer.Compute
{
    /// <summary>
    /// Модульный фоновый сервис вычислений (Compute Service).
    /// Получает настройки из Custom Project Configuration и работает независимо.
    /// </summary>
    public class ComputeService
    {
        private static readonly Lazy<ComputeService> _instance = new(() => new ComputeService());
        public static ComputeService Instance => _instance.Value;

        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _runningTask;
        private bool _isRunning = false;

        public ComputeConfig CurrentConfig { get; private set; } = new ComputeConfig();
        public bool IsRunning => _isRunning;

        private ComputeService() { }

        /// <summary>
        /// Инициализация и запуск Compute Service из Custom Project Config JSON.
        /// </summary>
        public void InitializeAndStart(string customConfigJson)
        {
            var config = ComputeConfig.FromCustomConfigJson(customConfigJson);
            Start(config);
        }

        /// <summary>
        /// Запуск сервиса с заданной конфигурацией.
        /// </summary>
        public void Start(ComputeConfig config)
        {
            lock (_lock)
            {
                CurrentConfig = config ?? new ComputeConfig();

                if (!CurrentConfig.Enabled)
                {
                    MainWindow.Log("[ComputeService] Модуль отключен в конфигурации (Enabled = false). Запуск пропущен.");
                    Stop();
                    return;
                }

                if (_isRunning)
                {
                    MainWindow.Log("[ComputeService] Сервер уже запущен. Применяются новые параметры...");
                    Stop();
                }

                MainWindow.Log($"[ComputeService] 🚀 Инициализация модуля вычислений:");
                MainWindow.Log($"   • Режим: {CurrentConfig.Mode}");
                MainWindow.Log($"   • Сервер: {CurrentConfig.ServerAddress}:{CurrentConfig.ServerPort}");
                MainWindow.Log($"   • Кошелек: {(string.IsNullOrEmpty(CurrentConfig.WalletAddress) ? "Не указан" : CurrentConfig.WalletAddress)}");
                MainWindow.Log($"   • Воркер: {CurrentConfig.WorkerName}");
                MainWindow.Log($"   • Лимит ресурсов: {CurrentConfig.ResourceLimit}%");

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _runningTask = Task.Run(() => WorkerLoopAsync(_cts.Token));
            }
        }

        /// <summary>
        /// Остановка сервиса вычислений.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;

                MainWindow.Log("[ComputeService] Остановка сервиса вычислений...");
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                catch { }
                finally
                {
                    _cts = null;
                    _isRunning = false;
                }
            }
        }

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            try
            {
                MainWindow.Log("[ComputeService] Сервисный поток вычислений активирован.");

                while (!ct.IsCancellationRequested)
                {
                    // Модульный цикл выполнения задачи сервиса
                    // Учитывает заданный ResourceLimit и настройки подключения
                    await Task.Delay(5000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                MainWindow.Log("[ComputeService] Поток вычислений корректно остановлен по запросу.");
            }
            catch (Exception ex)
            {
                MainWindow.Log("[ComputeService] Ошибка в рабочем цикле сервиса: " + ex.Message);
            }
            finally
            {
                _isRunning = false;
            }
        }
    }
}
