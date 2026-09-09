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
using System.Windows.Media.Imaging;

namespace NexusBuilder
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "8.0.0";
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private string? _cachedIsccPath;
        private string _activeIconPath = "";
        private bool _isInitialized = false;

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            
            string defaultOut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "NEXUS_Builds_v8.0.0"
            );
            tbOutputPath.Text = defaultOut;

            CheckInnoSetupStatus();
            InitializeDefaultIcon();

            Log("⚡ NEXUS Universal Application & Installer Builder v" + AppVersion + " инициализирован.");
            Log("• Доступна сборка: Папка с файлами (Multi-file), Single-File (.exe) и Setup Инсталлятор.");
            Log("• Поддержка кастомных иконок (.ico) и автоматическая распаковка в Program Files.");
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

        private void InitializeDefaultIcon()
        {
            SelectPresetIcon("thunder");
        }

        private string GetPresetIconPath(string tag)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string repoIcons = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer\builder-wpf\Resources\Icons";

            string fileName = tag switch
            {
                "fire" => "fire.ico",
                "singer" => "singer.ico",
                "svyaz" => "svyaz.ico",
                "cyber" => "cyber.ico",
                _ => "thunder.ico"
            };

            string localPath = Path.Combine(baseDir, "Resources", "Icons", fileName);
            if (File.Exists(localPath)) return localPath;

            string repoPath = Path.Combine(repoIcons, fileName);
            if (File.Exists(repoPath)) return repoPath;

            string fallback = Path.Combine(baseDir, "app.ico");
            if (File.Exists(fallback)) return fallback;

            return @"C:\Users\user\.gemini\antigravity\scratch\file-transfer\builder-wpf\app.ico";
        }

        private void SelectPresetIcon(string tag)
        {
            string path = GetPresetIconPath(tag);
            SetIcon(path, $"{tag}.ico (Встроенная)");
        }

        private void SetIcon(string path, string displayName)
        {
            _activeIconPath = path;
            if (lblIconName != null)
            {
                lblIconName.Text = displayName;
            }

            if (imgIconPreview != null)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgIconPreview.Source = bmp;
                    }
                    else
                    {
                        imgIconPreview.Source = null;
                    }
                }
                catch
                {
                    imgIconPreview.Source = null;
                }
            }
        }

        private void CbIconPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || cbIconPresets == null) return;

            if (cbIconPresets.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "thunder";
                if (tag == "__custom_ico__")
                {
                    BtnBrowseIcon_Click(sender, e);
                }
                else
                {
                    SelectPresetIcon(tag);
                }
            }
        }

        private void BtnBrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Title = "Выберите файл иконки приложения (.ico)";
            ofd.Filter = "Иконки (*.ico)|*.ico|Все файлы (*.*)|*.*";
            ofd.FilterIndex = 1;

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetIcon(ofd.FileName, Path.GetFileName(ofd.FileName));
                Log($"🎨 Выбрана пользовательская иконка: {ofd.FileName}");
            }
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
            if (!_isInitialized || cbOperators == null) return;

            if (cbOperators.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                bool isCustom = tag == "__custom__";
                if (tbCustomOperator != null)
                {
                    tbCustomOperator.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
                    if (isCustom) tbCustomOperator.Focus();
                }

                // Автоматически подбираем пресет иконки под выбранного оператора
                if (cbIconPresets != null)
                {
                    string targetIconTag = tag.ToLowerInvariant() switch
                    {
                        "dildman" => "fire",
                        "singer1isss" => "singer",
                        "saha_kakaha122" => "svyaz",
                        "huilaebanaya" => "cyber",
                        _ => "thunder"
                    };

                    for (int i = 0; i < cbIconPresets.Items.Count; i++)
                    {
                        if (cbIconPresets.Items[i] is ComboBoxItem cbi && cbi.Tag?.ToString() == targetIconTag)
                        {
                            cbIconPresets.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
        }

        private void CbPackageFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || cbPackageFormat == null) return;

            if (cbPackageFormat.SelectedItem is ComboBoxItem item)
            {
                string format = item.Tag?.ToString() ?? "multifile";
                bool isMulti = format == "multifile";

                if (txtStandaloneBtnSub != null)
                {
                    txtStandaloneBtnSub.Text = isMulti ? "Папка с файлами (Multi-file)" : "Единый файл (.exe)";
                }
                if (txtClientBtnSub != null)
                {
                    txtClientBtnSub.Text = isMulti ? "Папка с файлами (Multi-file)" : "Единый файл (.exe)";
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
            bool isMulti = GetIsMultiFile();
            await RunBuild(isStandalone: true, createInstaller: false, isMultiFile: isMulti);
        }

        private async void BtnBuildClient_Click(object sender, RoutedEventArgs e)
        {
            bool isMulti = GetIsMultiFile();
            await RunBuild(isStandalone: false, createInstaller: false, isMultiFile: isMulti);
        }

        private async void BtnBuildInstaller_Click(object sender, RoutedEventArgs e)
        {
            bool isStandalone = true;
            if (cbInstallerTarget.SelectedItem is ComboBoxItem cbi && cbi.Tag?.ToString() == "client")
            {
                isStandalone = false;
            }
            // Инсталлятор всегда упаковывает распакованную Multi-file структуру (не сингл файл!)
            await RunBuild(isStandalone: isStandalone, createInstaller: true, isMultiFile: true);
        }

        private bool GetIsMultiFile()
        {
            if (cbPackageFormat.SelectedItem is ComboBoxItem cbi)
            {
                return cbi.Tag?.ToString() == "multifile";
            }
            return true;
        }

        private async Task RunBuild(bool isStandalone, bool createInstaller, bool isMultiFile)
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

            string formatTitle = isMultiFile ? "Папка с файлами (Multi-file)" : "Single-File (.exe)";

            string targetOutputName = createInstaller
                ? $"NEXUS_Setup_{opName}.exe"
                : (isMultiFile 
                    ? (isStandalone ? $"NEXUS_Standalone_{opName}" : $"NEXUS_Client_{opName}")
                    : (isStandalone ? $"NEXUS_Standalone_{opName}.exe" : $"NEXUS_Client_{opName}.exe"));

            string outputFullPath = Path.Combine(outDir, targetOutputName);

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
            Log($"📦 Формат структуры: {formatTitle}");
            Log($"🎨 Иконка: {Path.GetFileName(_activeIconPath)}");
            Log($"💾 Путь назначения: {outputFullPath}");

            bool success = false;

            await Task.Run(async () =>
            {
                try
                {
                    if (isMultiFile)
                    {
                        // ── СБОРКА MULTI-FILE (НЕ В СИНГЛ ФАЙЛЕ) ──────────────────────────
                        string multiDir = await EnsureMultiFileTemplate(isStandalone);
                        if (string.IsNullOrEmpty(multiDir) || !Directory.Exists(multiDir))
                        {
                            Log("❌ ОШИБКА: Не удалось получить шаблон Multi-file!");
                            return;
                        }

                        string stagingDir = createInstaller
                            ? Path.Combine(Path.GetTempPath(), $"NEXUS_Stage_{Guid.NewGuid():N}")
                            : outputFullPath;

                        if (Directory.Exists(stagingDir))
                        {
                            try { Directory.Delete(stagingDir, true); } catch { }
                        }
                        Directory.CreateDirectory(stagingDir);

                        Log("📂 Копирование файлов приложения со всеми DLL...");
                        CopyDirectory(multiDir, stagingDir);

                        // Находим целевой управляемый DLL для внедрения конфигурации
                        string targetDllName = isStandalone ? "RAH PRO.dll" : "RAH Non Pro.dll";
                        string targetDllPath = Path.Combine(stagingDir, targetDllName);

                        if (!File.Exists(targetDllPath))
                        {
                            // Поиск любого подходящего dll
                            var dlls = Directory.GetFiles(stagingDir, "*.dll");
                            foreach (var d in dlls)
                            {
                                if (Path.GetFileName(d).Contains("RAH") || Path.GetFileName(d).Contains("FileTransfer"))
                                {
                                    targetDllPath = d;
                                    break;
                                }
                            }
                        }

                        if (!File.Exists(targetDllPath))
                        {
                            Log("❌ ОШИБКА: Управляемая библиотека DLL не найдена в шаблоне!");
                            return;
                        }

                        Log($"💉 Внедрение параметров оператора в {Path.GetFileName(targetDllPath)}...");
                        bool patchOk = InjectConfigIntoFile(targetDllPath, opName, appName, isStandalone);
                        if (!patchOk)
                        {
                            Log("❌ ОШИБКА внедрения параметров в DLL!");
                            return;
                        }

                        // Установка иконки
                        string targetExeName = isStandalone ? "RAH PRO.exe" : "RAH Non Pro.exe";
                        string targetExePath = Path.Combine(stagingDir, targetExeName);

                        if (File.Exists(_activeIconPath))
                        {
                            Log($"🎨 Внедрение иконки в исполняемый файл: {Path.GetFileName(targetExePath)}...");
                            string destIco = Path.Combine(stagingDir, "app.ico");
                            try { File.Copy(_activeIconPath, destIco, true); } catch { }

                            if (File.Exists(targetExePath))
                            {
                                bool iconInjected = IconInjector.InjectIcon(targetExePath, _activeIconPath);
                                if (iconInjected) Log("   ✅ Иконка успешно встроена в ресурсы PE .exe файла!");
                            }
                        }

                        if (createInstaller)
                        {
                            Log("🛠️ Сборка Setup Инсталлятора через Inno Setup 6...");
                            bool instOk = await CompileInnoSetup(
                                sourceDirectory: stagingDir,
                                outputDir: outDir,
                                outputBaseFilename: $"NEXUS_Setup_{opName}",
                                appName: appName,
                                appExeName: targetExeName,
                                appVersion: AppVersion,
                                opName: opName,
                                iconPath: _activeIconPath,
                                createDesktopShortcut: Dispatcher.Invoke(() => chkDesktopShortcut.IsChecked == true),
                                compressLzma: Dispatcher.Invoke(() => chkCompressLzma.IsChecked == true),
                                runAsAdmin: Dispatcher.Invoke(() => chkRunAsAdmin.IsChecked == true)
                            );

                            try { Directory.Delete(stagingDir, true); } catch { }

                            if (!instOk)
                            {
                                Log("❌ ОШИБКА сборки инсталлятора!");
                                return;
                            }
                        }
                    }
                    else
                    {
                        // ── СБОРКА SINGLE-FILE (.EXE) ────────────────────────────────────
                        string templatePath = await EnsureSingleFileTemplate(isStandalone);
                        if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                        {
                            Log("❌ ОШИБКА: Не удалось получить Single-File шаблон!");
                            return;
                        }

                        Log("💉 Внедрение параметров оператора в Single-File .exe...");
                        Directory.CreateDirectory(outDir);

                        File.Copy(templatePath, outputFullPath, true);
                        bool patchOk = InjectConfigIntoFile(outputFullPath, opName, appName, isStandalone);
                        if (!patchOk)
                        {
                            Log("❌ ОШИБКА внедрения параметров в .exe!");
                            return;
                        }

                        if (File.Exists(_activeIconPath))
                        {
                            Log("🎨 Внедрение иконки в Single-File .exe...");
                            IconInjector.InjectIcon(outputFullPath, _activeIconPath);
                        }
                    }

                    Log($"✅ СБОРКА УСПЕШНО ЗАВЕРШЕНА!");
                    Log($"📁 Расположение: {outputFullPath}");
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
            lblStatus.Text = success ? $"• Готово: {targetOutputName}" : "• Ошибка сборки";
            btnBuildStandalone.IsEnabled = true;
            btnBuildClient.IsEnabled = true;
            btnBuildInstaller.IsEnabled = true;
            btnBrowse.IsEnabled = true;

            if (success)
            {
                btnOpenFolder.Visibility = Visibility.Visible;
                System.Windows.MessageBox.Show(
                    $"Сборка для оператора {opName} успешно создана!\n\nРасположение:\n{outputFullPath}",
                    "NEXUS Builder v8.0.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        private bool InjectConfigIntoFile(string filePath, string opName, string appName, bool isStandalone)
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
                appTitleVersion = "v" + AppVersion,
                windowTitle = $"{appName} {AppVersion}",
                buildMode = isStandalone ? "standalone" : "loader",
                builtAt = DateTime.UtcNow.ToString("o")
            };

            string json = JsonSerializer.Serialize(configData);
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

        private async Task<bool> CompileInnoSetup(
            string sourceDirectory,
            string outputDir,
            string outputBaseFilename,
            string appName,
            string appExeName,
            string appVersion,
            string opName,
            string iconPath,
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

            if (!File.Exists(iconPath))
            {
                iconPath = GetPresetIconPath("thunder");
            }

            string compressionMode = compressLzma ? "lzma2/ultra64" : "lzma2/fast";
            string adminPrivilege = runAsAdmin ? "admin" : "lowest";

            string desktopTask = createDesktopShortcut
                ? "Name: \"desktopicon\"; Description: \"{cm:CreateDesktopIcon}\"; GroupDescription: \"{cm:AdditionalIcons}\"; Flags: unchecked"
                : "";

            string desktopIcon = createDesktopShortcut
                ? $"Name: \"{{autodesktop}}\\{{#MyAppName}}\"; Filename: \"{{app}}\\{{#MyAppExeName}}\"; Tasks: desktopicon; IconFilename: \"{{app}}\\app.ico\""
                : "";

            string setupIconLine = File.Exists(iconPath) ? $"SetupIconFile={iconPath}" : "";
            string iconFileLine = File.Exists(iconPath) ? $"Source: \"{iconPath}\"; DestDir: \"{{app}}\"; DestName: \"app.ico\"; Flags: ignoreversion" : "";

            string issScript = $@"#define MyAppName ""{appName}""
#define MyAppVersion ""{appVersion}""
#define MyAppPublisher ""NEXUS Core""
#define MyAppExeName ""{appExeName}""

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
{setupIconLine}
VersionInfoVersion={appVersion}.0
VersionInfoTextVersion={appVersion}
VersionInfoCompany={{#MyAppPublisher}}
VersionInfoDescription={{#MyAppName}} Setup (Operator: {opName})

[Languages]
Name: ""russian""; MessagesFile: ""compiler:Languages\\Russian.isl""

[Tasks]
{desktopTask}

[Files]
Source: ""{sourceDirectory}\*""; DestDir: ""{{app}}""; Flags: ignoreversion recursesubdirs createallsubdirs
{iconFileLine}

[Icons]
Name: ""{{autoprograms}}\{{#MyAppName}}""; Filename: ""{{app}}\{{#MyAppExeName}}""; IconFilename: ""{{app}}\\app.ico""
{desktopIcon}

[Run]
Filename: ""{{app}}\{{#MyAppExeName}}""; Description: ""{{cm:LaunchProgram,{{#StringChange(MyAppName, '&', '&&')}}}}""; Flags: nowait postinstall skipifsilent runascurrentuser
";

            string tempIssPath = Path.Combine(Path.GetTempPath(), $"nexus_setup_{Guid.NewGuid():N}.iss");
            File.WriteAllText(tempIssPath, issScript, Encoding.UTF8);

            Log($"⚡ Компиляция установщика через Inno Setup: {Path.GetFileName(iscc)}...");
            Log($"📦 Режим: распаковка полного пакета DLL в Program Files, иконка: {Path.GetFileName(iconPath)}");

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

        private async Task<string> EnsureMultiFileTemplate(bool isStandalone)
        {
            string dirName = isStandalone ? "standalone_multifile" : "client_multifile";
            string appDataCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", dirName
            );

            if (Directory.Exists(appDataCache))
            {
                string checkFile = isStandalone ? "RAH PRO.dll" : "RAH Non Pro.dll";
                if (File.Exists(Path.Combine(appDataCache, checkFile)))
                {
                    return appDataCache;
                }
            }

            // Компиляция через dotnet publish
            string repoRoot = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer";
            string projectPath = isStandalone 
                ? Path.Combine(repoRoot, "standalone-shonll", "FileTransfer.csproj")
                : Path.Combine(repoRoot, "client-wpf", "FileTransfer.csproj");

            if (File.Exists(projectPath))
            {
                Log($"🔨 Сборка Multi-file шаблона ({dirName}) через dotnet...");
                Directory.CreateDirectory(appDataCache);

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o \"{appDataCache}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0) return appDataCache;
                }
            }

            return "";
        }

        private async Task<string> EnsureSingleFileTemplate(bool isStandalone)
        {
            string templateName = isStandalone ? "standalone_template.exe" : "client_template.exe";
            string appDataCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", templateName
            );

            if (File.Exists(appDataCache)) return appDataCache;

            string repoRoot = @"C:\Users\user\.gemini\antigravity\scratch\file-transfer";
            string projectPath = isStandalone 
                ? Path.Combine(repoRoot, "standalone-shonll", "FileTransfer.csproj")
                : Path.Combine(repoRoot, "client-wpf", "FileTransfer.csproj");

            if (File.Exists(projectPath))
            {
                Log($"🔨 Сборка Single-File шаблона ({templateName})...");
                string outputDir = Path.Combine(Path.GetTempPath(), "NEXUS_Builder_Single_Stubs", isStandalone ? "standalone" : "client");
                Directory.CreateDirectory(outputDir);

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
                    string built = Path.Combine(outputDir, exeName);
                    if (File.Exists(built))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(appDataCache)!);
                        File.Copy(built, appDataCache, true);
                        return appDataCache;
                    }
                }
            }

            return "";
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSub = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSub);
            }
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