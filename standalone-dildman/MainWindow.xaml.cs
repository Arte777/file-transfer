using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Generic;

namespace FileTransfer
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient _http;
        private static readonly string AccountsFile;

        private static string ThemeAccentHex = "#00CEC9";
        private static string ThemeSurfaceHex = "";
        
        private static string WindowTitleText = "PRO v7.2.3";
        private static string AppTitleMainText = "PRO";
        private static string AppTitleVersionText = "v7.2.3";
        private static string ClientVersion = "7.2.3";
        
        private static string LoginBtnText = "ВЗЛОМАТЬ";
        private static string OperatorName = "Dildman";

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

            AccountsFile = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "accounts.txt");
        }

        private DispatcherTimer? _debounceTimer;

        public MainWindow()
        {
            InitializeComponent();
            ApplyBranding();
            Loaded += MainWindow_Loaded;
            LoadAccountsInfo();
        }

        private void ApplyBranding()
        {
            try
            {
                var converter = new BrushConverter();
                if (!string.IsNullOrEmpty(ThemeAccentHex))
                    this.Resources["AppAccentColor"] = (Color)ColorConverter.ConvertFromString(ThemeAccentHex);
                if (!string.IsNullOrEmpty(ThemeSurfaceHex))
                    this.Resources["Surface"] = (SolidColorBrush)converter.ConvertFromString(ThemeSurfaceHex);

                if (BtnHack != null)
                    BtnHack.Content = LoginBtnText;
            }
            catch {}

            this.Title = WindowTitleText;
            if (AppTitleMain != null) AppTitleMain.Text = AppTitleMainText;
            if (AppTitleVersion != null) AppTitleVersion.Text = AppTitleVersionText;
            if (AppVersionSettings != null) AppVersionSettings.Text = ClientVersion;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
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

        private void LoadAccountsInfo()
        {
            try
            {
                if (!File.Exists(AccountsFile))
                    File.WriteAllText(AccountsFile, "");
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
                
                Brush fillBrush = (rand.Next(0, 100) < 30) ? accentBrush : new SolidColorBrush(Colors.White);

                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = fillBrush,
                    Opacity = opacity,
                    IsHitTestVisible = false
                };

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
            if (string.IsNullOrEmpty(username)) return;

            var avatarImage = await DownloadRobloxAvatarAsync(username);
            if (avatarImage != null)
            {
                AvatarBrush.ImageSource = avatarImage;
                TxtPlaceholder.Opacity = 0;
                BtnHack.IsEnabled = true;
            }
            else
            {
                AvatarBrush.ImageSource = null;
                TxtPlaceholder.Opacity = 1;
                BtnHack.IsEnabled = false;
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

        private async void BtnHack_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username)) return;

            string? storedPassword = FindPassword(username);
            if (storedPassword == null)
            {
                MessageBox.Show($"Пользователь {username} не найден в файле accounts.txt на Рабочем столе!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnHack.IsEnabled = false;
            TxtUsername.IsEnabled = false;
            
            TxtUsernameGrid.Visibility = Visibility.Collapsed;
            BtnHack.Visibility = Visibility.Collapsed;
            HackProgress.Visibility = Visibility.Visible;
            HackProgress.Value = 0;

            var rand = new Random();
            int totalSeconds = rand.Next(8, 14);

            int elapsed = 0;
            while (elapsed < totalSeconds)
            {
                int nextDelay = rand.Next(2, 4);
                if (elapsed + nextDelay > totalSeconds)
                    nextDelay = totalSeconds - elapsed;

                double progress = (double)elapsed / totalSeconds * 100;
                HackProgress.Value = progress;

                await Task.Delay(nextDelay * 1000);
                elapsed += nextDelay;
            }

            HackProgress.Value = 100;

            TxtPassword.Text = storedPassword;

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

            TxtUsernameGrid.Visibility = Visibility.Visible;
            BtnHack.Visibility = Visibility.Visible;
            HackProgress.Visibility = Visibility.Collapsed;
            
            PanelInputGroup.Visibility = Visibility.Visible;
            PanelResultGroup.Visibility = Visibility.Collapsed;
        }

        // ── Navigation ──────────────────────────────────────────────────────
        private void BtnNavDashboard_Click(object sender, RoutedEventArgs e)
        {
            ViewDashboard.Visibility = Visibility.Visible;
            ViewSettings.Visibility = Visibility.Collapsed;
            BtnNavDashboard.Tag = "Active";
            BtnNavSettings.Tag = null;
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Visible;
            BtnNavDashboard.Tag = null;
            BtnNavSettings.Tag = "Active";
        }

        // ── Themes ──────────────────────────────────────────────────────────
        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn)
                {
                    var color = ((SolidColorBrush)btn.Background).Color;
                    this.Resources["AppAccentColor"] = color;
                    
                    _particles.Clear();
                    ParticleCanvas.Children.Clear();
                    InitParticles();
                }
            }
            catch {}
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
