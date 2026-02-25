using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace MizuLauncher
{
    public partial class MainWindow : Window
    {
        // 绝对生效的手搓日志方法 
        private static void WriteLog(string message)
        {
            try
            {
                // 日志会直接生成在你程序的运行目录下 
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mizu_debug.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
            }
            catch { }
        }

        private MinecraftLauncher? _launcher;
        private MinecraftPath? _baseMcPath;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private const string ConfigFileName = "launcher_config.json";
        private const int MaxQuickLaunchItems = 9; // 3x3 grid

        public ObservableCollection<string> QuickLaunchVersions { get; set; } = new();
        public ObservableCollection<PlayerInfo> OnlinePlayers { get; set; } = new();
        public ObservableCollection<PlayerInfo> OfflinePlayers { get; set; } = new();

        public MainWindow()
        {
            // 全局崩溃拦截写入日志 
            Application.Current.DispatcherUnhandledException += (s, e) =>
            {
                WriteLog($"【致命崩溃】 {e.Exception}");
                MessageBox.Show($"软件崩溃了！请查看目录下的 mizu_debug.txt\n{e.Exception.Message}");
                e.Handled = true;
            };

            try
            {
                WriteLog("=== 软件启动 ===");
                InitializeComponent();

                // 【核心改动】挂载系统底层句柄初始化事件，用于开启 Mica
                this.SourceInitialized += MainWindow_SourceInitialized;

                QuickLaunchVersions = new ObservableCollection<string>();
                QuickLaunchItems.ItemsSource = QuickLaunchVersions;

                OnlinePlayers = new ObservableCollection<PlayerInfo>();
                ListOnlinePlayers.ItemsSource = OnlinePlayers;

                OfflinePlayers = new ObservableCollection<PlayerInfo>();
                ListOfflinePlayers.ItemsSource = OfflinePlayers;

                string mcDirPath = @"C:\Users\Mizusumi\Personal\play\mc\.minecraft";
                _baseMcPath = new MinecraftPath(mcDirPath);
                _launcher = new MinecraftLauncher(_baseMcPath);

                this.Loaded += MainWindow_Loaded;
                QuickLaunchVersions.CollectionChanged += (s, e) => SaveConfig();

                // 移除构造函数中的直接调用，改到 Loaded 事件中统一处理
                // _ = UpdatePlayerUIFromState();
                // 以前的抓屏更新事件 (LocationChanged, SizeChanged) 已经全部被扬了！
            }
            catch (Exception ex)
            {
                WriteLog($"初始化失败: {ex.Message}");
                MessageBox.Show($"初始化失败: {ex.Message}");
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadConfig();
                RefreshVersionList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Loaded error: {ex.Message}");
            }
        }

        #region Windows 11 Mica & DWM API (真正的毛玻璃)

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            EnableMicaBackdrop(hwnd, _currentBgType);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private void EnableMicaBackdrop(IntPtr hwnd, int type = 2)
        {
            try
            {
                // 1. 强制窗口使用深色模式
                int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                int useDarkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, Marshal.SizeOf(typeof(int)));

                // 2. 启用材质
                // DWMSBT_MAINWINDOW = 2 (Mica)
                // DWMSBT_TRANSIENTWINDOW = 3 (Acrylic)
                // DWMSBT_NONE = 1 (Solid)
                int DWMWA_SYSTEMBACKDROP_TYPE = 38;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, Marshal.SizeOf(typeof(int)));

                // 3. 启用系统原生圆角
                int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                int DWMWCP_ROUND = 2;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, Marshal.SizeOf(typeof(int)));
            }
            catch (Exception ex)
            {
                WriteLog($"DWM设置失败: {ex.Message}");
            }
        }

        #endregion

        #region Navigation and Background Settings

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Visible;
            MoreContent.Visibility = Visibility.Collapsed;
        }

        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            HomeContent.Visibility = Visibility.Collapsed;
            MoreContent.Visibility = Visibility.Visible;
        }

        private void RadioBg_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                SolidColorSettings.Visibility = Visibility.Collapsed;
                MainRoot.Background = System.Windows.Media.Brushes.Transparent;
                BgTint.Visibility = Visibility.Visible;

                if (rb == RadioMica)
                {
                    _currentBgType = 2;
                    EnableMicaBackdrop(hwnd, 2); // Mica
                    BgTint.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x00, 0x00, 0x00));
                }
                else if (rb == RadioAcrylic)
                {
                    _currentBgType = 3;
                    EnableMicaBackdrop(hwnd, 3); // Acrylic
                    // 亚克力颜色进一步加深 (调整为 #121212，且不透明度增加)
                    BgTint.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0x12, 0x12, 0x12));
                }
                else if (rb == RadioSolid)
                {
                    _currentBgType = 1;
                    EnableMicaBackdrop(hwnd, 1); // None
                    SolidColorSettings.Visibility = Visibility.Visible;
                    BgTint.Visibility = Visibility.Collapsed; // 纯色模式下隐藏蒙层 
                    TxtCustomColor.Text = _currentCustomColor;
                    // 使用当前记录的自定义颜色或默认色
                    try
                    {
                        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_currentCustomColor);
                        MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                    }
                    catch { }
                }
                SaveConfig();
            }
        }

        private void ColorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorStr)
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                    MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                    _currentCustomColor = colorStr;
                    TxtCustomColor.Text = colorStr;
                    SaveConfig();
                }
                catch { }
            }
        }

        private void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = TxtCustomColor.Text;
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(input);
                MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                _currentCustomColor = input;
                SaveConfig();
            }
            catch
            {
                MessageBox.Show("无效的颜色代码");
            }
        }

        #endregion

        private void RefreshVersionList()
        {
            try
            {
                if (_baseMcPath == null) return;
                ListVersions.Items.Clear();
                string versionsDir = _baseMcPath.Versions;
                if (Directory.Exists(versionsDir))
                {
                    string[] localVersionDirs = Directory.GetDirectories(versionsDir);
                    foreach (string dir in localVersionDirs)
                    {
                        string versionName = Path.GetFileName(dir);
                        ListVersions.Items.Add(versionName);
                    }
                }

                if (ListVersions.Items.Count > 0)
                {
                    ListVersions.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("扫描本地版本失败: " + ex.Message);
            }
        }

        #region Configuration Storage (Safe)

        private int _currentBgType = 3; // Default Acrylic
        private string _currentCustomColor = "#FF1E1E1E";
        private string _currentPlayerName = "添加玩家";
        private bool _showDragHint = true;

        private void SaveConfig()
        {
            try
            {
                var config = new LauncherConfig
                {
                    QuickLaunchVersions = QuickLaunchVersions.ToList(),
                    BackgroundType = _currentBgType,
                    CustomColor = _currentCustomColor,
                    PlayerName = _currentPlayerName,
                    Players = OnlinePlayers.Concat(OfflinePlayers).ToList(),
                    ShowDragHint = _showDragHint
                };
                string json = JsonSerializer.Serialize(config);
                File.WriteAllText(ConfigFileName, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save config error: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    string json = File.ReadAllText(ConfigFileName);
                    var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                    if (config != null)
                    {
                        if (config.QuickLaunchVersions != null)
                        {
                            QuickLaunchVersions.Clear();
                            foreach (var v in config.QuickLaunchVersions)
                            {
                                if (QuickLaunchVersions.Count < MaxQuickLaunchItems)
                                    QuickLaunchVersions.Add(v);
                            }
                        }
                        _currentBgType = config.BackgroundType;
                        _currentCustomColor = config.CustomColor ?? "#FF1E1E1E";
                        _currentPlayerName = config.PlayerName ?? "添加玩家";
                        _showDragHint = config.ShowDragHint;

                        if (config.Players != null)
                        {
                            OnlinePlayers.Clear();
                            OfflinePlayers.Clear();
                            foreach (var p in config.Players)
                            {
                                if (p.IsOnline) OnlinePlayers.Add(p);
                                else OfflinePlayers.Add(p);
                                _ = LoadPlayerAvatarAsync(p);
                            }
                        }
                        
                        // Apply to UI state
                        UpdateBackgroundUIFromState();
                        _ = UpdatePlayerUIFromState();
                    }
                }
                else
                {
                    // No config file, ensure defaults are applied
                    _showDragHint = true;
                    UpdateBackgroundUIFromState();
                    _ = UpdatePlayerUIFromState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load config error: {ex.Message}");
            }
        }

        private async Task LoadPlayerAvatarAsync(PlayerInfo player)
        {
            player.Avatar = await LittleSkinFetcher.GetAvatarAsync(player.Name);
        }

        private void UpdateBackgroundUIFromState()
        {
            if (_currentBgType == 2) RadioMica.IsChecked = true;
            else if (_currentBgType == 3) RadioAcrylic.IsChecked = true;
            else if (_currentBgType == 1) RadioSolid.IsChecked = true;
            
            TxtCustomColor.Text = _currentCustomColor;
            CheckShowDragHint.IsChecked = _showDragHint;
            UpdateDragHintVisibility();

            // 确保在加载配置后也应用材质设置
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                EnableMicaBackdrop(hwnd, _currentBgType);
            }
            
            if (_currentBgType == 1)
            {
                SolidColorSettings.Visibility = Visibility.Visible;
                BgTint.Visibility = Visibility.Collapsed; // 纯色模式下隐藏蒙层 
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_currentCustomColor);
                    MainRoot.Background = new System.Windows.Media.SolidColorBrush(color);
                }
                catch { }
            }
            else
            {
                MainRoot.Background = System.Windows.Media.Brushes.Transparent;
                BgTint.Visibility = Visibility.Visible;
                
                if (_currentBgType == 3) // Acrylic
                {
                    // 亚克力颜色进一步加深 (调整为 #121212，且不透明度增加)
                    BgTint.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0x12, 0x12, 0x12));
                }
                else // Mica
                {
                    BgTint.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, 0x00, 0x00, 0x00));
                }
            }
        }

        private void CheckShowDragHint_Click(object sender, RoutedEventArgs e)
        {
            _showDragHint = CheckShowDragHint.IsChecked ?? true;
            UpdateDragHintVisibility();
            SaveConfig();
        }

        private void UpdateDragHintVisibility()
        {
            if (TxtDragHint == null) return;
            
            if (!_showDragHint)
            {
                TxtDragHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 如果启用了提示，则由 Style 中的 DataTrigger 决定是否显示（当磁贴数为0时显示）
                // 这里我们手动刷新一下，或者由于 Style 已经绑定了，我们可以通过设置一个本地值来覆盖或清除
                TxtDragHint.ClearValue(TextBlock.VisibilityProperty);
            }
        }

        private async Task UpdatePlayerUIFromState()
        {
            try
            {
                TxtPlayerName.Text = _currentPlayerName;
                var avatar = await LittleSkinFetcher.GetAvatarAsync(_currentPlayerName);
                if (avatar != null)
                {
                    ImgPlayerAvatar.Source = avatar;
                }
                
                // 如果是“添加玩家”，禁用启动按钮
                BtnLaunch.IsEnabled = _currentPlayerName != "添加玩家";
            }
            catch (Exception ex)
            {
                WriteLog($"更新玩家UI异常: {ex.Message}");
            }
        }

        private void DeletePlayer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    var border = contextMenu.PlacementTarget as Border;
                    if (border != null && border.DataContext is PlayerInfo player)
                    {
                        if (player.IsOnline) OnlinePlayers.Remove(player);
                        else OfflinePlayers.Remove(player);
                        
                        // 如果删掉的是当前玩家，重置为默认
                        if (_currentPlayerName == player.Name)
                        {
                            _currentPlayerName = "添加玩家";
                            _ = UpdatePlayerUIFromState();
                        }
                        SaveConfig();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Delete player error: " + ex.Message);
            }
        }

        private void BtnAddPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 关闭 Popup
            BtnPlayerCard.IsChecked = false;

            // 弹出输入框
            string input = Microsoft.VisualBasic.Interaction.InputBox("输入玩家离线 ID:", "添加新离线玩家", "");
            if (!string.IsNullOrEmpty(input))
            {
                AddOfflinePlayer(input);
            }
        }

        private async void AddOfflinePlayer(string input)
        {
            if (!OnlinePlayers.Any(p => p.Name == input) && !OfflinePlayers.Any(p => p.Name == input))
            {
                var newPlayer = new PlayerInfo { Name = input, IsOnline = false };
                OfflinePlayers.Add(newPlayer);
                _currentPlayerName = input;
                await UpdatePlayerUIFromState();
                SaveConfig();
                
                // 异步加载列表中的头像
                newPlayer.Avatar = await LittleSkinFetcher.GetAvatarAsync(input);
            }
            else
            {
                _currentPlayerName = input;
                await UpdatePlayerUIFromState();
                SaveConfig();
            }
        }

        private async void ListPlayers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem is PlayerInfo selected)
            {
                _currentPlayerName = selected.Name;
                await UpdatePlayerUIFromState();
                SaveConfig();
                BtnPlayerCard.IsChecked = false; // 关闭 Popup
                lb.SelectedItem = null; // 重置选择
            }
        }

        public class PlayerInfo : System.ComponentModel.INotifyPropertyChanged
        {
            private string _name = "";
            private System.Windows.Media.Imaging.BitmapImage? _avatar;
            private bool _isOnline = false;

            public string Name 
            { 
                get => _name; 
                set { _name = value; OnPropertyChanged(nameof(Name)); }
            }

            public bool IsOnline
            {
                get => _isOnline;
                set { _isOnline = value; OnPropertyChanged(nameof(IsOnline)); }
            }

            [System.Text.Json.Serialization.JsonIgnore]
            public System.Windows.Media.Imaging.BitmapImage? Avatar 
            { 
                get => _avatar; 
                set { _avatar = value; OnPropertyChanged(nameof(Avatar)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public class LauncherConfig
        {
            public System.Collections.Generic.List<string>? QuickLaunchVersions { get; set; }
            public int BackgroundType { get; set; } = 3; // Default Acrylic
            public string? CustomColor { get; set; } = "#FF1E1E1E";
            public string PlayerName { get; set; } = "添加玩家";
            public System.Collections.Generic.List<PlayerInfo>? Players { get; set; }
            public bool ShowDragHint { get; set; } = true;
        }

        #endregion

        #region Window Controls

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Drag and Drop Logic (Protected)

        private void ListVersions_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe &&
               (fe.GetType().Name.Contains("Scroll") ||
               (fe.TemplatedParent?.GetType().Name.Contains("Scroll") == true)))
            {
                return;
            }
            _dragStartPoint = e.GetPosition(null);
            WriteLog("鼠标左键按下，记录坐标。");
        }

        private void ListVersions_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBox listBox && listBox.SelectedItem != null)
                    {
                        string selectedVersion = listBox.SelectedItem.ToString() ?? "";
                        _isDragging = true;

                        WriteLog($"检测到移动，准备拖拽版本: {selectedVersion}");

                        if (e.OriginalSource is UIElement uiElement)
                        {
                            uiElement.ReleaseMouseCapture();
                        }
                        Mouse.Capture(null);

                        // 将系统级拖拽推迟到 UI 线程执行
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                WriteLog("--> 开始执行 DoDragDrop 系统方法");
                                DragDrop.DoDragDrop(listBox, selectedVersion, DragDropEffects.Copy);
                                WriteLog("--> DoDragDrop 执行完毕，用户松开了鼠标");
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"拖拽内部异常: {ex.Message}");
                            }
                            finally
                            {
                                _isDragging = false;
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
        }

        private void QuickLaunch_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void QuickLaunch_Drop(object sender, DragEventArgs e)
        {
            WriteLog("触发 Drop (放下) 事件！");
            try
            {
                if (QuickLaunchVersions.Count < MaxQuickLaunchItems && e.Data.GetDataPresent(DataFormats.StringFormat))
                {
                    string? version = e.Data.GetData(DataFormats.StringFormat) as string;
                    WriteLog($"正在尝试将 [{version}] 加入快速启动栏");

                    if (!string.IsNullOrEmpty(version) && !QuickLaunchVersions.Contains(version))
                    {
                        QuickLaunchVersions.Add(version);
                        WriteLog("加入成功！");
                    }
                    else
                    {
                        WriteLog("加入失败：版本为空，或该卡片已存在。");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Drop 崩溃: {ex.Message}");
            }
        }

        // 暂时注销单向拖拽事件
        private void QuickLaunchCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
        private void QuickLaunchCard_MouseMove(object sender, MouseEventArgs e) { }
        private void ListVersions_DragOver(object sender, DragEventArgs e) { }
        private void ListVersions_Drop(object sender, DragEventArgs e) { }

        #endregion

        #region Drag and Drop Logic (Protected)

        public void DeleteQuickLaunchCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                {
                    FrameworkElement? target = contextMenu.PlacementTarget as FrameworkElement;
                    Button? button = target as Button;

                    if (button == null && target != null)
                    {
                        button = target.TemplatedParent as Button;
                    }

                    if (button != null && button.Content != null)
                    {
                        string version = button.Content.ToString() ?? "";
                        QuickLaunchVersions.Remove(version);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Delete card error: " + ex.Message);
            }
        }

        #endregion

        #region Launch Logic (Protected)

        private async void LaunchGame(string versionName)
        {
            if (string.IsNullOrEmpty(versionName) || _baseMcPath == null) return;

            try
            {
                BtnLaunch.IsEnabled = false;
                TxtProgressStep.Text = "正在准备...";
                RectProgress.Width = 0;

                var isolatedPath = new MinecraftPath(Path.Combine(_baseMcPath.BasePath, "versions", versionName));
                isolatedPath.Assets = _baseMcPath.Assets;
                isolatedPath.Library = _baseMcPath.Library;
                isolatedPath.Runtime = _baseMcPath.Runtime;
                isolatedPath.Versions = _baseMcPath.Versions;

                var currentLauncher = new MinecraftLauncher(isolatedPath);

                currentLauncher.FileProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            TxtProgressStep.Text = $"{e.Name} ({e.ProgressedTasks}/{e.TotalTasks})";
                            if (e.TotalTasks > 0)
                                RectProgress.Width = (double)e.ProgressedTasks / e.TotalTasks * BorderProgress.ActualWidth;
                        }
                        catch { }
                    });
                };

                currentLauncher.ByteProgressChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (e.TotalBytes > 0)
                                RectProgress.Width = (double)e.ProgressedBytes / e.TotalBytes * BorderProgress.ActualWidth;
                        }
                        catch { }
                    });
                };

                var launchOption = new MLaunchOption
                {
                    Path = isolatedPath,
                    MaximumRamMb = 4096,
                    Session = MSession.CreateOfflineSession(_currentPlayerName)
                };

                TxtProgressStep.Text = "正在校验资源...";
                var process = await currentLauncher.InstallAndBuildProcessAsync(versionName, launchOption);

                TxtProgressStep.Text = "游戏已启动！";
                RectProgress.Width = BorderProgress.ActualWidth;
                process.Start();
            }
            catch (Exception ex)
            {
                TxtProgressStep.Text = "启动失败";
                MessageBox.Show("启动失败: " + ex.Message, "错误");
            }
            finally
            {
                BtnLaunch.IsEnabled = true;
            }
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPlayerName == "添加玩家")
                {
                    MessageBox.Show("请先点击左下角添加玩家账号。");
                    return;
                }

                if (ListVersions.SelectedItem != null)
                {
                    LaunchGame(ListVersions.SelectedItem.ToString() ?? "");
                }
                else
                {
                    MessageBox.Show("请先从列表中选择一个版本。");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Launch click error: " + ex.Message);
            }
        }

        private void QuickLaunchCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn && btn.Content != null)
                {
                    LaunchGame(btn.Content.ToString() ?? "");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Quick launch error: " + ex.Message);
            }
        }

        #endregion
    }
}