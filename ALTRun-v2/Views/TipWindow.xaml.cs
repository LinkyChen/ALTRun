using System;
using System.Windows;
using System.Windows.Input;
using ALTRun.Services;

namespace ALTRun.Views
{
    /// <summary>
    /// “未找到该条目”提示窗：非模态、置顶于主界面上层，在屏幕工作区内上下左右居中；
    /// 提供“删除 / 检查”两个按钮，删除按钮默认聚焦，空格 / 回车 / 鼠标点击均可触发；
    /// Esc 视为“检查”，直接关闭返回主界面。
    /// </summary>
    public partial class TipWindow : Window
    {
        /// <summary>点击“删除”时回调，由宿主决定是否弹出删除确认窗口。</summary>
        public Action? OnDeleteRequested { get; set; }

        private bool _closeByCheck;

        public TipWindow(string message, Window owner)
        {
            InitializeComponent();

            MessageText.Text = message;
            Owner = owner;

            // 跟随宿主窗口所在显示器，避免副屏呼出时提示窗跑回主屏
            Rect area = WindowHelper.GetWorkArea(owner);
            MaxHeight = Math.Max(160, area.Height - 40);

            // 键盘语义：
            // - 空格 / 回车：交给聚焦的“删除”按钮（IsDefault），空格由 WPF 按钮原生处理，无需拦截；
            // - Esc：视为“检查”，标记后关闭返回主界面。
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _closeByCheck = true;
                    Close();
                    e.Handled = true;
                }
                // 注意：不再对 Enter / Space 直接 Close()。
                // Enter 由 IsDefault 的 DeleteButton 触发 Click；Space 由聚焦按钮原生触发 Click。
            };

            // SizeToContent 下需在 Loaded 后拿到实际尺寸再定位，同时把焦点交给“删除”按钮
            Loaded += (_, _) =>
            {
                double left = area.Left + (area.Width - ActualWidth) / 2;
                double top = area.Top + (area.Height - ActualHeight) / 2;
                Left = Math.Max(area.Left, left);
                Top = Math.Max(area.Top, top);
                DeleteButton.Focus();
            };
        }

        /// <summary>宿主读取：本次是否通过“检查/Esc”关闭（用于区分是否需要清理条目）。</summary>
        public bool ClosedByCheck => _closeByCheck;

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            OnDeleteRequested?.Invoke();
        }

        private void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            _closeByCheck = true;
            Close();
        }
    }
}
