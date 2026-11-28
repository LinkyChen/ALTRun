using System;
using System.Windows;
using System.Windows.Input;
using ALTRun.Services;

namespace ALTRun.Views
{
    /// <summary>
    /// 统一风格的二选一确认弹窗（深色圆角卡片 + 主题按钮），与主界面 DeleteConfirmWindow 同风格，
    /// 取代管理中心的系统原生 MessageBox。左为主按钮（确定/是/确认合并），右为次按钮（否/取消）。
    /// 结果通过 ShowDialog 返回：true=主按钮，false/null=次按钮。
    /// </summary>
    public partial class ConfirmDialogWindow : Window
    {
        private ConfirmDialogWindow(Window owner, string title, string message,
            string primaryText, string secondaryText, bool focusSecondary,
            bool swapButtonColors = false)
        {
            InitializeComponent();

            Title = string.IsNullOrEmpty(title) ? AppStrings.AppName : title;
            TxtMessage.Text = message ?? string.Empty;
            BtnPrimary.Content = primaryText;
            BtnSecondary.Content = secondaryText;
            // 次按钮文案为空时隐藏，退化为单按钮提示窗（OK 场景）
            bool single = string.IsNullOrEmpty(secondaryText);
            BtnSecondary.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

            // 检查更新场景：左右按钮配色互换（左"是"灰底描边、右"否"主题强调色实底）。
            // 仅交换样式，不影响按钮文字、位置、聚焦与键盘/鼠标行为。
            if (swapButtonColors && !single)
            {
                Style primaryStyle = BtnPrimary.Style;
                BtnPrimary.Style = BtnSecondary.Style;
                BtnSecondary.Style = primaryStyle;
            }

            Owner = owner;

            // 跟随宿主显示器居中
            Rect area = WindowHelper.GetWorkArea(owner);
            MaxHeight = Math.Max(240, area.Height - 40);

            if (focusSecondary && !single)
            {
                // 检查更新场景：右侧"否"按钮聚焦，且空格/回车/ESC/鼠标点击都能触发
                BtnSecondary.IsDefault = true;   // 回车 → 否
                BtnSecondary.IsCancel = true;    // ESC → 否（并自动 DialogResult=false）
                BtnPrimary.IsDefault = false;
            }
            else
            {
                // 查重合并场景：左侧主按钮聚焦；右侧"取消"响应 ESC（单按钮时隐藏不影响）
                BtnPrimary.IsDefault = true;
                BtnSecondary.IsCancel = true;
            }

            try
            {
                ThemeManager.ApplyTheme(this, ThemeManager.GetTheme(
                    App.Config.Settings.Theme, App.Config.Settings.DarkMode));
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ConfirmDialog] 主题应用失败: {ex.Message}");
            }

            // Esc 由 BtnSecondary.IsCancel=true 统一处理（两个分支都设置了），
            // 无需额外 PreviewKeyDown，避免与 IsCancel 重复关闭窗口。

            Loaded += (_, _) =>
            {
                double left = area.Left + (area.Width - ActualWidth) / 2;
                double top = area.Top + (area.Height - ActualHeight) / 2;
                Left = Math.Max(area.Left, left);
                Top = Math.Max(area.Top, top);
                (focusSecondary && !single ? BtnSecondary : BtnPrimary).Focus();
            };
        }

        /// <summary>
        /// 弹出确认窗。返回 true=点击左侧主按钮，false=点击右侧次按钮 / Esc / 关闭。
        /// </summary>
        public static bool Confirm(Window owner, string title, string message,
            string primaryText, string secondaryText, bool focusSecondary,
            bool swapButtonColors = false)
        {
            if (owner == null) return false;
            var dlg = new ConfirmDialogWindow(owner, title, message, primaryText, secondaryText,
                focusSecondary, swapButtonColors);
            return dlg.ShowDialog() == true;
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
