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
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            // 程序内部清空输入（隐藏窗口时）不需要再搜一次
            if (_suspendSearch) return;

            // 防抖：重置定时器，连续输入只在最后一次停顿后触发一次搜索
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void PerformSearch()
        {
            // 立即执行时取消待触发的防抖，避免随后又重复搜索一次
            _searchDebounceTimer.Stop();

            string query = SearchBox.Text;
            _currentResult = SearchEngine.Search(query, App.Config.Settings.ShortCuts);

            ResultListBox.ItemsSource = _currentResult.Items;
            // 搜索结果重建后重置到第一项，并同步逻辑索引（SelectedIndex 会被布局重置，不能作为真相源）
            _selectedResultIndex = _currentResult.Items.Count > 0 ? 0 : -1;
            if (_selectedResultIndex >= 0)
            {
                ResultListBox.SelectedIndex = 0;
            }
            ResetListScrollToTop();
        }

        /// <summary>
        /// 统一设置当前候选项：同步逻辑索引与 ListBox 视觉选中，并把目标项滚入视野。
        /// 滚轮/上下键都通过本方法，确保“运行一次后再次呼出、SelectedIndex 被重置为 -1”时仍能连续切换。
        /// </summary>
        private void SetSelectedResult(int index)
        {
            int count = ResultListBox.Items.Count;
            if (count == 0)
            {
                _selectedResultIndex = -1;
                return;
            }
            index = Math.Max(0, Math.Min(count - 1, index));
            _selectedResultIndex = index;
            ResultListBox.SelectedIndex = index;
            ResultListBox.ScrollIntoView(ResultListBox.Items[index]);
        }


        /// <summary>
        /// 键盘执行类操作前调用：若还有防抖未落地，先同步执行一次，确保基于最新输入结果操作。
        /// </summary>
        private void FlushPendingSearch()
        {
            if (_searchDebounceTimer.IsEnabled)
            {
                PerformSearch();
            }
        }

        private void ResetListScrollToTop()
        {
            try
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(ResultListBox);
                scrollViewer?.ScrollToTop();
                if (_currentResult.Items.Count > 0 && ResultListBox.Items.Count > 0)
                {
                    ResultListBox.ScrollIntoView(_currentResult.Items[0]);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] MainWindow.xaml.cs:609 静默异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 鼠标滚轮：每格切换一个候选项。候选全部可见时列表本身不会滚动；
        /// 候选超出可视区时 ScrollIntoView 会把新选中项滚入视野。
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            FlushPendingSearch();
            if (ResultListBox.Items.Count == 0) return;

            int steps = Math.Max(1, Math.Abs(e.Delta) / 120);
            if (e.Delta > 0) steps = -steps; // 向上滚动选中上一项

            // 关键：以逻辑索引为起点，绝不读 ListBox.SelectedIndex。
            // 窗口 Hide/Show 或布局刷新会把 SelectedIndex 重置为 -1，若据此计算，
            // 每次向下滚都会得到 -1 + 1 = 0，表现就是“永远停在第 1 项”。
            int current = _selectedResultIndex;
            if (current < 0 || current >= ResultListBox.Items.Count)
            {
                // 兜底：逻辑索引失效时，向下滚从 -1 起步落到第 1 项，向上滚从末尾倒着选
                current = steps < 0 ? ResultListBox.Items.Count : -1;
            }
            int target = Math.Min(ResultListBox.Items.Count - 1, Math.Max(0, current + steps));

            if (target != current)
            {
                SetSelectedResult(target);
            }
            else
            {
                // 目标与逻辑索引一致时也要确保视觉选中被重新应用（SelectedIndex 可能已被重置）
                ResultListBox.SelectedIndex = target;
                ResultListBox.ScrollIntoView(ResultListBox.Items[target]);
            }
            e.Handled = true;
        }

        /// <summary>
        /// 取当前逻辑选中的候选项。绝不读 ListBox.SelectedItem —— 替换 ItemsSource 后，
        /// 它（与集合视图的 CurrentItem 同步时）会残留上一次的旧项对象，导致
        /// “候选项显示正常变化，但执行时永远运行同一项（如记事本）”。
        /// 唯一真相源是 _currentResult.Items + _selectedResultIndex。
        /// </summary>
        private ShortCutItem? GetSelectedResultItem()
        {
            if (_selectedResultIndex < 0 || _selectedResultIndex >= _currentResult.Items.Count)
                return null;
            return _currentResult.Items[_selectedResultIndex];
        }

        private void ExecuteSelectedItem(bool runAsAdmin)
        {
            if (GetSelectedResultItem() is ShortCutItem item)
            {
                string param = _currentResult.ExtractedParam;
                bool success = CommandExecutor.Execute(item, param, runAsAdmin, out string? errorMessage);

                if (success)
                {
                    // 搜索结果是 Clone 副本，Freq 自增发生在副本上；按 Id 同步回原始配置项才能持久化
                    var original = App.Config.Settings.ShortCuts.FirstOrDefault(x => x.Id == item.Id);
                    if (original != null)
                    {
                        original.Freq = item.Freq;
                    }
                    App.Config.MarkDirty();
                    HideWindow();
                }
                else if (!string.IsNullOrEmpty(errorMessage))
                {
                    ShowMissingFileTip(errorMessage, item);
                }
            }
        }

    }
}
