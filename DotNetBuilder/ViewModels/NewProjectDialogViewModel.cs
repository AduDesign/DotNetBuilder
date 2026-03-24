using AduSkin.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 新建项目对话框 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class NewProjectDialogViewModel : ObservableObject
    {
        private Action<string, string>? _onConfirmCallback;

        [ObservableProperty] 
        [NotifyPropertyChangedFor(nameof(CanProceedStepProjectName))]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string _newProjectName = "新项目";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanProceedStepProjectPath))]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string _newProjectRootPath = string.Empty;
        partial void OnNewProjectRootPathChanged(string? oldValue, string newValue)
        {
            if (File.Exists(NewProjectRootPath))
            {
                AduMessageBox.Show("项目文件已存在，请修改项目名称或者换个目录！");
            }
        }

        [ObservableProperty]
        private bool _showDialog;

        public bool CanConfirm() => !string.IsNullOrWhiteSpace(NewProjectName) &&
                                  !string.IsNullOrWhiteSpace(NewProjectRootPath);

        /// <summary>
        /// 校验项目名
        /// </summary>
        public bool CanProceedStepProjectName => !string.IsNullOrEmpty(NewProjectName) && NewProjectName.Length >= 0;

        /// <summary>
        /// 校验路径
        /// </summary>
        public bool CanProceedStepProjectPath => !string.IsNullOrEmpty(NewProjectRootPath) && NewProjectRootPath.Length >= 0 && !File.Exists(NewProjectRootPath);

        [RelayCommand]
        private void Browse()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择包含 Git 项目的根目录"
            };

            if (dialog.ShowDialog() == true)
                NewProjectRootPath = dialog.FolderName;
        }
        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            ShowDialog = false;
            _onConfirmCallback?.Invoke(NewProjectName, NewProjectRootPath);
        }

        [RelayCommand]
        private void Cancel()
        {
            ShowDialog = false;
            NewProjectName = "新项目";
            NewProjectRootPath = string.Empty;
        }

        /// <summary>
        /// 设置确认回调
        /// </summary>
        public void SetOnConfirmCallback(Action<string, string> callback)
        {
            _onConfirmCallback = callback;
        }

        public void Show()
        {
            NewProjectName = "新项目";
            NewProjectRootPath = string.Empty;
            ShowDialog = true;
        }
    }
}
