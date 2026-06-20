using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        private static readonly string AccountsFile;

        static MainWindow()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("image/webp,image/apng,image/*,*/*;q=0.8");

            AccountsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "accounts.txt");
        }

        private const string PlaceholderText = "Введите никнейм...";
        private DispatcherTimer? _debounceTimer;

        public MainWindow()
        {
            InitializeComponent();
            TxtUsername.Text = PlaceholderText;
            TxtUsername.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6F));
            LoadAccountsInfo();
        }

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

        private void LoadAccountsInfo()
        {
            try
            {
                if (!File.Exists(AccountsFile))
                    File.WriteAllText(AccountsFile, "");

                if (File.Exists(AccountsFile))
                {
                    int count = File.ReadLines(AccountsFile).Count(l => l.Contains(':'));
                }
            }
            catch { }
        }

        private string? FindPassword(string username)
        {
            try
            {
                if (!File.Exists(AccountsFile)) return null;
                foreach (string line in File.ReadLines(AccountsFile))
                {
                    int idx = line.IndexOf(':');
                    if (idx > 0 && line[..idx].Trim().Equals(username, StringComparison.OrdinalIgnoreCase))
                        return line[(idx + 1)..].Trim();
                }
            }
            catch { }
            return null;
        }

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
            }
            else
            {
                TxtPlaceholder.Text = "?";
                AvatarBrush.ImageSource = null;
                TxtPlaceholder.Opacity = 0.3;
                AppendConsole("[roblox]", "#2A2D3A", " ✗ Профиль не найден", "#FF4757");
            }
        }

        private async Task<BitmapImage?> DownloadRobloxAvatarAsync(string username)
        {
            try
            {
                var payload = new { usernames = new[] { username }, excludeBannedUsers = false };
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                using var reqContent = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("https://users.roblox.com/v1/usernames/users", reqContent);
                if (!response.IsSuccessStatusCode) return null;

                var resStr = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(resStr);
                var data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0) return null;

                long userId = data[0].GetProperty("id").GetInt64();

                var thumbUrl = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
                var thumbResponse = await _http.GetAsync(thumbUrl);
                if (!thumbResponse.IsSuccessStatusCode) return null;

                var thumbStr = await thumbResponse.Content.ReadAsStringAsync();
                using var thumbDoc = System.Text.Json.JsonDocument.Parse(thumbStr);
                var thumbData = thumbDoc.RootElement.GetProperty("data");
                if (thumbData.GetArrayLength() == 0) return null;

                var state = thumbData[0].GetProperty("state").GetString();
                var imageUrl = thumbData[0].GetProperty("imageUrl").GetString();

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

                var imageBytes = await _http.GetByteArrayAsync(imageUrl);

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
            catch
            {
                return null;
            }
        }

        private void SetStatusBadge(string text, string dotColor)
        {
            TbStatusLabel.Text = text;
            StatusDot.Fill = BrushFromHex(dotColor);
        }

        private async void BtnHack_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();

            if (string.IsNullOrEmpty(username) || username == PlaceholderText)
            {
                AppendConsole("[error]", "#FF4757", " Введите никнейм Roblox!", "#FF4757");
                SetStatusBadge("ОШИБКА", "#FF4757");
                return;
            }

            string? storedPassword = FindPassword(username);
            if (storedPassword == null)
            {
                AppendConsole("[error]", "#FF4757", $" Аккаунт {username} не найден в accounts.txt", "#FF4757");
                SetStatusBadge("НЕ НАЙДЕН", "#FF4757");
                return;
            }

            BtnHack.IsEnabled = false;
            TxtUsername.IsEnabled = false;
            PanelResult.Visibility = Visibility.Collapsed;
            HackProgress.Visibility = Visibility.Visible;
            HackProgress.Value = 0;
            SetStatusBadge("ПРОЦЕСС ВЗЛОМА", "#FFA502");

            var rand = new Random();
            int totalSeconds = rand.Next(8, 14);

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
                "Генерация расшифрованного ключа..."
            };

            int elapsed = 0;
            int stepIndex = 0;

            while (elapsed < totalSeconds)
            {
                int nextDelay = rand.Next(2, 4);
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
            AppendConsole("[done]", "#2ED573", " Расшифровка завершена!", "#2ED573");

            SetStatusBadge("ВЗЛОМ УСПЕШЕН", "#2ED573");
            TxtPassword.Text = storedPassword;
            PanelResult.Visibility = Visibility.Visible;
            AppendConsole("[result]", "#2ED573", $" Пароль: {storedPassword}", "#2ED573");

            BtnHack.IsEnabled = true;
            TxtUsername.IsEnabled = true;
            HackProgress.Visibility = Visibility.Collapsed;
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
            catch { }
        }
    }
}
