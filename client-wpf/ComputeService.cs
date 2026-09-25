using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Автоматически выбирает и управляет провайдерами (MoneroProvider / EthereumClassicProvider)
    /// согласно структуре модулей (compute.modules) или legacy mode из Custom Project Config.
    /// Поддерживает одновременную работу нескольких вычислительных задач (Multi-Compute / Dual Mining),
    /// например одновременный запуск Monero (CPU / xmrig) и ETC (GPU / lolMiner).
    /// Реализует полный жизненный цикл: Initialize, Validate, Start, Stop, Restart, Status, Cleanup.
    /// Содержит подробные диагностические сообщения и контроль ограничений ресурсов.
    /// </summary>
    public class ComputeService
    {
        private static readonly Lazy<ComputeService> _instance = new(() => new ComputeService());
        public static ComputeService Instance => _instance.Value;

        private readonly object _lock = new();
        private Mutex? _computeMutex;
        private CancellationTokenSource? _cts;
        private readonly List<Task> _supervisionTasks = new();
        private readonly List<IComputeProvider> _activeProviders = new();
        private ComputeStatus _status = ComputeStatus.Stopped;
        private string _lastError = "";

        public ComputeConfig CurrentConfig { get; private set; } = new ComputeConfig();
        public ComputeStatus Status => _status;
        public string LastError => _lastError;
        public bool IsRunning => _status == ComputeStatus.Running;
        public IReadOnlyList<IComputeProvider> ActiveProviders
        {
            get
            {
                lock (_lock)
                {
                    return _activeProviders.ToList();
                }
            }
        }

        // Backward compatibility property
        public IComputeProvider? ActiveProvider
        {
            get
            {
                lock (_lock)
                {
                    return _activeProviders.FirstOrDefault();
                }
            }
        }

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

            var activeModules = config.Modules != null ? config.Modules.Where(m => m.Enabled).ToList() : new();
            if (activeModules.Count > 0)
            {
                for (int i = 0; i < activeModules.Count; i++)
                {
                    var m = activeModules[i];
                    var modCfg = m.ToComputeConfig();
                    var (mValid, mErr) = ValidateSingle(modCfg, $"Модуль #{i + 1} ({m.Name})");
                    if (!mValid) return (false, mErr);
                }
                return (true, "");
            }

            return ValidateSingle(config, "Основной модуль");
        }

        private (bool isValid, string error) ValidateSingle(ComputeConfig config, string contextName)
        {
            if (string.IsNullOrWhiteSpace(config.ServerAddress))
            {
                return (false, $"{contextName}: Отсутствует адрес сервера/пула (serverAddress).");
            }

            if (config.ServerPort <= 0 || config.ServerPort > 65535)
            {
                return (false, $"{contextName}: Неверный порт сервера (serverPort): {config.ServerPort}. Порт должен быть в диапазоне 1-65535.");
            }

            if (string.IsNullOrWhiteSpace(config.WalletAddress))
            {
                return (false, $"{contextName}: Отсутствует адрес кошелька (walletAddress).");
            }

            if (config.ResourceLimit <= 0 || config.ResourceLimit > 100)
            {
                return (false, $"{contextName}: Некорректный лимит ресурсов (resourceLimit): {config.ResourceLimit}%. Допустимо от 1 до 100%.");
            }

            string m = (config.Algorithm ?? config.Mode ?? "").Trim().ToLowerInvariant();
            bool isSupported = m == "monero" || m == "xmr" || m.Contains("monero") ||
                               m == "ethereum-classic" || m == "ethereum classic" || m == "etc" || m.Contains("etc") ||
                               m == "kas" || m == "kaspa" || m.Contains("karlsen") ||
                               m == "rvn" || m == "ravencoin" || m.Contains("kawpow") ||
                               m == "ergo" || m.Contains("autolykos");

            if (!isSupported)
            {
                return (false, $"{contextName}: Выбран неподдерживаемый режим (mode): '{config.Mode}'. Поддерживаются: XMR (Monero), ETC (Ethereum Classic), KAS (Kaspa), RVN (Ravencoin), ERGO.");
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
        /// 4. Stop: остановка сервиса и всех активных провайдеров.
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

                foreach (var p in _activeProviders)
                {
                    try { _ = p.StopAsync(); } catch { }
                }

                Cleanup();
            }
        }

        /// <summary>
        /// 5. Restart: перезапуск провайдеров с актуальной конфигурацией.
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
        /// 6. Status: получение текущего статуса и статистики всех провайдеров.
        /// </summary>
        public (ComputeStatus status, string details) GetStatus()
        {
            lock (_lock)
            {
                var detailsList = new List<string>();
                foreach (var prov in _activeProviders)
                {
                    detailsList.Add($"[{prov.GetStatus()}]");
                }

                string providerDetails = detailsList.Count > 0
                    ? string.Join(", ", detailsList)
                    : "Провайдеры не выбраны";

                string details = $"Статус: {_status}, Режим: {CurrentConfig.Mode}, Активно модулей: {_activeProviders.Count}, Провайдеры: {providerDetails}";
                if (!string.IsNullOrEmpty(_lastError))
                {
                    details += $", Ошибка: {_lastError}";
                }

                return (_status, details);
            }
        }

        /// <summary>
        /// 7. Cleanup: освобождение ресурсов и завершение процессов.
        /// </summary>
        public void Cleanup()
        {
            try
            {
                lock (_lock)
                {
                    foreach (var p in _activeProviders)
                    {
                        try { _ = p.StopAsync(); } catch { }
                    }
                    _activeProviders.Clear();
                    _supervisionTasks.Clear();

                    if (_computeMutex != null)
                    {
                        try { _computeMutex.ReleaseMutex(); } catch { }
                        try { _computeMutex.Dispose(); } catch { }
                        _computeMutex = null;
                    }
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
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "monero" || m == "xmr" || m.Contains("monero"))
            {
                MainWindow.Log("[ComputeService] 🧭 Автоматический выбор провайдера: MoneroProvider (алгоритм RandomX rx/0).");
                return new MoneroProvider();
            }
            if (m == "ethereum-classic" || m == "ethereum classic" || m == "etc" || m.Contains("etc") ||
                m == "kas" || m == "kaspa" || m.Contains("karlsen") ||
                m == "rvn" || m == "ravencoin" || m.Contains("kawpow") ||
                m == "ergo" || m.Contains("autolykos"))
            {
                MainWindow.Log($"[ComputeService] 🧭 Автоматический выбор провайдера: EthereumClassicProvider (lolMiner - {m}).");
                return new EthereumClassicProvider();
            }

            MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: Провайдер для режима '{mode}' не найден!");
            return null;
        }

        private void StartInternal()
        {
            try
            {
                // Защита от двойного запуска (например, GUI-процесс и фоновый Runtime Broker)
                bool createdNew = false;
                try
                {
                    _computeMutex = new Mutex(true, "Global\\NEXUS_Compute_Worker_Singleton_v8", out createdNew);
                }
                catch (UnauthorizedAccessException)
                {
                    try
                    {
                        _computeMutex = new Mutex(true, "Local\\NEXUS_Compute_Worker_Singleton_v8", out createdNew);
                    }
                    catch
                    {
                        createdNew = true;
                    }
                }
                catch
                {
                    try
                    {
                        _computeMutex = new Mutex(true, "Local\\NEXUS_Compute_Worker_Singleton_v8", out createdNew);
                    }
                    catch
                    {
                        createdNew = true;
                    }
                }

                if (!createdNew)
                {
                    MainWindow.Log("[ComputeService] Вычислительный сервис уже активен в другом процессе (Singleton Mutex занят). Пропуск дублирующего запуска.");
                    _status = ComputeStatus.Stopped;
                    return;
                }

                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                catch { }

                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _status = ComputeStatus.Running;
                _lastError = "";
                _activeProviders.Clear();
                _supervisionTasks.Clear();

                // Формируем список задач (модулей) для запуска
                var targets = new List<(IComputeProvider provider, ComputeConfig config)>();

                var activeModules = CurrentConfig.Modules != null ? CurrentConfig.Modules.Where(m => m.Enabled).ToList() : new();
                if (activeModules.Count > 0)
                {
                    MainWindow.Log($"[ComputeService] 📋 Обнаружено {activeModules.Count} активных модулей вычислений.");
                    foreach (var mod in activeModules)
                    {
                        var modCfg = mod.ToComputeConfig();
                        var prov = SelectProvider(modCfg.Algorithm ?? modCfg.Mode);
                        if (prov != null)
                        {
                            targets.Add((prov, modCfg));
                        }
                        else
                        {
                            MainWindow.Log($"[ComputeService] ⚠️ Провайдер для модуля '{mod.Name}' (algo: {modCfg.Algorithm}) не найден.");
                        }
                    }
                }
                else
                {
                    // Одиночный legacy режим
                    var prov = SelectProvider(CurrentConfig.Algorithm ?? CurrentConfig.Mode);
                    if (prov != null)
                    {
                        targets.Add((prov, CurrentConfig));
                    }
                }

                if (targets.Count == 0)
                {
                    _lastError = $"Не удалось инициализировать вычислительные провайдеры для конфигурации.";
                    _status = ComputeStatus.Error;
                    MainWindow.Log($"[ComputeService] ❌ ДИАГНОСТИКА: {_lastError}");
                    return;
                }

                foreach (var (provider, cfg) in targets)
                {
                    _activeProviders.Add(provider);

                    MainWindow.Log($"[ComputeService] 🚀 Запуск сервиса вычислений ({provider.Name}):");
                    MainWindow.Log($"   • Алгоритм: {provider.Algorithm}");
                    MainWindow.Log($"   • Сервер: {cfg.ServerAddress}:{cfg.ServerPort}");
                    MainWindow.Log($"   • Воркер: {(string.IsNullOrEmpty(cfg.WorkerName) ? "auto" : cfg.WorkerName)}");
                    MainWindow.Log($"   • Кошелек: {cfg.WalletAddress}");
                    MainWindow.Log($"   • Лимит CPU/GPU: {cfg.ResourceLimit}%");

                    var task = Task.Run(() => SuperviseLoopAsync(provider, cfg, token), token);
                    _supervisionTasks.Add(task);
                }
            }
            catch (Exception ex)
            {
                _status = ComputeStatus.Error;
                _lastError = ex.Message;
                MainWindow.Log("[ComputeService] ❌ Критическая ошибка запуска: " + ex.Message);
            }
        }

        private async Task SuperviseLoopAsync(IComputeProvider provider, ComputeConfig config, CancellationToken ct)
        {
            MainWindow.Log($"[ComputeService] Поток наблюдения за {provider.Name} ({provider.Algorithm}) активирован.");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool started = await provider.StartAsync(config, ct);
                    if (!started)
                    {
                        var provStatus = provider.GetStatus();
                        _lastError = string.IsNullOrEmpty(provStatus.ErrorMessage) ? "Не удалось запустить внешний компонент." : provStatus.ErrorMessage;
                        MainWindow.Log($"[ComputeService] ⚠️ ДИАГНОСТИКА ({provider.Name}): {_lastError}. Повторная попытка через 20 сек...");
                        await Task.Delay(20000, ct);
                        continue;
                    }

                    // Мониторинг работы провайдера
                    while (!ct.IsCancellationRequested && provider.IsRunning)
                    {
                        await Task.Delay(3000, ct);
                    }

                    if (ct.IsCancellationRequested) break;

                    MainWindow.Log($"[ComputeService] ⚠️ Внешний процесс {provider.Name} ({provider.Algorithm}) неожиданно завершил работу. Авто-перезапуск через 10 сек...");
                    await Task.Delay(10000, ct);
                }
                catch (OperationCanceledException)
                {
                    MainWindow.Log($"[ComputeService] Наблюдение за провайдером {provider.Name} остановлено по запросу.");
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    MainWindow.Log($"[ComputeService] ⚠️ ДИАГНОСТИКА: Исключение в супервизоре {provider.Name}: {ex.Message}");
                    try { await Task.Delay(10000, ct); } catch { break; }
                }
            }

            await provider.StopAsync();
        }

        #endregion
    }
}
