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
        /// <summary>
        /// 显示“未找到该条目”提示：非模态、置顶于主界面上层，在屏幕工作区内上下左右居中；
        /// 提供“删除 / 检查”两个按钮。删除按钮聚焦，空格/回车/鼠标点击均可触发；
        /// “检查”或 Esc 直接关闭返回主界面；“删除”弹出二次确认窗口后决定是否删除条目。
        /// 打开期间抑制主界面的失焦自动隐藏。
        /// </summary>
        private void ShowMissingFileTip(string message, ShortCutItem item)
        {
            // 已经有一个提示窗时只把它激活，不重复弹出
            if (_missingFileTip != null && _missingFileTip.IsLoaded)
            {
                _missingFileTip.Activate();
                return;
            }

            var tip = new TipWindow(message, this);
            tip.OnDeleteRequested = () =>
            {
                // 先关掉提示窗（其 Closed 事件会复位状态并把焦点交回主界面），
                // 再进入删除确认流程；item 用闭包捕获，与提示窗生命周期无关。
                tip.Close();
                ConfirmAndDeleteItem(item);
            };

            _missingFileTip = tip;
            _suppressHideOnDeactivate = true;
            tip.Closed += (_, _) =>
            {
                _missingFileTip = null;
                _suppressHideOnDeactivate = false;
                if (IsLoaded)
                {
                    Activate();
                    SearchBox.Focus();
                }
            };

            tip.Show();
        }

        /// <summary>
        /// 弹出删除确认窗口（展示快捷词/名称/命令行），用户点“删除”后才真正移除条目。
        /// 供“未找到该条目”提示窗与主界面右键菜单共用。
        /// </summary>
        internal void ConfirmAndDeleteItem(ShortCutItem item)
        {
            if (item == null) return;

            // 删除确认窗口是模态窗，打开期间主界面失焦属预期行为，不能触发自动隐藏。
            // 保存原状态，结束后恢复，避免影响后续正常的失焦隐藏逻辑。
            bool previousSuppress = _suppressHideOnDeactivate;
            _suppressHideOnDeactivate = true;
            try
            {
                if (DeleteConfirmWindow.Confirm(item, this))
                {
                    DeleteShortcutItem(item);
                }
            }
            finally
            {
                _suppressHideOnDeactivate = previousSuppress;
                if (IsLoaded)
                {
                    Activate();
                    SearchBox.Focus();
                }
            }
        }

        /// <summary>
        /// 从配置中删除指定条目并持久化。搜索结果是 Clone 副本，需按 Id 定位原始配置项。
        /// 删除后刷新拼音缓存与当前搜索列表，让候选项立即消失。
        /// </summary>
        private void DeleteShortcutItem(ShortCutItem item)
        {
            var all = App.Config.Settings.ShortCuts;
            var original = all.FirstOrDefault(x => x.Id == item.Id);
            if (original != null)
            {
                all.Remove(original);
                App.Config.RefreshPinyinCache();
                App.Config.Save();
            }

            // 同步刷新当前候选列表，保证被删条目不再出现在主界面
            PerformSearch();
        }

    }
}
