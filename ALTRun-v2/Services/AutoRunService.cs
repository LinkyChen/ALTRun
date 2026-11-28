using System;
using System.IO;
using Microsoft.Win32;

namespace ALTRun.Services
{
    public static class AutoRunService
    {
        private const string AppName = "ALTRun";
        private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static string StartupShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{AppName}.lnk");

        /// <summary>
        /// 开机静默启动的关键：历史上启动文件夹里可能存在 ALTRun.lnk / ALTRun.exe.lnk 等
        /// 无 --silent 参数的旧快捷方式。它们开机时会再拉起一个实例，触发单实例唤醒事件，
        /// 把已经静默驻留托盘的主界面弹出来。这里统一按文件名前缀清理所有 ALTRun*.lnk。
        /// </summary>
        private static void CleanLegacyStartupShortcuts()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (string.IsNullOrEmpty(startupFolder) || !Directory.Exists(startupFolder)) return;

                foreach (string lnk in Directory.GetFiles(startupFolder, $"{AppName}*.lnk"))
                {
                    File.Delete(lnk);
                    AppLog.Write($"[ALTRun] 已清理开机自启动残留快捷方式: {lnk}");
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] AutoRunService.CleanLegacy 静默异常: {ex.Message}");
            }
        }

        public static void ApplyAutoRun(bool enable)
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            // 1. 注册表启动项 (HKCU\Software\Microsoft\Windows\CurrentVersion\Run)
            // 规范化添加 --silent 参数，确保开机静默常驻托盘，即使被系统多次拉起也不会误唤醒主界面
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    string desiredValue = $"\"{exePath}\" --silent";
                    string? currentValue = key.GetValue(AppName) as string;

                    if (enable)
                    {
                        // 值没变就不写：避免每次开机启动都做一次无谓的注册表写入（也会引起杀软/系统提示）
                        if (!string.Equals(currentValue, desiredValue, StringComparison.Ordinal))
                        {
                            key.SetValue(AppName, desiredValue);
                        }
                    }
                    else if (currentValue != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"[ALTRun] AutoRunService.cs:40 静默异常: {ex.Message}");
            }

            // 2. 清理历史遗留的 Windows 用户启动文件夹 (shell:startup) 快捷方式
            // 避免注册表与启动文件夹同时存在造成双重自启动冲突，也避免无 --silent 参数的
            // 旧 lnk 开机拉起第二实例、唤醒并弹出静默驻留的主界面。
            CleanLegacyStartupShortcuts();
        }

        public static bool CheckStatus()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
