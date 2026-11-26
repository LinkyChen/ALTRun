using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ALTRun.Models;
using ALTRun.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace ALTRun.Views
{
    public partial class WelcomeWizardWindow : Window
    {
        public WelcomeWizardWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void BtnAutoImport_Click(object sender, RoutedEventArgs e)
        {
            PnlButtons.IsEnabled = false;
            PnlScanning.Visibility = Visibility.Visible;

            // UI 线程先取快照，后台只做磁盘扫描；共享配置的写入、缓存刷新与保存全部回到 UI 线程，
            // 避免后台直接替换列表/改缓存与搜索遍历并发。
            var existing = App.Config.Settings.ShortCuts.ToList();
            var additions = await Task.Run(() =>
            {
                var scanned = AppScannerService.ScanInstalledApps(existing);
                var existingPaths = new HashSet<string>(existing.Select(x => (x.CommandLine ?? "").Trim().Trim('\"')), StringComparer.OrdinalIgnoreCase);
                var newItems = new List<ShortCutItem>();

                foreach (var item in scanned)
                {
                    if (existingPaths.Contains(item.TargetPath)) continue;

                    newItems.Add(new ShortCutItem
                    {
                        Name = item.Name,
                        ShortCut = (item.ShortCut ?? "").ToLowerInvariant(),
                        CommandLine = item.TargetPath,
                        WorkingDir = string.IsNullOrWhiteSpace(item.WorkingDir) ? Path.GetDirectoryName(item.TargetPath) ?? "" : item.WorkingDir,
                        Freq = 0
                    });
                    existingPaths.Add(item.TargetPath);
                }

                return newItems;
            });

            int count = additions.Count;
            existing.AddRange(additions);
            App.Config.Settings.ShortCuts = existing;
            foreach (var item in additions)
            {
                App.Config.RefreshPinyinCache(item);
            }
            App.Config.Save();

            PnlScanning.Visibility = Visibility.Collapsed;

            WpfMessageBox.Show(string.Format(AppStrings.WizardImportSuccessFormat, count) + AppStrings.WizardReadySuffix, AppStrings.WizardReadyTitle, MessageBoxButton.OK, MessageBoxImage.Information);

            SafeClose(true);
        }

        private void BtnCustomSelect_Click(object sender, RoutedEventArgs e)
        {
            var scanWin = new ScanAppsWindow
            {
                Owner = this
            };

            bool? res = scanWin.ShowDialog();
            if (res == true)
            {
                SafeClose(true);
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            SafeClose(false);
        }

        private void SafeClose(bool? dialogResult = null)
        {
            try
            {
                DialogResult = dialogResult;
            }
            catch
            {
                Close();
            }
        }
    }
}
