using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MizuLauncher
{
    public static class ThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public static void ApplyAcrylic(Window window)
        {
            var interopHelper = new WindowInteropHelper(window);
            var hwnd = interopHelper.Handle;

            if (hwnd == IntPtr.Zero)
            {
                window.SourceInitialized += (s, e) => ApplyAcrylic(window);
                return;
            }

            // Set Acrylic effect
            // 1 = DWMSBT_AUTO (System decide)
            // 2 = DWMSBT_MAINWINDOW (Mica)
            // 3 = DWMSBT_TRANSIENTWINDOW (Mica Alt)
            // 4 = DWMSBT_TABBEDWINDOW (Acrylic)
            int backdropType = 4; // Acrylic
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // Optional: Enable dark mode title bar
            int trueValue = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
        }
    }
}
