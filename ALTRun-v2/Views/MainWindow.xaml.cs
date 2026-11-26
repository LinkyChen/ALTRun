using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ALTRun.Models;
using ALTRun.Services;
using ContextMenu = System.Windows.Controls.ContextMenu;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;

namespace ALTRun.Views
{
    public partial class MainWindow : Window
    {
        private GlobalHotKey? _globalHotKey;
        private TrayIconService? _trayIconService;
        private SearchResult _currentResult = new();

        // 候选列表的“逻辑选中索引”。ListBox.SelectedIndex 会在窗口 Hide/Show、ItemsSource
        // 重建或布局刷新时被 WPF 重置为 -1（尤其“运行一次后再次呼出”这条路径），
        // 因此滚轮/上下键统一以本字段为唯一真相源，避免被视觉选中态干扰。
        private int _selectedResultIndex = -1;

        // 搜索防抖：30ms 内的连续输入合并为一次搜索，避免每个字符都全量遍历并重建列表
        private readonly System.Windows.Threading.DispatcherTimer _searchDebounceTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        private bool _isSystemIntegrationInitialized = false;

        // 文件未找到提示窗：非模态、位于主界面上层，且打开期间不触发“失焦隐藏”
        private Window? _missingFileTip;
        private bool _suppressHideOnDeactivate;

        // 隐藏窗口时会清空搜索框，这属于内部清场而不是用户输入，不应再触发一次搜索
        private bool _suspendSearch;

        public MainWindow()
        {
            InitializeComponent();
            _searchDebounceTimer.Tick += (_, _) => PerformSearch();
            ApplySavedWindowSize();
            ApplyTheme(ThemeManager.GetTheme(App.Config.Settings.Theme, App.Config.Settings.DarkMode));
            SourceInitialized += (s, e) => InitializeSystemIntegration();
            Activated += (s, e) =>
            {
                // 提示窗打开时，即使主界面被再次激活，也把提示窗保持在最上层
                if (_missingFileTip != null && _missingFileTip.IsLoaded)
                {
                    _missingFileTip.Activate();
                }
            };
            Loaded += MainWindow_Loaded;
            Closing += (s, e) =>
            {
                SaveWindowSize();
                e.Cancel = true;
                HideWindow();
            };

            // 托盘常驻：窗口完全隐藏满 10 秒后裁剪工作集，把闲置页交还系统
            MemoryTrimmer.Start(this);
        }

        private void ApplySavedWindowSize()
        {
            if (App.Config.Settings.WindowWidth >= 480)
            {
                Width = App.Config.Settings.WindowWidth;
            }
            if (App.Config.Settings.WindowHeight >= 260)
            {
                Height = App.Config.Settings.WindowHeight;
            }
        }

        private void SaveWindowSize()
        {
            if (WindowState == WindowState.Normal && Width >= 480 && Height >= 260)
            {
                if (Math.Abs(App.Config.Settings.WindowWidth - Width) > 1 ||
                    Math.Abs(App.Config.Settings.WindowHeight - Height) > 1)
                {
                    App.Config.Settings.WindowWidth = Math.Round(Width);
                    App.Config.Settings.WindowHeight = Math.Round(Height);
                    App.Config.Save();
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeSystemIntegration();
            PerformSearch();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public void ForceForeground()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hWnd = helper.Handle;
                if (hWnd != IntPtr.Zero)
                {
                    keybd_event(0, 0, 0, UIntPtr.Zero);
                    SetForegroundWindow(hWnd);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] MainWindow.xaml.cs:250 静默异常: {ex.Message}");
            }
        }

        public void ShowAndActivate()
        {
            Dispatcher.Invoke(() =>
            {
                var helper = new WindowInteropHelper(this);
                IntPtr currentFg = SystemController.GetForegroundWindow();
                if (currentFg != IntPtr.Zero && currentFg != helper.Handle)
                {
                    SystemController.LastForegroundWindow = currentFg;
                }

                // 呼出位置跟随鼠标所在显示器（含 DPI 换算），不再固定在主屏
                Rect workArea = WindowHelper.GetWorkAreaAtCursor(this);
                double targetLeft = workArea.Left + (workArea.Width - Width) / 2;
                double targetTop = workArea.Top + workArea.Height * 0.22;
                Left = Math.Max(workArea.Left + 10, Math.Min(targetLeft, workArea.Left + workArea.Width - Width - 10));
                Top = Math.Max(workArea.Top + 20, Math.Min(targetTop, workArea.Top + workArea.Height - Height - 20));

                // 必须先 Show 再搜索：窗口处于 Hidden 时 ListBox 尚未进入可视树，
                // 此时设置的 ItemsSource / SelectedIndex=0 会在 Show 后被布局重置为 -1。
                // 结果就是“成功运行一次后再次呼出，未输入字符时滚轮始终停在第 1 项”。
                Show();
                WindowState = WindowState.Normal;

                PerformSearch(); // 内部已调用 ResetListScrollToTop()，无需重复复位

                Activate();
                ForceForeground();
                SearchBox.Focus();
                SearchBox.SelectAll();
            });
        }

        public void HideWindow()
        {
            SaveWindowSize();

            // 提示窗以主窗口为 Owner：主窗口 Hide 时它会被连带隐藏，但不会触发 Closed，
            // 于是 _missingFileTip 与 _suppressHideOnDeactivate 永远不复位——此后主界面再也不会失焦自动隐藏。
            // 这里先显式关闭（Closed 处理器会复位状态），再兜底强制复位，保证状态一定干净。
            if (_missingFileTip != null)
            {
                try
                {
                    _missingFileTip.Close();
                }
                catch (Exception ex)
                {
                    AppLog.Write($"[ALTRun] MainWindow.HideWindow 关闭提示窗失败: {ex.Message}");
                }
            }
            _missingFileTip = null;
            _suppressHideOnDeactivate = false;

            _searchDebounceTimer.Stop();

            // 清空输入会触发 SearchBox_TextChanged；用 _suspendSearch 抑制其重启防抖定时器，
            // 否则隐藏之后还会再跑一次毫无意义的搜索。
            _suspendSearch = true;
            try
            {
                SearchBox.Text = string.Empty;
            }
            finally
            {
                _suspendSearch = false;
            }

            ResetListScrollToTop();
            Hide();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 提示窗打开期间，主界面失去焦点属于预期行为，不能触发自动隐藏
            if (_suppressHideOnDeactivate) return;

            if (App.Config.Settings.HideWhenLostFocus)
            {
                HideWindow();
            }
        }

        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            App.OpenManageWindow();
        }

        /// <summary>
        /// 释放系统级资源（托盘图标、全局热键）。幂等实现：窗口关闭与进程退出两条路径都会调用，
        /// 避免某条路径没经过 OnClosed 时在任务栏留下"僵尸"托盘图标或占用热键。
        /// </summary>
        public void ReleaseSystemResources()
        {
            try
            {
                _trayIconService?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] MainWindow 释放托盘图标失败: {ex.Message}");
            }
            finally
            {
                _trayIconService = null;
            }

            try
            {
                _globalHotKey?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] MainWindow 释放全局热键失败: {ex.Message}");
            }
            finally
            {
                _globalHotKey = null;
            }

            // 停掉隐藏态工作集裁剪的轮询，避免退出过程中还有定时器回调
            MemoryTrimmer.Stop();
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveWindowSize();
            ReleaseSystemResources();
            base.OnClosed(e);
        }

    }
}
