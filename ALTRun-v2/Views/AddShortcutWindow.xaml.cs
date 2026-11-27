using System;
using System.IO;
using System.Windows;
using ALTRun.Models;
using ALTRun.Services;
using Microsoft.Win32;

namespace ALTRun.Views
{
    /// <summary>
    /// 右键"发送到 ALTRun"时弹出的添加快捷方式对话框。
    /// 字段与老版 Delphi TShortCutForm 对齐：快捷词 / 名称 / 命令行 / 四档参数类型。
    /// 主题使用与主界面相同的 DynamicResource，ThemeManager.ApplyTheme 统一着色。
    /// </summary>
    public partial class AddShortcutWindow : Window
    {
        public string ResultShortCut { get; private set; } = string.Empty;
        public string ResultName { get; private set; } = string.Empty;
        public string ResultCommandLine { get; private set; } = string.Empty;
        public ParamType ResultParamType { get; private set; } = ParamType.ptNone;

        public AddShortcutWindow(string shortCut, string name, string commandLine, ParamType paramType)
        {
            InitializeComponent();

            TxtShortCut.Text = shortCut ?? string.Empty;
            TxtName.Text = name ?? string.Empty;
            TxtCommandLine.Text = commandLine ?? string.Empty;
            SetParamType(paramType);

            try
            {
                ThemeManager.ApplyTheme(this, ThemeManager.GetTheme(
                    App.Config.Settings.Theme, App.Config.Settings.DarkMode));
            }
            catch (Exception ex)
            {
                AppLog.Write($"[SendTo] 添加对话框主题应用失败: {ex.Message}");
            }

            Loaded += (_, _) =>
            {
                TxtShortCut.Focus();
                TxtShortCut.SelectAll();
            };
        }

        private ParamType GetParamType()
        {
            if (RbNoEncoding.IsChecked == true) return ParamType.ptNoEncoding;
            if (RbUrlQuery.IsChecked == true) return ParamType.ptURLQuery;
            if (RbUtf8Query.IsChecked == true) return ParamType.ptUTF8Query;
            return ParamType.ptNone;
        }

        private void SetParamType(ParamType type)
        {
            switch (type)
            {
                case ParamType.ptNoEncoding: RbNoEncoding.IsChecked = true; break;
                case ParamType.ptURLQuery: RbUrlQuery.IsChecked = true; break;
                case ParamType.ptUTF8Query: RbUtf8Query.IsChecked = true; break;
                default: RbNoParam.IsChecked = true; break;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = AppStrings.BtnBrowseFile,
                Filter = "所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                TxtCommandLine.Text = dialog.FileName;
                string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
                if (string.IsNullOrWhiteSpace(TxtShortCut.Text)) TxtShortCut.Text = baseName;
                if (string.IsNullOrWhiteSpace(TxtName.Text)) TxtName.Text = baseName;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtShortCut.Text.Trim();
            string name = TxtName.Text.Trim();
            string command = TxtCommandLine.Text.Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(command))
            {
                MessageBox.Show(this, AppStrings.SendToBlankLine, AppStrings.MsgBoxTitleHint,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var paramType = GetParamType();

            // 对齐老版：选了"无参数启动"却写了 %p/%c 占位符时，提示用户确认参数类型
            if (paramType == ParamType.ptNone &&
                (command.Contains("%p", StringComparison.OrdinalIgnoreCase) ||
                 command.Contains("%c", StringComparison.OrdinalIgnoreCase)))
            {
                var answer = MessageBox.Show(this, AppStrings.ParamTypeMismatch,
                    AppStrings.MsgBoxTitleHint, MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Yes)
                {
                    RbNoEncoding.IsChecked = true;
                    RbNoEncoding.Focus();
                    return;
                }
            }

            ResultShortCut = key;
            ResultName = name;
            ResultCommandLine = command;
            ResultParamType = paramType;
            DialogResult = true;
        }
    }
}
