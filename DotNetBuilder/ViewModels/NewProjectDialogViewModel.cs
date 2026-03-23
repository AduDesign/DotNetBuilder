using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input; 

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 新建项目对话框 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class NewProjectDialogViewModel : ObservableObject
    {
        private Action<string, string>? _onConfirmCallback;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConfirm))]
        private string _newProjectName = "新项目";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConfirm))]
        private string _newProjectRootPath = string.Empty;

        [ObservableProperty]
        private bool _showDialog;

        public bool CanConfirm => !string.IsNullOrWhiteSpace(NewProjectName) &&
                                  !string.IsNullOrWhiteSpace(NewProjectRootPath);
         

        public NewProjectDialogViewModel()
        {
        }

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
        [RelayCommand]
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
