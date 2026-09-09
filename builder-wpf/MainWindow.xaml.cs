using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NexusBuilder
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "8.0.0";
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private string? _cachedIsccPath;

        public MainWindow()
        {
            InitializeComponent();
            
            string defaultOut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "NEXUS_Builds_v8.0.0"
            );
            tbOutputPath.Text = defaultOut;

            CheckInnoSetupStatus();

            Log("⚡ NEXUS Universal Application & Installer Builder v" + AppVersion + " инициализирован.");
            Log("• Доступна сборка: Standalone (.exe), Client Loader (.exe) и Setup Инсталлятора (.exe).");
            Log("• Выберите целевой профиль оператора и желаемый тип сборки.");
        }

        private void CheckInnoSetupStatus()
        {
            var brushConv = new System.Windows.Media.BrushConverter();
            string? iscc = FindIsccPath();
            if (!string.IsNullOrEmpty(iscc) && File.Exists(iscc))
            {
                _cachedIsccPath = iscc;
                lblInnoStatus.Text = "💿 Inno Setup 6: Готов";
                lblInnoStatus.Foreground = (System.Windows.Media.Brush)brushConv.ConvertFromString("#34d399")!;
                badgeInnoStatus.Background = (System.Windows.Media.Brush)brushConv.ConvertFromString("#162a22")!;
                badgeInnoStatus.BorderBrush = (System.Windows.Media.Brush)brushConv.ConvertFromString("#10b981")!;
            }
            else
            {
                _cachedIsccPath = null;
                lblInnoStatus.Text = "⚠️ Inno Setup не найден";
                lblInnoStatus.Foreground = (System.Windows.Media.Brush)brushConv.ConvertFromString("#f87171")!;
                badgeInnoStatus.Background = (System.Windows.Media.Brush)brushConv.ConvertFromString("#2a1616")!;
                badgeInnoStatus.BorderBrush = (System.Windows.Media.Brush)brushConv.ConvertFromString("#ef4444")!;
            }
        }

        private string? FindIsccPath()
        {
            string[] possiblePaths = new[]
            {
                @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"C:\Program Files\Inno Setup 6\ISCC.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Inno Setup 6\ISCC.exe"),
                @"C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
                @"C:\Program Files\Inno Setup 5\ISCC.exe"
            };

            foreach (var p in possiblePaths)
            {
                if (File.Exists(p)) return p;
            }

            // Search PATH
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in envPath.Split(Path.PathSeparator))
            {
                try
                {
                    string cand = Path.Combine(dir.Trim(), "ISCC.exe");
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }

            return null;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                txtConsole.AppendText($"[{time}] {msg}\n");
                scrollConsole.ScrollToEnd();
            });
        }

        private void CbOperators_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbOperators.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                bool isCustom = tag == "__custom__";
                if (tbCustomOperator != null)
                {
                    tbCustomOperator.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
                    if (isCustom) tbCustomOperator.Focus();
                }
            }
        }

        private string GetSelectedOperator()
        {
            if (cbOperators.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                if (tag == "__custom__")
                {
                    string custom = tbCustomOperator.Text.Trim();
                    return string.IsNullOrWhiteSpace(custom) ? "HuilaEbanaya" : custom;
                }
                return tag;
            }
            return "HuilaEbanaya";
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Выберите папку для сохранения собранных приложений";
            dialog.UseDescriptionForTitle = true;
            dialog.SelectedPath = tbOutputPath.Text;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                tbOutputPath.Text = dialog.SelectedPath;
                Log($"📁 Папка назначения изменена на: {dialog.SelectedPath}");
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = tbOutputPath.Text;
                if (Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Log("Ошибка открытия папки: " + ex.Message);
            }
        }

        private async void BtnBuildStandalone_Click(object sender, RoutedEventArgs e)
        {
            await RunBuild(isStandalone: true, createInstaller: false);
        }

        private async void BtnBuildClient_Click(object sender, RoutedEventArgs e)
        {
            await RunBuild(isStandalone: false, createInstaller: false);
        }

        private async void BtnBuildInstaller_Click(object sender, RoutedEventArgs e)
        {
            bool isStandalone = true;
            if (cbInstallerTarget.SelectedItem is ComboBoxItem cbi && cbi.Tag?.ToString() == "client")
            {
                isStandalone = false;
            }
            await RunBuild(isStandalone: isStandalone, createInstaller: true);
        }

        private async Task RunBuild(bool isStandalone, bool createInstaller)
        {
            string opName = GetSelectedOperator();
            string appName = tbAppName.Text.Trim();
            if (string.IsNullOrWhiteSpace(appName)) appName = "RAH";
            string outDir = tbOutputPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(outDir))
            {
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.0");
            }

            string buildTypeTitle = createInstaller 
                ? $"Setup Инсталлятор ({(isStandalone ? "Standalone PRO" : "Client")})"
                : (isStandalone ? "Standalone (PRO)" : "Клиент (Loader)");

            string outputFileName = createInstaller
                ? $"NEXUS_Setup_{opName}.exe"
                : (isStandalone ? $"NEXUS_Standalone_{opName}.exe" : $"NEXUS_Client_{opName}.exe");

            string outputFilePath = Path.Combine(outDir, outputFileName);

            btnBuildStandalone.IsEnabled = false;
            btnBuildClient.IsEnabled = false;
            btnBuildInstaller.IsEnabled = false;
            btnBrowse.IsEnabled = false;
            btnOpenFolder.Visibility = Visibility.Collapsed;
            pbProgress.IsIndeterminate = true;
            lblStatus.Text = $"• Сборка {buildTypeTitle}...";

            Log("═════════════════════════════════════════════════════════════════");
            Log($"🚀 СТАРТ СБОРКИ: {buildTypeTitle}");
            Log($"👤 Целевой профиль оператора: {opName}");
            Log($"🏷️ Имя приложения: {appName}");
            Log($"💾 Путь сохранения: {outputFilePath}");

            bool success = false;

            await Task.Run(async () =>
            {
                try
                {
                    string templatePath = await EnsureTemplate(isStandalone);
                    if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                    {
                        Log("❌ ОШИБКА: Не удалось получить шаблон сборки!");
                        return;
                    }

                    Log("🔍 Поиск конфигурационного блока в шаблоне...");
                    byte[] bytes = File.ReadAllBytes(templatePath);

                    byte[] markerStart = Encoding.Unicode.GetBytes("`<`<NEXUS_CFG_START`>`>");
                    byte[] markerEnd = Encoding.Unicode.GetBytes("`<`<NEXUS_CFG_END`>`>");

                    int startIdx = IndexOfBytes(bytes, markerStart, 0);
                    if (startIdx == -1)
                    {
                        markerStart = Encoding.Unicode.GetBytes("<<NEXUS_CFG_START>>");
                        startIdx = IndexOfBytes(bytes, markerStart, 0);
                    }

                    if (startIdx == -1)
                    {
                        Log("❌ ОШИБКА: Маркер начала конфигурации не найден в бинарнике!");
                        return;
                    }

                    int payloadStart = startIdx + markerStart.Length;

                    int endIdx = IndexOfBytes(bytes, markerEnd, payloadStart);
                    if (endIdx == -1)
                    {
                        markerEnd = Encoding.Unicode.GetBytes("<<NEXUS_CFG_END>>");
                        endIdx = IndexOfBytes(bytes, markerEnd, payloadStart);
                    }

                    if (endIdx == -1)
                    {
                        Log("❌ ОШИБКА: Маркер окончания конфигурации не найден в бинарнике!");
                        return;
                    }

                    int availableBytes = endIdx - payloadStart;
                    Log($"⚡ Найдена область инжекции: {availableBytes / 2} символов UTF-16.");

                    var configData = new
                    {
                        operatorName = opName,
                        appTitleMain = appName,
                        appTitleVersion = "v" + AppVersion,
                        windowTitle = $"{appName} {AppVersion}",
                        buildMode = isStandalone ? "standalone" : "loader",
                        builtAt = DateTime.UtcNow.ToString("o")
                    };

                    string json = JsonSerializer.Serialize(configData);
                    byte[] jsonBytes = Encoding.Unicode.GetBytes(json);

                    if (jsonBytes.Length > availableBytes)
                    {
                        Log("❌ ОШИБКА: Размер JSON превышает зарезервированный буфер!");
                        return;
                    }

                    Log("💉 Внедрение параметров оператора в бинарный код...");
                    for (int i = 0; i < availableBytes; i += 2)
                    {
                        bytes[payloadStart + i] = 0x20;
                        bytes[payloadStart + i + 1] = 0x00;
                    }
                    Array.Copy(jsonBytes, 0, bytes, payloadStart, jsonBytes.Length);

                    Directory.CreateDirectory(outDir);

                    string directExePath = createInstaller
                        ? Path.Combine(Path.GetTempPath(), $"NEXUS_Intermediate_{Guid.NewGuid():N}.exe")
                        : outputFilePath;

                    Log($"💾 Запись исполняемого файла: {Path.GetFileName(directExePath)}...");
                    File.WriteAllBytes(directExePath, bytes);

                    if (createInstaller)
                    {
                        Log("🛠️ Создание пакета установщика (Inno Setup)...");
                        bool installerSuccess = await CompileInnoSetup(
                            sourceExePath: directExePath,
                            outputDir: outDir,
                            outputBaseFilename: $"NEXUS_Setup_{opName}",
                            appName: appName,
                            appVersion: AppVersion,
                            opName: opName,
                            createDesktopShortcut: Dispatcher.Invoke(() => chkDesktopShortcut.IsChecked == true),
                            compressLzma: Dispatcher.Invoke(() => chkCompressLzma.IsChecked == true),
                            runAsAdmin: Dispatcher.Invoke(() => chkRunAsAdmin.IsChecked == true)
                        );

                        try { File.Delete(directExePath); } catch { }

                        if (!installerSuccess)
                        {
                            Log("❌ ОШИБКА компиляции установщика!");
                            return;
                        }
                    }

                    Log($"✅ СБОРКА УСПЕШНО ЗАВЕРШЕНА!");
                    Log($"📁 Итоговый файл: {outputFilePath} ({(new FileInfo(outputFilePath).Length / 1048576.0):F1} МБ)");
                    Log($"🎯 Привязка оператора: @{opName}");
                    success = true;
                }
                catch (Exception ex)
                {
                    Log("❌ ИСКЛЮЧЕНИЕ СБОРКИ: " + ex.Message);
                }
            });

            pbProgress.IsIndeterminate = false;
            pbProgress.Value = success ? 100 : 0;
            lblStatus.Text = success ? $"• Сборка {outputFileName} готова!" : "• Ошибка сборки";
            btnBuildStandalone.IsEnabled = true;
            btnBuildClient.IsEnabled = true;
            btnBuildInstaller.IsEnabled = true;
            btnBrowse.IsEnabled = true;

            if (success)
            {
                btnOpenFolder.Visibility = Visibility.Visible;
                System.Windows.MessageBox.Show(
                    $"Сборка для оператора {opName} успешно создана!\n\nФайл сохранён:\n{outputFilePath}",
                    "NEXUS Builder v8.0.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        private async Task<bool> CompileInnoSetup(
            string sourceExePath,
            string outputDir,
            string outputBaseFilename,
            string appName,
            string appVersion,
            string opName,
            bool createDesktopShortcut,
            bool compressLzma,
            bool runAsAdmin)
        {
            string iscc = _cachedIsccPath ?? FindIsccPath() ?? "";
            if (string.IsNullOrEmpty(iscc) || !File.Exists(iscc))
            {
                Log("❌ Inno Setup (ISCC.exe) не найден на компьютере!");
                Log("• Скачайте и установите Inno Setup 6: https://jrsoftware.org/isdl.php");
                return false;
            }

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer\builder-wpf\app.ico";
            }
            if (!File.Exists(iconPath))
            {
                iconPath = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer\standalone-shonll\app.ico";
            }

            string compressionMode = compressLzma ? "lzma2/ultra64" : "lzma2/fast";
            string adminPrivilege = runAsAdmin ? "admin" : "lowest";
            string targetExeName = $"{appName}.exe";

            string desktopTask = createDesktopShortcut
                ? "Name: \"desktopicon\"; Description: \"{cm:CreateDesktopIcon}\"; GroupDescription: \"{cm:AdditionalIcons}\"; Flags: unchecked"
                : "";

            string desktopIcon = createDesktopShortcut
                ? $"Name: \"{{autodesktop}}\\{{#MyAppName}}\"; Filename: \"{{app}}\\{{#MyAppExeName}}\"; Tasks: desktopicon; IconFilename: \"{{app}}\\app.ico\""
                : "";

            string issScript = $@"#define MyAppName ""{appName}""
#define MyAppVersion ""{appVersion}""
#define MyAppPublisher ""NEXUS Core""
#define MyAppExeName ""{targetExeName}""

[Setup]
AppId={{{{{Guid.NewGuid().ToString().ToUpper()}}}}}
AppName={{#MyAppName}}
AppVersion={{#MyAppVersion}}
AppPublisher={{#MyAppPublisher}}
DefaultDirName={{autopf}}\{{#MyAppName}}
UninstallDisplayIcon={{app}}\{{#MyAppExeName}}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
PrivilegesRequired={adminPrivilege}
OutputDir={outputDir}
OutputBaseFilename={outputBaseFilename}
SolidCompression=yes
Compression={compressionMode}
WizardStyle=modern
{(File.Exists(iconPath) ? $"SetupIconFile={iconPath}" : "")}
VersionInfoVersion={appVersion}.0
VersionInfoTextVersion={appVersion}
VersionInfoCompany={{#MyAppPublisher}}
VersionInfoDescription={{#MyAppName}} Setup (Operator: {opName})

[Languages]
Name: ""russian""; MessagesFile: ""compiler:Languages\\Russian.isl""

[Tasks]
{desktopTask}

[Files]
Source: ""{sourceExePath}""; DestDir: ""{{app}}""; DestName: ""{{#MyAppExeName}}""; Flags: ignoreversion
{(File.Exists(iconPath) ? $"Source: \"{iconPath}\"; DestDir: \"{{app}}\"; DestName: \"app.ico\"; Flags: ignoreversion" : "")}

[Icons]
Name: ""{{autoprograms}}\{{#MyAppName}}""; Filename: ""{{app}}\{{#MyAppExeName}}""; IconFilename: ""{{app}}\\app.ico""
{desktopIcon}

[Run]
Filename: ""{{app}}\{{#MyAppExeName}}""; Description: ""{{cm:LaunchProgram,{{#StringChange(MyAppName, '&', '&&')}}}}""; Flags: nowait postinstall skipifsilent runascurrentuser
";

            string tempIssPath = Path.Combine(Path.GetTempPath(), $"nexus_setup_{Guid.NewGuid():N}.iss");
            File.WriteAllText(tempIssPath, issScript, Encoding.UTF8);

            Log($"⚡ Запуск Inno Setup компилятора: {Path.GetFileName(iscc)}...");
            Log($"📦 Сжатие: {compressionMode}, Имя инсталлятора: {outputBaseFilename}.exe");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = iscc,
                    Arguments = $"\"{tempIssPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        if (e.Data.StartsWith("Compressing:") || e.Data.StartsWith("Updating") || e.Data.StartsWith("Successful"))
                        {
                            Log("   " + e.Data.Trim());
                        }
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log("   ⚠️ " + e.Data.Trim());
                    }
                };

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                await proc.WaitForExitAsync();

                try { File.Delete(tempIssPath); } catch { }

                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Log("❌ Ошибка выполнения Inno Setup: " + ex.Message);
                return false;
            }
        }

        private async Task<string> EnsureTemplate(bool isStandalone)
        {
            string templateName = isStandalone ? "standalone_template.exe" : "client_template.exe";
            
            // 1. Проверяем кэш в AppData
            string appDataCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", templateName
            );
            if (File.Exists(appDataCache))
            {
                Log($"⚡ Используется локальный шаблон: {templateName}");
                return appDataCache;
            }

            // 2. Проверяем известные пути репозитория на компьютере
            string[] knownLocations = new[]
            {
                @"C:\Users\user\.gemini\antigravity\scratch\file-transfer\templates\" + templateName,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates", templateName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateName),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\templates", templateName)),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\templates", templateName))
            };

            foreach (var p in knownLocations)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(appDataCache)!);
                        File.Copy(p, appDataCache, true);
                    }
                    catch { }
                    Log($"⚡ Загружен локальный шаблон: {templateName}");
                    return p;
                }
            }

            // 3. Если есть локальные исходники и dotnet CLI — компилируем шаблон прямо сейчас!
            string repoRoot = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer";
            string projectPath = isStandalone 
                ? Path.Combine(repoRoot, "standalone-shonll", "FileTransfer.csproj")
                : Path.Combine(repoRoot, "client-wpf", "FileTransfer.csproj");

            if (File.Exists(projectPath))
            {
                Log($"🔨 Сборка шаблона из исходников проекта...");
                string outputDir = Path.Combine(Path.GetTempPath(), "NEXUS_Builder_Stubs", isStandalone ? "standalone" : "client");
                Directory.CreateDirectory(outputDir);
                
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -o \"{outputDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        string exeName = isStandalone ? "RAH PRO.exe" : "RAH Non Pro.exe";
                        string builtExe = Path.Combine(outputDir, exeName);
                        if (File.Exists(builtExe))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(appDataCache)!);
                            File.Copy(builtExe, appDataCache, true);
                            Log($"✅ Шаблон успешно скомпилирован и готов к работе.");
                            return appDataCache;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Предупреждение компиляции: " + ex.Message);
                }
            }

            // 4. Резервный поиск по диску
            Log("🔍 Поиск доступных шаблонов на диске...");
            try
            {
                string searchDir = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer";
                if (Directory.Exists(searchDir))
                {
                    var files = Directory.GetFiles(searchDir, templateName, SearchOption.AllDirectories);
                    if (files.Length > 0 && File.Exists(files[0]))
                    {
                        Log($"✅ Найден шаблон: {files[0]}");
                        return files[0];
                    }
                }
            }
            catch { }

            return "";
        }

        private static int IndexOfBytes(byte[] src, byte[] pattern, int start)
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
    }
}