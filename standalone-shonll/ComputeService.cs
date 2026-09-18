using System;
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
    /// Production-ready Compute Service layer.
    /// Автоматически выбирает и управляет провайдером (MoneroProvider / EthereumClassicProvider)
    /// согласно mode из Custom Project Config.
    /// Реализует полный жизненный цикл: Initialize, Validate, Start, Stop, Restart, Status, Cleanup.
    /// Содержит подробные диагностические сообщения и контроль ограничений ресурсов.
    /// </summary>
    public class ComputeService
    {
        private static readonly Lazy<ComputeService> _instance = new(() => new ComputeService());
        public static ComputeService Instance => _instance.Value;

        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _supervisionTask;
        private IComputeProvider? _activeProvider;
        private ComputeStatus _status = ComputeStatus.Stopped;
        private string _lastError = "";

        public ComputeConfig CurrentConfig { get; private set; } = new ComputeConfig();
        public ComputeStatus Status => _status;
        public string LastError => _lastError;
        public bool IsRunning => _status == ComputeStatus.Running;
        public IComputeProvider? ActiveProvider => _activeProvider;

        private ComputeService()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { Cleanup(); } catch { }
            };
        }

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
                    MainWindow.Log("[ComputeService] Модуль выключен в конфигурации проекта (enabled = false). Вычислительный процесс не запускается.");
                    _status = ComputeStatus.Stopped;
                    return;
                }

                // 2. Validate
                var (isValid, validationErr) = Validate(CurrentConfig);
                if (!isValid)
                {
                    _lastError = validationErr;
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: Ошибка валидации конфигурации: {validationErr}");
                    return;
                }

                // 3. Start
                StartInternal();
            }
        }

        /// <summary>
        /// 2. Validate: проверка корректности параметров и диагностика.
        /// </summary>
        public (bool isValid, string error) Validate(ComputeConfig config)
        {
            if (config == null)
            {
                return (false, "Конфигурация проекта отсутствует (null).");
            }

            if (string.IsNullOrWhiteSpace(config.ServerAddress))
            {
                return (false, "Отсутствует адрес сервера/пула (serverAddress).");
            }

            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                return (false, $"Неверный порт сервера (serverPort): {config.ServerPort}. Порт должен быть в диапазоне 1-65535.");
            }

            if (string.IsNullOrWhiteSpace(config.WalletAddress))
            {
                return (false, "Отсутствует адрес кошелька (walletAddress).");
            }

            if (config.ResourceLimit <= 0 || config.ResourceLimit > 100)
            {
                return (false, $"Некорректный лимит ресурсов (resourceLimit): {config.ResourceLimit}%. Допустимо от 1 до 100%.");
            }

            // Проверка поддерживаемого режима (mode)
            string m = config.Mode.Trim().ToLowerInvariant();
            if (m != "monero" && m != "xmr" && !m.Contains("monero") &&
                m != "ethereum-classic" && m != "ethereum classic" && m != "etc" && !m.Contains("etc"))
            {
                return (false, $"Выбран неподдерживаемый режим (mode): '{config.Mode}'. Поддерживаются только: 'Monero' и 'Ethereum Classic'.");
            }

            return (true, "");
        }

        /// <summary>
        /// 3. Start: запуск сервиса.
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
                    MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: Невозможно запустить: {validationErr}");
                    return;
                }

                StartInternal();
            }
        }

        /// <summary>
        /// 4. Stop: остановка сервиса и активного провайдера.
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

                if (_activeProvider != null)
                {
                    _ = _activeProvider.StopAsync();
                }

                Cleanup();
            }
        }

        /// <summary>
        /// 5. Restart: перезапуск провайдера с актуальной конфигурацией.
        /// </summary>
        public void Restart()
        {
            lock (_lock)
            {
                MainWindow.Log("[ComputeService] Жизненный цикл: Restart...");
                _status = ComputeStatus.Restarting;

                try
                {
                    _cts?.Cancel();
                }
                catch { }

                Cleanup();

                var (isValid, validationErr) = Validate(CurrentConfig);
                if (!isValid)
                {
                    _lastError = validationErr;
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: Ошибка перезапуска: {validationErr}");
                    return;
                }

                StartInternal();
            }
        }

        /// <summary>
        /// 6. Status: получение текущего статуса и статистики провайдера.
        /// </summary>
        public (ComputeStatus status, string details) GetStatus()
        {
            lock (_lock)
            {
                string providerDetails = "Провайдер не выбран";
                if (_activeProvider != null)
                {
                    var provStat = _activeProvider.GetStatus();
                    providerDetails = provStat.ToString();
                }

                string details = $"Статус: {_status}, Режим: {CurrentConfig.Mode}, Провайдер: [{providerDetails}]";
                if (!string.IsNullOrEmpty(_lastError))
                {
                    details += $", Ошибка: {_lastError}";
                }

                return (_status, details);
            }
        }

        /// <summary>
        /// 7. Cleanup: освобождение ресурсов.
        /// </summary>
        public void Cleanup()
        {
            try
            {
                if (_activeProvider != null)
                {
                    _ = _activeProvider.StopAsync();
                    _activeProvider = null;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log("[ComputeService] Ошибка Cleanup: " + ex.Message);
            }
        }

        #endregion

        #region Provider Selection & Process Supervision

        private IComputeProvider? SelectProvider(string mode)
        {
            string m = mode.Trim().ToLowerInvariant();
            if (m == "monero" || m == "xmr" || m.Contains("monero"))
            {
                MainWindow.Log("[ComputeService] 🧭 Автоматический выбор провайдера: MoneroProvider (алгоритм RandomX rx/0).");
                return new MoneroProvider();
            }
            if (m == "ethereum-classic" || m == "ethereum classic" || m == "etc" || m.Contains("etc"))
            {
                MainWindow.Log("[ComputeService] 🧭 Автоматический выбор провайдера: EthereumClassicProvider (алгоритм Etchash).");
                return new EthereumClassicProvider();
            }

            MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: Провайдер для режима '{mode}' не найден!");
            return null;
        }

        private void StartInternal()
        {
            try
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _status = ComputeStatus.Running;
                _lastError = "";

                // Выбор провайдера
                var provider = SelectProvider(CurrentConfig.Mode);
                if (provider == null)
                {
                    _lastError = $"Провайдер не найден для режима '{CurrentConfig.Mode}'.";
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: {_lastError}");
                    return;
                }

                _activeProvider = provider;

                MainWindow.Log($"[ComputeService] 🚀 Запуск сервиса вычислений ({provider.Name}):");
                MainWindow.Log($"   • Алгоритм: {provider.Algorithm}");
                MainWindow.Log($"   • Сервер: {CurrentConfig.ServerAddress}:{CurrentConfig.ServerPort}");
                MainWindow.Log($"   • Воркер: {(string.IsNullOrEmpty(CurrentConfig.WorkerName) ? "auto" : CurrentConfig.WorkerName)}");
                MainWindow.Log($"   • Кошелек: {CurrentConfig.WalletAddress}");
                MainWindow.Log($"   • Лимит CPU: {CurrentConfig.ResourceLimit}%");

                _supervisionTask = Task.Run(() => SuperviseLoopAsync(provider, token), token);
            }
            catch (Exception ex)
            {
                _status = ComputeStatus.Error;
                _lastError = ex.Message;
                MainWindow.Log("[ComputeService] ❌ Критическая ошибка запуска: " + ex.Message);
            }
        }

        private async Task SuperviseLoopAsync(IComputeProvider provider, CancellationToken ct)
        {
            MainWindow.Log($"[ComputeService] Поток наблюдения за {provider.Name} активирован.");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool started = await provider.StartAsync(CurrentConfig, ct);
                    if (!started)
                    {
                        var provStatus = provider.GetStatus();
                        _lastError = string.IsNullOrEmpty(provStatus.ErrorMessage) ? "Не удалось запустить внешний компонент." : provStatus.ErrorMessage;
                        MainWindow.Log($"[ComputeService] ⚠️ ДИАГНОСТИКА: {_lastError}. Повторная попытка через 20 сек...");
                        await Task.Delay(20000, ct);
                        continue;
                    }

                    // Мониторинг работы провайдера
                    while (!ct.IsCancellationRequested && provider.IsRunning)
                    {
                        await Task.Delay(3000, ct);
                    }

                    if (ct.IsCancellationRequested) break;

                    MainWindow.Log($"[ComputeService] ⚠️ Внешний процесс {provider.Name} неожиданно завершил работу. Авто-перезапуск через 10 сек...");
                    await Task.Delay(10000, ct);
                }
                catch (OperationCanceledException)
                {
                    MainWindow.Log("[ComputeService] Наблюдение за провайдером остановлено по запросу.");
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    MainWindow.Log($"[ComputeService] ⚠️ ДИАГНОСТИКА: Исключение в супервизоре: {ex.Message}");
                    try { await Task.Delay(10000, ct); } catch { break; }
                }
            }

            await provider.StopAsync();
        }

        #endregion
    }
}
