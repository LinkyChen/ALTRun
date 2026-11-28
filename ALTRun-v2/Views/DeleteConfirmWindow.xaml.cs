using System;
using System.Windows;
using System.Windows.Input;
using ALTRun.Models;
using ALTRun.Services;

namespace ALTRun.Views
{
    /// <summary>
    /// 删除条目确认窗口：展示待删条目的快捷词 / 名称 / 命令行，
    /// 左侧“删除”按钮（默认聚焦，空格/回车/鼠标均可触发），右侧“取消”按钮。
    /// 结果通过 DialogResult 返回：true=删除，false=取消。
    /// </summary>
    public partial class DeleteConfirmWindow : Window
    {
        /// <summary>
        /// 统一的删除确认入口：主界面右键 / 未找到提示 / 管理中心（按钮与列表右键）共用，
        /// 展示同一个窗口（快捷词、名称、命令行 + 删除/取消），返回 true 表示用户确认删除。
        /// </summary>
        public static bool Confirm(ShortCutItem item, Window owner)
        {
            if (item == null || owner == null) return false;
            return new DeleteConfirmWindow(item, owner).ShowDialog() == true;
        }

        public DeleteConfirmWindow(ShortCutItem item, Window owner)
        {
            InitializeComponent();

            TxtShortCut.Text = item?.ShortCut ?? string.Empty;
            TxtName.Text = item?.Name ?? string.Empty;
            TxtCommandLine.Text = item?.CommandLine ?? string.Empty;
            Owner = owner;

            // 跟随宿主的显示器，避免副屏呼出时窗口跑回主屏
            Rect area = WindowHelper.GetWorkArea(owner);
            MaxHeight = Math.Max(220, area.Height - 40);

            try
            {
                ThemeManager.ApplyTheme(this, ThemeManager.GetTheme(
                    App.Config.Settings.Theme, App.Config.Settings.DarkMode));
            }
            catch (Exception ex)
            {
                AppLog.Write($"[DeleteConfirm] 主题应用失败: {ex.Message}");
            }

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                }
            };

            Loaded += (_, _) =>
            {
                double left = area.Left + (area.Width - ActualWidth) / 2;
                double top = area.Top + (area.Height - ActualHeight) / 2;
                Left = Math.Max(area.Left, left);
                Top = Math.Max(area.Top, top);
                BtnDelete.Focus();
            };
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
