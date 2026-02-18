using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaikoDiveLauncher.Helpers
{
    /// <summary>
    /// Windows 11 Mica バックドロップを適用するためのヘルパークラス。
    /// DwmSetWindowAttribute P/Invoke を使用。
    /// </summary>
    public static class MicaHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // Backdrop types
        private const int DWMSBT_DISABLE = 1;
        private const int DWMSBT_MAINWINDOW = 2;  // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4;  // Mica Alt

        /// <summary>
        /// Windows 11 かどうかを判定する。
        /// </summary>
        public static bool IsWindows11OrLater
        {
            get
            {
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22000;
            }
        }

        /// <summary>
        /// ウィンドウに Mica バックドロップを適用する。
        /// </summary>
        /// <param name="window">適用先のウィンドウ</param>
        /// <param name="useDarkMode">ダークモードを有効にするか</param>
        /// <returns>適用に成功したかどうか</returns>
        public static bool ApplyMica(Window window, bool useDarkMode = true)
        {
            if (!IsWindows11OrLater) return false;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            // ダークモード設定
            int darkMode = useDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // Mica バックドロップ適用
            int backdropType = DWMSBT_MAINWINDOW;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            return result == 0; // S_OK
        }

        /// <summary>
        /// ウィンドウの Mica バックドロップを無効化する。
        /// </summary>
        /// <param name="window">対象のウィンドウ</param>
        public static void RemoveMica(Window window)
        {
            if (!IsWindows11OrLater) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int backdropType = DWMSBT_DISABLE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
    }
}
