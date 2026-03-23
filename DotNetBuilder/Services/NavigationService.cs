namespace DotNetBuilder.Services
{
    /// <summary>
    /// 导航目标枚举
    /// </summary>
    public enum NavigationTarget
    {
        Welcome,
        ProjectList
    }

    /// <summary>
    /// 导航服务 - 处理导航和文件选择
    /// </summary>
    public class NavigationService
    {
        public event Action<NavigationTarget>? OnNavigateRequested;
        public event Action<string>? OnOpenProjectRequested;

        /// <summary>
        /// 导航到指定目标
        /// </summary>
        public void NavigateTo(NavigationTarget target)
        {
            OnNavigateRequested?.Invoke(target);
        }

        /// <summary>
        /// 请求打开项目文件
        /// </summary>
        public void RequestOpenProject(string filePath)
        {
            OnOpenProjectRequested?.Invoke(filePath);
        }

        /// <summary>
        /// 打开文件选择对话框
        /// </summary>
        /// <returns>选中的文件路径，未选择则返回 null</returns>
        public string? OpenFilePicker()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开 DotNetBuilder 项目",
                Filter = "DotNetBuilder 项目 (*.bdproj)|*.bdproj|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }

        /// <summary>
        /// 选择目录
        /// </summary>
        /// <returns>选中的目录路径，未选择则返回 null</returns>
        public string? SelectDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择包含 Git 项目的根目录"
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FolderName;
            }

            return null;
        }
    }
}
