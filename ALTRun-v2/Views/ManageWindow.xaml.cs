using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ALTRun.Models;
using ALTRun.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ALTRun.Views
{
    public partial class ManageWindow : Window
    {
        private ObservableCollection<ShortCutItem> _viewList = new();
        private ShortCutItem? _selectedItem;

        public ManageWindow(ShortCutItem? itemToEdit = null, bool addNew = false)
        {
            InitializeComponent();
            AppLog.Write($"[ManageSel] ManageWindow 创建 Hash={GetHashCode()} addNew={addNew} itemToEdit={itemToEdit?.ShortCut}");
            WindowHelper.FitToWorkArea(this);
            InitVersionInfo();
            InitHotKeyControls();
            // 主题统一在 Loaded 里应用一次：构造函数里再应用一次会重复创建约 30 个画刷、
            // 重复调用 DWM 并提前强制创建窗口句柄，是打开偏慢的次要原因。

            Loaded += (s, e) =>
            {
                ManageWindow_Loaded(s, e);

                if (addNew)
                {
                    BtnAdd_Click(this, new RoutedEventArgs());
                }
                else if (itemToEdit != null)
                {
                    SelectAndFocusItem(itemToEdit);
                }
            };
        }

        public void SelectAndFocusItem(ShortCutItem item)
        {
            var match = App.Config.Settings.ShortCuts.FirstOrDefault(x => x.Id == item.Id ||
                (x.ShortCut == item.ShortCut && x.Name == item.Name && x.CommandLine == item.CommandLine));

            if (match != null)
            {
                // TxtFilter.Text 赋空值本身就会触发 TextChanged → RefreshGrid，
                // 无需在这里再显式刷新一次（重复重建 215 行 ItemsSource）。
                if (!string.IsNullOrEmpty(TxtFilter.Text))
                {
                    TxtFilter.Text = string.Empty;
                }
                SelectItemByReference(match, focusEditor: true);
            }
        }

        private void InitVersionInfo()
        {
            try
            {
                var ver = typeof(ManageWindow).Assembly.GetName().Version;
                string verStr = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v2.0.8";
                Title = string.Format(AppStrings.ManageTitleFormat, verStr);
            }
            catch
            {
                Title = AppStrings.ManageTitleFallback;
            }
        }

        private void ManageWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppLog.Write($"[ManageSel] Loaded Hash={GetHashCode()} 存活ManageWindow数={System.Windows.Application.Current.Windows.OfType<ManageWindow>().Count()}");
            // 绑定主题下拉选项
            CmbThemes.ItemsSource = ThemeManager.Themes;
            var currentTheme = ThemeManager.GetTheme(App.Config.Settings.Theme, App.Config.Settings.DarkMode);
            CmbThemes.SelectedItem = ThemeManager.Themes.FirstOrDefault(x => x.Id == currentTheme.Id) ?? ThemeManager.Themes[0];
            ApplyTheme(currentTheme);

            LoadCurrentHotKey();
            RefreshGrid();
        }

        private void InitHotKeyControls()
        {
            // 初始化常用按键候选列表
            var keys = new List<string>();

            // 字母 A-Z
            for (char c = 'A'; c <= 'Z'; c++)
            {
                keys.Add(c.ToString());
            }

            // 特殊键
            keys.Add("Space");
            keys.Add("Tab");
            keys.Add("Return");
            keys.Add("OemTilde");

            // 数字 0-9
            for (int i = 0; i <= 9; i++)
            {
                keys.Add("D" + i);
            }

            // 功能键 F1-F12
            for (int i = 1; i <= 12; i++)
            {
                keys.Add("F" + i);
            }

            CmbKey.ItemsSource = keys;
        }

        private void LoadCurrentHotKey()
        {
            string current = App.Config.Settings.HotKey;
            var (modifiers, key) = MainWindow.ParseHotKey(current);
            ChkAlt.IsChecked = (modifiers & KeyModifiers.Alt) != 0;
            ChkCtrl.IsChecked = (modifiers & KeyModifiers.Control) != 0;
            ChkShift.IsChecked = (modifiers & KeyModifiers.Shift) != 0;
            ChkWin.IsChecked = (modifiers & KeyModifiers.Windows) != 0;

            string keyStr = key == Key.Space ? "Space" : key.ToString();
            CmbKey.SelectedItem = keyStr;
            if (CmbKey.SelectedIndex < 0)
            {
                CmbKey.SelectedItem = "R";
            }
        }

        private void BtnSaveHotKey_Click(object sender, RoutedEventArgs e)
        {
            KeyModifiers modifiers = KeyModifiers.None;
            if (ChkAlt.IsChecked == true) modifiers |= KeyModifiers.Alt;
            if (ChkCtrl.IsChecked == true) modifiers |= KeyModifiers.Control;
            if (ChkShift.IsChecked == true) modifiers |= KeyModifiers.Shift;
            if (ChkWin.IsChecked == true) modifiers |= KeyModifiers.Windows;

            if (modifiers == KeyModifiers.None)
            {
                WpfMessageBox.Show(AppStrings.HotKeyNeedModifier, AppStrings.MsgBoxTitleHint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? selectedKeyStr = CmbKey.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedKeyStr))
            {
                WpfMessageBox.Show(AppStrings.HotKeyNeedMainKey, AppStrings.MsgBoxTitleHint, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Key key;
            if (selectedKeyStr == "Space")
            {
                key = Key.Space;
            }
            else if (!Enum.TryParse(selectedKeyStr, true, out key))
            {
                key = Key.R;
            }

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWin)
            {
                bool success = mainWin.UpdateHotKey(modifiers, key);
                if (success)
                {
                    string hotkeyText = MainWindow.FormatHotKeyText(modifiers, key);
                    WpfMessageBox.Show(string.Format(AppStrings.HotKeyUpdatedFormat, hotkeyText), AppStrings.HotKeyUpdatedTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    WpfMessageBox.Show(AppStrings.HotKeyConflict, AppStrings.HotKeyConflictTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        public void ApplyTheme(ThemeDefinition theme)
        {
            ThemeManager.ApplyTheme(this, theme);

            if (CmbThemes != null && (CmbThemes.SelectedItem as ThemeDefinition)?.Id != theme.Id)
            {
                var match = ThemeManager.Themes.FirstOrDefault(x => x.Id == theme.Id);
                if (match != null)
                {
                    CmbThemes.SelectedItem = match;
                }
            }

            // 表格行与边框特定对比配色
            if (theme.IsDark)
            {
                Resources["RowBg"] = new SolidColorBrush(theme.CardBgColor);
                Resources["RowAltBg"] = new SolidColorBrush(Color.FromRgb((byte)Math.Min(theme.CardBgColor.R + 6, 255),
                                                                         (byte)Math.Min(theme.CardBgColor.G + 6, 255),
                                                                         (byte)Math.Min(theme.CardBgColor.B + 8, 255)));
                Resources["RowHoverBg"] = new SolidColorBrush(theme.HoverBgColor);
                Resources["RowSelectedBg"] = new SolidColorBrush(theme.HighlightBgColor);
                Resources["HeaderBg"] = new SolidColorBrush(theme.BgColor);
                Resources["BtnBg"] = new SolidColorBrush(theme.CardBgColor);
                Resources["BtnFg"] = new SolidColorBrush(theme.TextColor);
                Resources["BtnBorder"] = new SolidColorBrush(theme.BorderColor);
                Resources["GridLine"] = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
                Resources["BadgeBg"] = new SolidColorBrush(theme.BadgeBgColor);
            }
            else
            {
                Resources["RowBg"] = new SolidColorBrush(Colors.White);
                Resources["RowAltBg"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                Resources["RowHoverBg"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6));
                Resources["RowSelectedBg"] = new SolidColorBrush(theme.HighlightBgColor);
                Resources["HeaderBg"] = new SolidColorBrush(Color.FromRgb(0xEA, 0xED, 0xF4));
                Resources["BtnBg"] = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF4));
                Resources["BtnFg"] = new SolidColorBrush(theme.TextColor);
                Resources["BtnBorder"] = new SolidColorBrush(theme.BorderColor);
                Resources["GridLine"] = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
                Resources["BadgeBg"] = new SolidColorBrush(theme.BadgeBgColor);
            }

            try
            {
                DwmHelper.ApplyModernWindowStyles(this, theme.IsDark);
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] ManageWindow.xaml.cs:236 静默异常: {ex.Message}");
            }
        }

        private void CmbThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbThemes.SelectedItem is ThemeDefinition selected)
            {
                if (App.Config.Settings.Theme != selected.Id)
                {
                    App.Config.Settings.Theme = selected.Id;
                    App.Config.Settings.DarkMode = selected.IsDark;
                    App.Config.Save();

                    ApplyTheme(selected);

                    if (System.Windows.Application.Current.MainWindow is MainWindow mainWin)
                    {
                        mainWin.ApplyTheme(selected);
                    }
                }
            }
        }



        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var allThemes = ThemeManager.Themes;
            var current = ThemeManager.GetTheme(App.Config.Settings.Theme, App.Config.Settings.DarkMode);
            int idx = allThemes.FindIndex(x => x.Id == current.Id);
            int nextIdx = (idx + 1) % allThemes.Count;
            var nextTheme = allThemes[nextIdx];

            CmbThemes.SelectedItem = nextTheme;
        }

        private void RefreshGrid(string filter = "")
        {
            var source = App.Config.Settings.ShortCuts;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                string lower = filter.ToLowerInvariant();
                source = source.Where(x => (x.ShortCut ?? "").ToLowerInvariant().Contains(lower) ||
                                           (x.Name ?? "").ToLowerInvariant().Contains(lower) ||
                                           (x.CommandLine ?? "").ToLowerInvariant().Contains(lower) ||
                                           (x.PinyinInitials ?? "").ToLowerInvariant().Contains(lower)).ToList();
            }

            _viewList = new ObservableCollection<ShortCutItem>(source);
            ShortcutGrid.ItemsSource = _viewList;
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshGrid(TxtFilter.Text);
        }

        private void ShortcutGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 优先取"本次新增"的选中项：DataGrid 内部状态异常时 SelectedItems 可能同时残留旧行与新行，
            // SelectedItem 属性只反映其中第一个（可能是旧行），取 AddedItems 最后一个才是用户真正点到的行。
            var target = e.AddedItems.Count > 0
                ? e.AddedItems[e.AddedItems.Count - 1] as ShortCutItem
                : ShortcutGrid.SelectedItem as ShortCutItem;

            AppLog.Write($"[ManageSel] SelectionChanged Added={e.AddedItems.Count} Removed={e.RemovedItems.Count} " +
                         $"SelectedItems={ShortcutGrid.SelectedItems.Count} -> {target?.ShortCut}");
            ApplySelection(target);
        }

        /// <summary>按引用（而非 record 值相等）在当前视图列表中定位索引，避免值相等项歧义。</summary>
        private int IndexOfReference(ShortCutItem item)
        {
            for (int i = 0; i < _viewList.Count; i++)
            {
                if (ReferenceEquals(_viewList[i], item))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 按引用选中一项并同步编辑框。用 SelectedIndex（索引赋值）而非 SelectedItem（要经过
        /// IndexOf/Equals 查找），从机制上排除 record 值相等导致的选中错位。
        /// </summary>
        private void SelectItemByReference(ShortCutItem item, bool focusEditor = false)
        {
            int idx = IndexOfReference(item);
            if (idx >= 0)
            {
                ShortcutGrid.SelectedIndex = idx;
                ShortcutGrid.ScrollIntoView(item);
            }
            ApplySelection(item);

            if (focusEditor)
            {
                EditShortCut.Focus();
                EditShortCut.SelectAll();
            }
        }

        /// <summary>
        /// 点击表格行：先 UnselectAll 彻底清掉 DataGrid 内部的选中记录（含可能残留的旧行），
        /// 再按索引重设选中，最后强制同步编辑框。这条路径不依赖 SelectionChanged 是否正常触发，
        /// 即使 DataGrid 选中状态被破坏，文本框也能跟着点击走。
        /// </summary>
        private void ShortcutGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row && row.DataContext is ShortCutItem item)
            {
                int idx = IndexOfReference(item);
                AppLog.Write($"[ManageSel] 点击行 ShortCut={item.ShortCut} idx={idx} rowIsSelected={row.IsSelected} " +
                             $"SelectedItem={(ShortcutGrid.SelectedItem as ShortCutItem)?.ShortCut} " +
                             $"SelectedItems={ShortcutGrid.SelectedItems.Count}");

                if (idx < 0)
                {
                    // 该行不在当前视图列表（如筛选重建后残留的旧容器），交给 DataGrid 默认逻辑
                    return;
                }

                ShortcutGrid.UnselectAll();
                ShortcutGrid.SelectedIndex = idx;
                ClearStaleRowVisual(item);
                ApplySelection(item);
            }
        }

        /// <summary>
        /// 强制清除"视觉残留选中"：行容器回收（VirtualizationMode=Recycling）或状态错乱时，
        /// 旧行容器的 IsSelected 可能仍为 true，而 DataGrid 的逻辑选中已不含它（UnselectAll
        /// 清不到这种残留，运行日志已证实 selectedItems 会出现 2 条）。这里按 DataContext
        /// 逐一核对，非当前选中项一律强制复位，保证视觉上永远只有一行高亮。
        /// </summary>
        private void ClearStaleRowVisual(ShortCutItem current)
        {
            for (int i = 0; i < ShortcutGrid.Items.Count; i++)
            {
                if (ShortcutGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow r &&
                    r.IsSelected &&
                    r.DataContext is ShortCutItem it &&
                    !ReferenceEquals(it, current))
                {
                    AppLog.Write($"[ManageSel] 清除视觉残留: index={i} ShortCut={it.ShortCut}");
                    r.IsSelected = false;
                }
            }
        }

        private void ApplySelection(ShortCutItem? item)
        {
            _selectedItem = item;
            EditShortCut.Text = item?.ShortCut ?? string.Empty;
            EditName.Text = item?.Name ?? string.Empty;
            EditCommandLine.Text = item?.CommandLine ?? string.Empty;
        }

        private void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = AppStrings.SelectProgramTitle,
                Filter = AppStrings.ProgramFileFilter
            };

            if (dialog.ShowDialog() == true)
            {
                string filePath = dialog.FileName;
                EditCommandLine.Text = filePath;

                // 若名称为空或是默认占位符，自动提取文件名
                if (string.IsNullOrWhiteSpace(EditName.Text) || EditName.Text == AppStrings.NewShortCutName)
                {
                    string name = Path.GetFileNameWithoutExtension(filePath);
                    EditName.Text = name;

                    if (string.IsNullOrWhiteSpace(EditShortCut.Text) || EditShortCut.Text == "new")
                    {
                        string initials = PinyinHelper.GetInitials(name).ToLowerInvariant();
                        EditShortCut.Text = !string.IsNullOrEmpty(initials) ? initials : name.ToLowerInvariant();
                    }
                }
            }
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = AppStrings.SelectFolderTitle
            };

            if (dialog.ShowDialog(this) == true)
            {
                string folderPath = dialog.FolderName;
                EditCommandLine.Text = folderPath;

                if (string.IsNullOrWhiteSpace(EditName.Text) || EditName.Text == AppStrings.NewShortCutName)
                {
                    string name = Path.GetFileName(folderPath);
                    if (string.IsNullOrEmpty(name)) name = folderPath;
                    EditName.Text = name;

                    if (string.IsNullOrWhiteSpace(EditShortCut.Text) || EditShortCut.Text == "new")
                    {
                        string initials = PinyinHelper.GetInitials(name).ToLowerInvariant();
                        EditShortCut.Text = !string.IsNullOrEmpty(initials) ? initials : name.ToLowerInvariant();
                    }
                }
            }
        }

        private void BtnSaveEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem != null)
            {
                AppLog.Write($"[ManageSel] 保存前 _selectedItem={_selectedItem.ShortCut} " +
                             $"gridSelectedItem={(ShortcutGrid.SelectedItem as ShortCutItem)?.ShortCut} " +
                             $"selectedItems={ShortcutGrid.SelectedItems.Count}");

                _selectedItem.ShortCut = EditShortCut.Text.Trim();
                _selectedItem.Name = EditName.Text.Trim();
                _selectedItem.CommandLine = EditCommandLine.Text.Trim();

                // 必须走 RefreshPinyinCache：它同时刷新拼音首字母与 4 个小写缓存（LowerShortCut / LowerName /
                // LowerPinyinInitials / LowerPinyinShortcut）。只更新 PinyinInitials/PinyinShortcut 会漏掉小写缓存，
                // 导致改名后主界面仍用旧关键词匹配——新名字搜不到，旧关键词却能搜到（搜索结果与显示内容不一致）。
                // 单条编辑只需刷新这一项，不必重算整表。
                App.Config.RefreshPinyinCache(_selectedItem);
                App.Config.Save();

                // ShortCutItem 已实现 INotifyPropertyChanged，上面改属性时四列绑定会自动刷新，
                // 这里绝不能再调用 Items.Refresh() / RefreshGrid()：它们会重建全部行容器，
                // 让 DataGrid 的视觉选中与 SelectedItem 脱节（表现为 A、B 两行同时高亮、
                // 点击其它行不再切换、编辑框不更新）。
                // 只有处于筛选状态时，修改后的项可能不再匹配筛选词，才按筛选重建列表。
                if (!string.IsNullOrWhiteSpace(TxtFilter.Text))
                {
                    RefreshGrid(TxtFilter.Text);
                }

                AppLog.Write($"[ManageSel] 保存后 selectedItems={ShortcutGrid.SelectedItems.Count} " +
                             $"gridSelectedItem={(ShortcutGrid.SelectedItem as ShortCutItem)?.ShortCut}");

                WpfMessageBox.Show(AppStrings.SavedMessage, AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // 若没有选中项，直接作为新增项保存
                var newItem = new ShortCutItem
                {
                    ShortCut = EditShortCut.Text.Trim(),
                    Name = EditName.Text.Trim(),
                    CommandLine = EditCommandLine.Text.Trim(),
                    Freq = 0
                };
                if (!string.IsNullOrEmpty(newItem.ShortCut) || !string.IsNullOrEmpty(newItem.Name))
                {
                    App.Config.Settings.ShortCuts.Insert(0, newItem);
                    App.Config.RefreshPinyinCache(newItem);   // 新增项同样补齐小写缓存，保证立刻可被新名称/新快捷键搜到
                    App.Config.Save();
                    RefreshGrid(TxtFilter.Text);
                    SelectItemByReference(newItem);
                    WpfMessageBox.Show(AppStrings.AddedMessage, AppStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        public void BtnAdd_Click_External()
        {
            BtnAdd_Click(this, new RoutedEventArgs());
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new ShortCutItem
            {
                ShortCut = "new",
                Name = AppStrings.NewShortCutName,
                CommandLine = "",
                Freq = 0
            };

            App.Config.Settings.ShortCuts.Insert(0, newItem);
            App.Config.RefreshPinyinCache(newItem);
            App.Config.Save();
            RefreshGrid(TxtFilter.Text);

            SelectItemByReference(newItem, focusEditor: true);
        }

        private void BtnScanApps_Click(object sender, RoutedEventArgs e)
        {
            var scanWin = new ScanAppsWindow
            {
                Owner = this
            };

            if (scanWin.ShowDialog() == true)
            {
                RefreshGrid(TxtFilter.Text);
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            // 用 _selectedItem（按引用核对仍在当前视图列表中）而非 ShortcutGrid.SelectedItem：
            // SelectedItem 在选中状态异常时可能指向残留对象，按引用核对可避免误删。
            var item = _selectedItem;
            if (item != null && _viewList.Any(x => ReferenceEquals(x, item)))
            {
                if (WpfMessageBox.Show(string.Format(AppStrings.DeleteConfirmFormat, item.Name), AppStrings.DeleteConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    App.Config.Settings.ShortCuts.Remove(item);
                    App.Config.Save();
                    RefreshGrid(TxtFilter.Text);
                }
            }
        }

        /// <summary>
        /// 列表查重：按目标路径（CommandLine）归一化分组。
        /// 每组以频次最高的项为主项，其余项按频次从高到低把快捷词用“-”接到主项快捷词后，
        /// 然后删除重复项、刷新拼音缓存并保存。执行前先弹出预览，用户确认后才写入。
        /// </summary>
        private void BtnDedup_Click(object sender, RoutedEventArgs e)
        {
            var all = App.Config.Settings.ShortCuts;

            // 1. 按归一化目标路径分组（忽略大小写与首尾空格），跳过空目标
            var groups = all
                .Where(x => !string.IsNullOrWhiteSpace(x.CommandLine))
                .GroupBy(x => NormalizeTarget(x.CommandLine), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (groups.Count == 0)
            {
                WpfMessageBox.Show(AppStrings.DedupNoDuplicateBody, AppStrings.DedupNoDuplicate,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. 计算每组合并方案：Freq 最高为主，其余快捷词按 Freq 降序用“-”连接
            var plans = new List<(List<ShortCutItem> Group, ShortCutItem Master, string MergedShortCut, int RemovedCount)>();
            foreach (var group in groups)
            {
                var ordered = group
                    .OrderByDescending(x => x.Freq)
                    .ThenBy(x => x.ShortCut, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var master = ordered[0];

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(master.ShortCut)) parts.Add(master.ShortCut.Trim());
                foreach (var dup in ordered.Skip(1))
                {
                    string sc = (dup.ShortCut ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(sc) &&
                        !parts.Any(p => string.Equals(p, sc, StringComparison.OrdinalIgnoreCase)))
                    {
                        parts.Add(sc);
                    }
                }
                string merged = string.Join("-", parts);
                plans.Add((group.ToList(), master, merged, group.Count() - 1));
            }

            int totalRemoved = plans.Sum(p => p.RemovedCount);

            // 3. 预览：列出每组的原目标与合并后的快捷词
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(AppStrings.DedupConfirmHeader, plans.Count, totalRemoved));
            sb.AppendLine();
            foreach (var plan in plans)
            {
                string target = plan.Master.CommandLine ?? string.Empty;
                if (target.Length > 60) target = target.Substring(0, 60) + "…";
                sb.AppendLine(string.Format(AppStrings.DedupMergeLine, target, plan.MergedShortCut, plan.Master.Freq));
            }
            sb.AppendLine();
            sb.Append(AppStrings.DedupConfirmYes + " / " + AppStrings.DedupConfirmNo + "?");

            var result = WpfMessageBox.Show(this, sb.ToString(), AppStrings.DedupConfirmTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;

            // 4. 执行合并：主项快捷词更新为合并串，Freq 取组内最大；删除其余重复项
            var toRemove = new HashSet<ShortCutItem>();
            foreach (var plan in plans)
            {
                plan.Master.ShortCut = plan.MergedShortCut;
                int maxFreq = plan.Group.Max(x => x.Freq);
                plan.Master.Freq = maxFreq;
                foreach (var dup in plan.Group)
                {
                    if (!ReferenceEquals(dup, plan.Master)) toRemove.Add(dup);
                }
            }
            foreach (var item in toRemove)
            {
                all.Remove(item);
            }

            App.Config.RefreshPinyinCache();
            App.Config.Save();
            RefreshGrid(TxtFilter.Text);

            WpfMessageBox.Show(this, string.Format(AppStrings.DedupDoneFormat, plans.Count, totalRemoved),
                AppStrings.DedupDoneTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>目标路径归一化：去首尾空白、统一大小写由调用方 GroupBy 比较器处理。</summary>
        private static string NormalizeTarget(string commandLine)
        {
            return (commandLine ?? string.Empty).Trim().TrimEnd('\\', '/');
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Filter = AppStrings.LegacyImportFilter,
                Title = AppStrings.SelectLegacyTitle
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var imported = App.Config.ImportFromLegacyFile(dialog.FileName);
                    if (imported.Count > 0)
                    {
                        foreach (var newItem in imported)
                        {
                            // 手工编辑过的配置可能出现 null 字段，旧写法 x.ShortCut.Equals(...) 会抛空引用 → "导入失败"
                            var existing = App.Config.Settings.ShortCuts.FirstOrDefault(x =>
                                string.Equals(x.ShortCut, newItem.ShortCut, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(x.Name, newItem.Name, StringComparison.OrdinalIgnoreCase));

                            if (existing != null)
                            {
                                existing.CommandLine = newItem.CommandLine;
                                existing.Freq = Math.Max(existing.Freq, newItem.Freq);
                            }
                            else
                            {
                                App.Config.Settings.ShortCuts.Add(newItem);
                            }
                        }

                        App.Config.RefreshPinyinCache();
                        App.Config.Save();
                        RefreshGrid(TxtFilter.Text);

                        WpfMessageBox.Show(string.Format(AppStrings.ImportSuccessFormat, imported.Count), AppStrings.ImportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        WpfMessageBox.Show(AppStrings.ImportEmpty, AppStrings.MsgBoxTitleHint, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(AppStrings.ImportFailPrefix + ex.Message, AppStrings.MsgBoxTitleError, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfSaveFileDialog
            {
                Filter = AppStrings.LegacyExportFilter,
                FileName = "ShortCutList.txt",
                Title = AppStrings.ExportLegacyTitle
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    App.Config.ExportToLegacyFile(dialog.FileName);
                    WpfMessageBox.Show(string.Format(AppStrings.ExportSuccessFormat, dialog.FileName), AppStrings.ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(AppStrings.ExportFailPrefix + ex.Message, AppStrings.MsgBoxTitleError, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var btn = BtnCheckUpdate;
            // 按钮 Content 在 XAML 里是带图标和文字的 StackPanel 对象。
            // 必须保存对象引用本身：用 ToString() 会存下 "System.Windows.Controls.StackPanel"
            // 这个类型名字符串，弹窗结束后恢复的就是这串文字，按钮内容被永久破坏。
            object? originalContent = btn.Content;
            btn.IsEnabled = false;
            btn.Content = AppStrings.UpdateBtnChecking;

            try
            {
                var info = await UpdateCheckerService.CheckForUpdatesAsync();

                if (info.HasUpdate)
                {
                    string msg = string.Format(AppStrings.UpdateNewVersionBodyFormat, info.CurrentVersion, info.LatestVersion);

                    var result = WpfMessageBox.Show(this, msg, AppStrings.UpdateTitleSoftware, MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        UpdateCheckerService.OpenUrl(info.ReleaseUrl);
                    }
                }
                else
                {
                    string statusDesc = string.IsNullOrEmpty(info.ErrorMessage)
                        ? AppStrings.UpdateLatest
                        : AppStrings.UpdateNoticePrefix + info.ErrorMessage;

                    string msg = string.Format(AppStrings.UpdateLatestWithNoticeBodyFormat,
                        info.CurrentVersion, UpdateCheckerService.RepoUrl, statusDesc);

                    var result = WpfMessageBox.Show(this, msg, AppStrings.UpdateTitleVersion, MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        UpdateCheckerService.OpenUrl(UpdateCheckerService.RepoUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, AppStrings.UpdateErrorPrefix + ex.Message, AppStrings.MsgBoxTitleHint, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = originalContent;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}
