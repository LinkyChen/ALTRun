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
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 任何按键决策前先把待处理的防抖搜索落地，保证基于最新输入结果操作
            FlushPendingSearch();

            if (e.Key == Key.Escape)
            {
                // 第一次 Esc：清空输入但保留界面；第二次（输入本来就是空的）Esc：隐藏界面
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    SearchBox.Text = string.Empty;
                    SearchBox.Focus();
                    e.Handled = true;
                    return;
                }

                HideWindow();
                e.Handled = true;
                return;
            }

            // 输入后按空格 = 执行当前选中项（Shift+Space 仍可输入普通空格）
            if (e.Key == Key.Space &&
                (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift &&
                SearchBox.IsKeyboardFocused &&
                SearchBox.SelectionLength == 0 &&
                SearchBox.CaretIndex == SearchBox.Text.Length &&
                !string.IsNullOrWhiteSpace(SearchBox.Text) &&
                !SearchBox.Text.TrimStart().StartsWith(">") &&
                !SearchBox.Text.TrimStart().StartsWith("=") &&
                GetSelectedResultItem() is ShortCutItem)
            {
                bool runAsAdmin = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                ExecuteSelectedItem(runAsAdmin);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (GetSelectedResultItem() is ShortCutItem selected && !IsSpecialCommandItem(selected))
                {
                    OpenItemDirectory(selected);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Tab)
            {
                if (GetSelectedResultItem() is ShortCutItem selected)
                {
                    SearchBox.Text = selected.ShortCut + " ";
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                int current = _selectedResultIndex >= 0 ? _selectedResultIndex : ResultListBox.SelectedIndex;
                if (current < ResultListBox.Items.Count - 1)
                {
                    SetSelectedResult(current + 1);
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                int current = _selectedResultIndex >= 0 ? _selectedResultIndex : ResultListBox.SelectedIndex;
                if (current > 0)
                {
                    SetSelectedResult(current - 1);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                bool runAsAdmin = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                ExecuteSelectedItem(runAsAdmin);
                e.Handled = true;
                return;
            }

            // ── 数字键直达触发机制 (支持 1~9 以及 0 表示第 10 项) ──
            Key actualKey = e.Key;
            if (actualKey == Key.System) actualKey = e.SystemKey;
            if (actualKey == Key.ImeProcessed) actualKey = e.ImeProcessedKey;

            int digit = -1;
            if (actualKey >= Key.D1 && actualKey <= Key.D9)
            {
                digit = actualKey - Key.D1 + 1;
            }
            else if (actualKey == Key.D0)
            {
                digit = 10;
            }
            else if (actualKey >= Key.NumPad1 && actualKey <= Key.NumPad9)
            {
                digit = actualKey - Key.NumPad1 + 1;
            }
            else if (actualKey == Key.NumPad0)
            {
                digit = 10;
            }

            if (digit > 0)
            {
                bool hasAlt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
                bool hasCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                bool hasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

                // 1. 显式修饰键直达: Alt + 数字 或 Ctrl + 数字 (最高优先级，100% 确认意图为选择序号)
                if (hasAlt || (hasCtrl && !hasShift))
                {
                    int targetIndex = digit - 1;
                    if (targetIndex >= 0 && targetIndex < _currentResult.Items.Count)
                    {
                        SetSelectedResult(targetIndex);
                        bool runAsAdmin = hasCtrl;
                        ExecuteSelectedItem(runAsAdmin);
                        e.Handled = true;
                        return;
                    }
                }

                // 2. 单数字键直达:
                // 旧版对所有情况都用 "追加数字后 HasAnyMatch 无匹配才直达" 判定，
                // 但空输入时 predictedQuery 只有一位数字，而配置里大量项含数字（Win10/7zip/日期等），
                // HasAnyMatch 几乎恒为 true，导致呼出后按数字从不直达。
                // 修正：空输入时列表是 Top N，数字键一律按序号直达；已有输入时保留原智能判定，
                // 使 win10、7zip 这类含数字关键词仍可正常输入。
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    string currentText = SearchBox.SelectionLength == SearchBox.Text.Length ? string.Empty : SearchBox.Text;

                    // 算式模式和终端命令行模式豁免，保证用户能正常输入数字算式与命令
                    if (!currentText.StartsWith(">") && !currentText.StartsWith("="))
                    {
                        bool shouldDirectRun;
                        if (string.IsNullOrEmpty(currentText))
                        {
                            // 空输入（Top N 列表）：数字键即序号，直接运行对应项
                            shouldDirectRun = true;
                        }
                        else
                        {
                            // 已有输入：追加数字后若仍能匹配快捷词则放行输入，否则视为序号直达
                            string predictedQuery = currentText + digit.ToString();
                            shouldDirectRun = !SearchEngine.HasAnyMatch(predictedQuery, App.Config.Settings.ShortCuts);
                        }

                        int targetIndex = digit - 1;
                        if (shouldDirectRun && targetIndex >= 0 && targetIndex < _currentResult.Items.Count)
                        {
                            SetSelectedResult(targetIndex);
                            ExecuteSelectedItem(false);
                            e.Handled = true;
                        }
                        // 其余情况放行数字进入搜索框（继续拼含数字的关键词）
                    }
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindVisualParent<System.Windows.Controls.TextBox>(dep) != null ||
                    FindVisualParent<System.Windows.Controls.Button>(dep) != null ||
                    FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) != null ||
                    FindVisualParent<ListBoxItem>(dep) != null)
                {
                    return;
                }
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (Exception ex)
                {
                    AppLog.Write($"[ALTRun] MainWindow.xaml.cs:883 静默异常: {ex.Message}");
                }
            }
        }

        private static string NormalizeDropPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

            // 拖放场景保持原有行为：重复目标只提升频次（不弹替换确认）
            AddFilesAsShortcuts(files, confirmReplace: false);
        }

        /// <summary>
        /// 把外部文件（拖放 / 右键"发送到 ALTRun"）添加为快捷项。
        /// </summary>
        /// <param name="files">文件路径列表。</param>
        /// <param name="confirmReplace">
        /// true = "发送到"场景：弹 WPF 添加对话框让用户修改快捷词/名称/参数类型，
        ///        确定后按快捷词判断是否替换已有项（对齐老版 TShortCutForm 交互）；
        /// false = 拖放场景：静默添加，重复目标只提升频次，不弹窗。
        /// </param>
        public void AddFilesAsShortcuts(IEnumerable<string> files, bool confirmReplace)
        {
            var fileList = files.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).ToList();
            if (fileList.Count == 0) return;

            var shortcuts = App.Config.Settings.ShortCuts;
            var usedKeys = new HashSet<string>(
                shortcuts.Where(s => !string.IsNullOrWhiteSpace(s.ShortCut)).Select(s => s.ShortCut),
                StringComparer.OrdinalIgnoreCase);
            var usedPaths = new HashSet<string>(
                shortcuts.Select(s => NormalizeDropPath(s.CommandLine)),
                StringComparer.OrdinalIgnoreCase);

            bool anyAdded = false;
            foreach (var file in fileList)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("ShortCutList.txt", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    var imported = App.Config.ImportFromLegacyFile(file)
                        .Where(item => usedPaths.Add(NormalizeDropPath(item.CommandLine)))
                        .ToList();
                    if (imported.Count > 0)
                    {
                        shortcuts.AddRange(imported);
                        foreach (var item in imported)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ShortCut)) usedKeys.Add(item.ShortCut);
                            App.Config.RefreshPinyinCache(item);
                        }
                        anyAdded = true;
                        MessageBox.Show(string.Format(AppStrings.ImportSuccessFormat, imported.Count), AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    continue;
                }

                string normalized = NormalizeDropPath(file);
                string baseName = Path.GetFileNameWithoutExtension(file);

                if (confirmReplace)
                {
                    // 对齐老版：先弹出添加窗口，预填智能快捷词/名称/命令行，用户点确定后才写入
                    string suggestedKey = AppScannerService.GenerateSmartShortcut(baseName, usedKeys);
                    var dialog = new AddShortcutWindow(suggestedKey, baseName, file, ParamType.ptNone);
                    if (dialog.ShowDialog() != true) continue;

                    string key = dialog.ResultShortCut;
                    string name = dialog.ResultName;
                    string command = dialog.ResultCommandLine;
                    ParamType paramType = dialog.ResultParamType;

                    // 老版按"快捷词"判断重复（GetShortCutItemByShortCut），不是按目标路径
                    var existing = shortcuts.FirstOrDefault(
                        s => string.Equals(s.ShortCut, key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        string body = string.Format(AppStrings.SendToReplaceFormat,
                            existing.ShortCut, existing.Name, existing.CommandLine);
                        // 不带 owner：冷启动时主窗口隐藏在托盘，以隐藏窗口为 owner 弹窗可能不置顶
                        var answer = MessageBox.Show(body, AppStrings.SendToReplaceTitle,
                            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                        if (answer != MessageBoxResult.Yes) continue; // 否：保留原项

                        existing.Name = name;
                        existing.CommandLine = command;
                        existing.ParamType = paramType;
                        existing.Freq = 0;
                        App.Config.RefreshPinyinCache(existing);
                        anyAdded = true;
                        AppLog.Write($"[SendTo] 替换已有快捷项: {key} -> {command}");
                        continue;
                    }

                    var added = new ShortCutItem
                    {
                        ShortCut = key,
                        Name = name,
                        CommandLine = command,
                        ParamType = paramType,
                        Freq = 0
                    };
                    App.Config.RefreshPinyinCache(added);
                    shortcuts.Insert(0, added);
                    usedKeys.Add(key);
                    anyAdded = true;
                    AppLog.Write($"[SendTo] 新增快捷项: {key} -> {command}");
                    continue;
                }

                // 拖放场景保持原有静默行为
                if (!usedPaths.Add(normalized))
                {
                    var duplicate = shortcuts.FirstOrDefault(s => string.Equals(NormalizeDropPath(s.CommandLine), normalized, StringComparison.OrdinalIgnoreCase));
                    if (duplicate != null) duplicate.Freq++;
                    continue;
                }

                string shortCutKey = AppScannerService.GenerateSmartShortcut(baseName, usedKeys);
                var newItem = new ShortCutItem
                {
                    ShortCut = shortCutKey,
                    Name = baseName,
                    CommandLine = file,
                    Freq = 0
                };
                App.Config.RefreshPinyinCache(newItem);
                shortcuts.Insert(0, newItem);
                anyAdded = true;
            }

            if (anyAdded)
            {
                App.Config.Save();
                PerformSearch();
            }
        }

    }
}
