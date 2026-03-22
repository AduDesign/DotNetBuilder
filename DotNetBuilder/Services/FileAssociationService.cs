using System.Diagnostics;
using Microsoft.Win32;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 文件关联服务 - 负责注册 .bdproj 文件关联
    /// </summary>
    public class FileAssociationService
    {
        private const string FileExtension = ".bdproj";
        private const string ProgId = "DotNetBuilder.Project";
        private const string FileTypeDescription = "DotNetBuilder 项目";

        private readonly string _appPath;

        public FileAssociationService()
        {
            _appPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }

        /// <summary>
        /// 注册文件关联
        /// </summary>
        public bool RegisterFileAssociation()
        {
            try
            {
                // 在 HKEY_CURRENT_USER 下注册，用户级别不需要管理员权限
                using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{FileExtension}");
                if (extensionKey == null) return false;
                extensionKey.SetValue("", ProgId);

                using var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
                if (progIdKey == null) return false;
                progIdKey.SetValue("", FileTypeDescription);

                // 设置默认图标
                using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
                iconKey?.SetValue("", $"\"{_appPath}\",0");

                // 设置打开命令
                using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue("", $"\"{_appPath}\" \"%1\"");

                // 通知系统注册表已更改
                NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取消注册文件关联
        /// </summary>
        public bool UnregisterFileAssociation()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{FileExtension}", false);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);

                NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查文件关联是否已注册
        /// </summary>
        public bool IsFileAssociationRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{FileExtension}");
                return key?.GetValue("") is string value && value == ProgId;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Windows API 方法
    /// </summary>
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
