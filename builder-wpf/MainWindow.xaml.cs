using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NexusBuilder
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "8.0.0";
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private string? _cachedIsccPath;
        private string _activeIconPath = "";
        private bool _isInitialized = false;

        private string _currentUser = "";
        private string _authToken = "";
        private bool _isUserAdmin = false;
        private readonly string _authFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NEXUS_Builder", "auth.json"
        );
        private static readonly string API_BASE = "https://file-transfer-production-75ad.up.railway.app";

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            
            string defaultOut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "NEXUS_Builds_v8.0.0"
            );
            tbOutputPath.Text = defaultOut;

            ExtractEmbeddedIcons();
            InitializeDefaultIcon();
            _cachedIsccPath = FindIsccPath();

            Log("⚡ NEXUS Builder v" + AppVersion + " [Cloud Sync] готов к работе.");
            Log("• Доступна сборка: Standalone Инсталлятор (PRO) и Client Инсталлятор.");
            Log("• Облачная синхронизация шаблонов: Активна (автоматическая загрузка).");

            TryAutoLogin();
        }

        private void ExtractEmbeddedIcons()
        {
            try
            {
                string iconDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NEXUS_Builder", "icons"
                );
                Directory.CreateDirectory(iconDir);

                var asm = Assembly.GetExecutingAssembly();
                foreach (string resName in asm.GetManifestResourceNames())
                {
                    if (resName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = resName;
                        int lastDot = resName.LastIndexOf('.', resName.Length - 5);
                        if (lastDot >= 0)
                        {
                            fileName = resName.Substring(lastDot + 1);
                        }

                        string targetPath = Path.Combine(iconDir, fileName);
                        using var s = asm.GetManifestResourceStream(resName);
                        if (s != null)
                        {
                            using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                            s.CopyTo(fs);
                        }
                    }
                }
            }
            catch { }
        }

        private async void TryAutoLogin()
        {
            try
            {
                if (File.Exists(_authFilePath))
                {
                    string json = await File.ReadAllTextAsync(_authFilePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("user", out var u) && root.TryGetProperty("token", out var t))
                    {
                        string user = u.GetString() ?? "";
                        string token = t.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(token))
                        {
                            ApplyUserSession(user, token);
                            return;
                        }
                    }
                }
            }
            catch { }

            ShowLoginScreen();
        }

        private void ShowLoginScreen()
        {
            gridMainBuilder.Visibility = Visibility.Collapsed;
            gridAuthLogin.Visibility = Visibility.Visible;
            badgeUserSession.Visibility = Visibility.Collapsed;
            tbAuthLogin.Focus();
        }

        private void ShowAuthError(string msg)
        {
            txtAuthError.Text = msg;
            borderAuthError.Visibility = Visibility.Visible;
        }

        private async void BtnAuthLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = tbAuthLogin.Text.Trim();
            string password = pbAuthPassword.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowAuthError("⚠️ Введите логин и пароль");
                return;
            }

            btnAuthLogin.IsEnabled = false;
            btnAuthLogin.Content = "ПОДКЛЮЧЕНИЕ...";
            borderAuthError.Visibility = Visibility.Collapsed;

            try
            {
                var payload = JsonSerializer.Serialize(new { username, password });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"{API_BASE}/api/login", content);

                if (!resp.IsSuccessStatusCode)
                {
                    ShowAuthError("⚠️ Неверный логин или пароль!");
                    return;
                }

                string respBody = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                string user = root.TryGetProperty("user", out var u) ? u.GetString() ?? username : username;
                string token = root.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";

                if (chkRememberSession.IsChecked == true)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_authFilePath)!);
                        string saveJson = JsonSerializer.Serialize(new { user, token, savedAt = DateTime.UtcNow });
                        await File.WriteAllTextAsync(_authFilePath, saveJson);
                    }
                    catch { }
                }

                ApplyUserSession(user, token);
            }
            catch (Exception ex)
            {
                ShowAuthError("⚠️ Ошибка связи с сервером: " + ex.Message);
            }
            finally
            {
                btnAuthLogin.IsEnabled = true;
                btnAuthLogin.Content = "Войти в систему";
            }
        }

        private void TbAuthField_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                BtnAuthLogin_Click(sender, e);
            }
        }

        private void ApplyUserSession(string user, string token)
        {
            _currentUser = user;
            _authToken = token;
            _isUserAdmin = user.Equals("shonll", StringComparison.OrdinalIgnoreCase);

            lblLoggedInUser.Text = user;
            lblLoggedInRole.Text = _isUserAdmin ? "ADMIN" : "WORKER";
            badgeUserSession.Visibility = Visibility.Visible;

            if (_isUserAdmin)
            {
                cbOperators.IsEnabled = true;
                borderNonAdminNotice.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Не-админ: строго привязываем к его авторизованному имени
                borderNonAdminNotice.Visibility = Visibility.Visible;
                txtNonAdminNotice.Text = $"🔒 Профиль заблокирован: {user}";
                cbOperators.IsEnabled = false;

                bool found = false;
                for (int i = 0; i < cbOperators.Items.Count; i++)
                {
                    if (cbOperators.Items[i] is ComboBoxItem cbi && cbi.Tag?.ToString()?.Equals(user, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        cbOperators.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    var newItem = new ComboBoxItem
                    {
                        Content = $"👤 {user} (Ваш профиль)",
                        Tag = user,
                        IsSelected = true
                    };
                    cbOperators.Items.Insert(0, newItem);
                    cbOperators.SelectedIndex = 0;
                }

                if (tbCustomOperator != null) tbCustomOperator.Visibility = Visibility.Collapsed;
            }

            gridAuthLogin.Visibility = Visibility.Collapsed;
            gridMainBuilder.Visibility = Visibility.Visible;

            Log($"✅ Авторизован: {user} [{(_isUserAdmin ? "Главный Администратор" : "Оператор / Воркер")}]");
            Log($"• Профиль: {(_isUserAdmin ? "Любой (свободный выбор)" : $"Закреплён за {user}")}");
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_authFilePath)) File.Delete(_authFilePath);
            }
            catch { }

            _currentUser = "";
            _authToken = "";
            _isUserAdmin = false;

            pbAuthPassword.Password = "";
            borderAuthError.Visibility = Visibility.Collapsed;
            ShowLoginScreen();

            Log("🚪 Выполнен выход из аккаунта. Билдер заблокирован.");
        }

        private string? FindIsccPath()
        {
            string[] possiblePaths = new[]
            {
                @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"C:\Program Files\Inno Setup 6\ISCC.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Inno Setup 6\ISCC.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"NEXUS_Builder\inno\ISCC.exe"),
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

        private async Task<string?> EnsureInnoSetup()
        {
            string? iscc = FindIsccPath();
            if (!string.IsNullOrEmpty(iscc) && File.Exists(iscc)) return iscc;

            string portableDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "inno"
            );
            string portableIscc = Path.Combine(portableDir, "ISCC.exe");
            if (File.Exists(portableIscc)) return portableIscc;

            Log("⚙️ Inno Setup не обнаружен в системе. Автоматическая загрузка портативного компилятора...");
            Directory.CreateDirectory(portableDir);

            string zipPath = Path.Combine(portableDir, "inno_portable.zip");
            string downloadUrl = $"{API_BASE}/downloads/templates/inno_portable.zip";

            try
            {
                using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    Log($"❌ Не удалось загрузить Inno Setup с сервера (HTTP {resp.StatusCode})");
                    return null;
                }

                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await resp.Content.CopyToAsync(fs);
                }

                Log("📦 Распаковка портативного компилятора Inno Setup...");
                ZipFile.ExtractToDirectory(zipPath, portableDir, true);
                try { File.Delete(zipPath); } catch { }

                if (File.Exists(portableIscc))
                {
                    Log("✅ Портативный Inno Setup успешно настроен!");
                    return portableIscc;
                }
            }
            catch (Exception ex)
            {
                Log("❌ Ошибка загрузки Inno Setup: " + ex.Message);
            }

            return null;
        }

        private async Task<string> EnsureAppTemplate()
        {
            string templatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", "app_template"
            );

            // Проверяем, есть ли уже распакованный шаблон со всеми DLL
            if (Directory.Exists(templatesDir) && 
                File.Exists(Path.Combine(templatesDir, "RAH PRO.dll")) && 
                File.Exists(Path.Combine(templatesDir, "RAH Non Pro.dll")))
            {
                return templatesDir;
            }

            // Проверяем кэш старых версий на ПК разработчика
            string oldClientCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "client_multifile");
            string oldStandaloneCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "standalone_multifile");

            if (Directory.Exists(oldClientCache) && File.Exists(Path.Combine(oldClientCache, "RAH Non Pro.dll")) &&
                Directory.Exists(oldStandaloneCache) && File.Exists(Path.Combine(oldStandaloneCache, "RAH PRO.dll")))
            {
                Directory.CreateDirectory(templatesDir);
                CopyDirectory(oldClientCache, templatesDir);
                foreach (var f in Directory.GetFiles(oldStandaloneCache, "RAH PRO.*"))
                {
                    File.Copy(f, Path.Combine(templatesDir, Path.GetFileName(f)), true);
                }
                if (File.Exists(Path.Combine(templatesDir, "RAH PRO.dll")))
                {
                    return templatesDir;
                }
            }

            // Загрузка с сервера для пользователей
            Log("📥 Шаблоны приложения отсутствуют на этом ПК. Загрузка с сервера...");
            Directory.CreateDirectory(Path.GetDirectoryName(templatesDir)!);

            string zipPath = Path.Combine(Path.GetDirectoryName(templatesDir)!, "app_template.zip");
            string downloadUrl = $"{API_BASE}/downloads/templates/app_template.zip";

            try
            {
                using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    Log($"❌ Ошибка скачивания шаблона: сервер вернул код {resp.StatusCode}");
                    return "";
                }

                long totalBytes = resp.Content.Headers.ContentLength ?? 72000000;
                using var contentStream = await resp.Content.ReadAsStreamAsync();
                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;
                    DateTime lastLog = DateTime.Now;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if ((DateTime.Now - lastLog).TotalMilliseconds > 400)
                        {
                            int percent = (int)((totalRead * 100) / totalBytes);
                            if (percent > 100) percent = 100;
                            Dispatcher.Invoke(() =>
                            {
                                pbProgress.Value = percent;
                                lblStatus.Text = $"• Загрузка шаблона: {percent}% ({totalRead / (1024 * 1024)} МБ / {totalBytes / (1024 * 1024)} МБ)...";
                            });
                            lastLog = DateTime.Now;
                        }
                    }
                }

                Log("📦 Распаковка шаблона приложения в локальный кэш...");
                Dispatcher.Invoke(() => { pbProgress.IsIndeterminate = true; lblStatus.Text = "• Распаковка шаблона..."; });

                if (Directory.Exists(templatesDir)) Directory.Delete(templatesDir, true);
                Directory.CreateDirectory(templatesDir);
                ZipFile.ExtractToDirectory(zipPath, templatesDir, true);

                try { File.Delete(zipPath); } catch { }

                Log("✅ Шаблон приложения успешно загружен и готов!");
                return templatesDir;
            }
            catch (Exception ex)
            {
                Log("❌ Ошибка загрузки шаблона: " + ex.Message);
                return "";
            }
        }

        private void InitializeDefaultIcon()
        {
            SelectPresetIcon("thunder");
        }

        private string GetPresetIconPath(string tag)
        {
            string fileName = tag switch
            {
                "fire" => "fire.ico",
                "singer" => "singer.ico",
                "svyaz" => "svyaz.ico",
                "cyber" => "cyber.ico",
                _ => "thunder.ico"
            };

            string appDataIcons = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "icons"
            );
            string iconPath = Path.Combine(appDataIcons, fileName);
            if (File.Exists(iconPath)) return iconPath;

            string fallback = Path.Combine(appDataIcons, "app.ico");
            if (File.Exists(fallback)) return fallback;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFallback = Path.Combine(baseDir, fileName);
            if (File.Exists(localFallback)) return localFallback;

            return iconPath;
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
                Log($"🎨 Выбрана иконка: {ofd.FileName}");
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

        private string GetSelectedOperator()
        {
            if (!_isUserAdmin && !string.IsNullOrWhiteSpace(_currentUser))
            {
                return _currentUser;
            }

            if (cbOperators.SelectedItem is ComboBoxItem item)
            {
                string tag = item.Tag?.ToString() ?? "";
                if (tag == "__custom__")
                {
                    string custom = tbCustomOperator.Text.Trim();
                    return string.IsNullOrWhiteSpace(custom) ? (_currentUser.Length > 0 ? _currentUser : "HuilaEbanaya") : custom;
                }
                return tag;
            }
            return string.IsNullOrWhiteSpace(_currentUser) ? "HuilaEbanaya" : _currentUser;
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
                Log($"📁 Папка назначения: {dialog.SelectedPath}");
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

        private async void BtnBuildStandaloneInstaller_Click(object sender, RoutedEventArgs e)
        {
            await RunBuild(isStandalone: true);
        }

        private async void BtnBuildClientInstaller_Click(object sender, RoutedEventArgs e)
        {
            await RunBuild(isStandalone: false);
        }

        private async Task RunBuild(bool isStandalone)
        {
            if (string.IsNullOrWhiteSpace(_currentUser))
            {
                Log("❌ Ошибка: Сборка заблокирована! Требуется авторизация в аккаунте.");
                ShowLoginScreen();
                return;
            }

            string opName = GetSelectedOperator();
            string appName = tbAppName.Text.Trim();
            if (string.IsNullOrWhiteSpace(appName)) appName = "RAH";
            string tgChannel = tbTelegramChannel?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(tgChannel)) tgChannel = "https://t.me/robloxvzlomez";
            else if (!tgChannel.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !tgChannel.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                tgChannel = "https://t.me/" + tgChannel.TrimStart('@');
            }

            string outDir = tbOutputPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(outDir))
            {
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.0");
            }

            string buildTypeTitle = isStandalone ? "Standalone Инсталлятор (PRO)" : "Client Инсталлятор";
            string targetOutputName = isStandalone ? $"NEXUS_Standalone_Setup_{opName}.exe" : $"NEXUS_Client_Setup_{opName}.exe";
            string outputFullPath = Path.Combine(outDir, targetOutputName);

            btnBuildStandaloneInstaller.IsEnabled = false;
            btnBuildClientInstaller.IsEnabled = false;
            btnBrowse.IsEnabled = false;
            btnOpenFolder.Visibility = Visibility.Collapsed;
            pbProgress.IsIndeterminate = true;
            lblStatus.Text = $"• Сборка {buildTypeTitle}...";

            Log("═════════════════════════════════════════════════════════════════");
            Log($"🚀 СТАРТ СБОРКИ: {buildTypeTitle}");
            Log($"👤 Целевой профиль оператора: {opName}");
            Log($"🏷️ Имя приложения: {appName}");
            Log($"📢 Telegram канал: {tgChannel}");
            Log($"🎨 Иконка: {Path.GetFileName(_activeIconPath)}");
            Log($"💾 Путь назначения: {outputFullPath}");

            bool success = false;

            await Task.Run(async () =>
            {
                try
                {
                    // 1. Получение шаблона приложения (из локального кэша или автоматическая загрузка)
                    string templateDir = await EnsureAppTemplate();
                    if (string.IsNullOrEmpty(templateDir) || !Directory.Exists(templateDir))
                    {
                        Log("❌ ОШИБКА: Не удалось получить шаблон приложения!");
                        return;
                    }

                    string stagingDir = Path.Combine(Path.GetTempPath(), $"NEXUS_Stage_{Guid.NewGuid():N}");
                    if (Directory.Exists(stagingDir))
                    {
                        try { Directory.Delete(stagingDir, true); } catch { }
                    }
                    Directory.CreateDirectory(stagingDir);

                    Log("📂 Подготовка файлов приложения...");
                    CopyDirectory(templateDir, stagingDir);

                    // Очистка лишних файлов режима
                    if (isStandalone)
                    {
                        foreach (var f in Directory.GetFiles(stagingDir, "RAH Non Pro.*"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    else
                    {
                        foreach (var f in Directory.GetFiles(stagingDir, "RAH PRO.*"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }

                    // 2. Внедрение параметров оператора в целевой управляемый DLL
                    string targetDllName = isStandalone ? "RAH PRO.dll" : "RAH Non Pro.dll";
                    string targetDllPath = Path.Combine(stagingDir, targetDllName);

                    if (!File.Exists(targetDllPath))
                    {
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
                        Log("❌ ОШИБКА: Библиотека приложения не найдена!");
                        return;
                    }

                    Log($"💉 Внедрение параметров оператора в {Path.GetFileName(targetDllPath)}...");
                    bool patchOk = InjectConfigIntoFile(targetDllPath, opName, appName, tgChannel, isStandalone);
                    if (!patchOk)
                    {
                        Log("❌ ОШИБКА внедрения параметров оператора!");
                        return;
                    }

                    // 3. Внедрение иконки в исполняемый файл
                    string targetExeName = isStandalone ? "RAH PRO.exe" : "RAH Non Pro.exe";
                    string targetExePath = Path.Combine(stagingDir, targetExeName);

                    if (File.Exists(_activeIconPath))
                    {
                        Log($"🎨 Внедрение иконки в {Path.GetFileName(targetExePath)}...");
                        string destIco = Path.Combine(stagingDir, "app.ico");
                        try { File.Copy(_activeIconPath, destIco, true); } catch { }

                        if (File.Exists(targetExePath))
                        {
                            bool iconInjected = IconInjector.InjectIcon(targetExePath, _activeIconPath);
                            if (iconInjected) Log("   ✅ Иконка успешно встроена в ресурсы PE .exe файла!");
                        }
                    }

                    // 4. Сборка инсталлятора через Inno Setup
                    Log("🛠️ Сборка Setup Инсталлятора через Inno Setup 6...");
                    bool instOk = await CompileInnoSetup(
                        sourceDirectory: stagingDir,
                        outputDir: outDir,
                        outputBaseFilename: isStandalone ? $"NEXUS_Standalone_Setup_{opName}" : $"NEXUS_Client_Setup_{opName}",
                        appName: appName,
                        appExeName: targetExeName,
                        appVersion: AppVersion,
                        opName: opName,
                        iconPath: _activeIconPath,
                        createDesktopShortcut: true,
                        compressLzma: true,
                        runAsAdmin: true
                    );

                    try { Directory.Delete(stagingDir, true); } catch { }

                    if (!instOk)
                    {
                        Log("❌ ОШИБКА сборки инсталлятора!");
                        return;
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
            btnBuildStandaloneInstaller.IsEnabled = true;
            btnBuildClientInstaller.IsEnabled = true;
            btnBrowse.IsEnabled = true;

            if (success)
            {
                btnOpenFolder.Visibility = Visibility.Visible;
                System.Windows.MessageBox.Show(
                    $"Инсталлятор для оператора {opName} успешно создан!\n\nРасположение:\n{outputFullPath}",
                    "NEXUS Builder v8.0.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        private bool InjectConfigIntoFile(string filePath, string opName, string appName, string tgChannel, bool isStandalone)
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
                clientVersion = AppVersion,
                version = AppVersion,
                telegramChannel = tgChannel,
                telegramUrl = tgChannel,
                tgChannel = tgChannel,
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
            string? iscc = await EnsureInnoSetup();
            if (string.IsNullOrEmpty(iscc) || !File.Exists(iscc))
            {
                Log("❌ Inno Setup (ISCC.exe) не найден и не удалось загрузить!");
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
DefaultDirName={{autopf}}\\{{#MyAppName}}
UninstallDisplayIcon={{app}}\\{{#MyAppExeName}}
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
Name: ""{{autoprograms}}\\{{#MyAppName}}""; Filename: ""{{app}}\\{{#MyAppExeName}}""; IconFilename: ""{{app}}\\app.ico""
{desktopIcon}

[Run]
Filename: ""{{app}}\\{{#MyAppExeName}}""; Description: ""{{cm:LaunchProgram,{{#StringChange(MyAppName, '&', '&&')}}}}""; Flags: nowait postinstall skipifsilent runascurrentuser
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