using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 冲突对话框 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class ConflictDialogViewModel : ObservableObject
    {
        private readonly OutputViewModel _outputViewModel;

        [ObservableProperty]
        private GitProject? _conflictProject;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConflictFileList))]
        private List<string>? _conflictFiles;

        public string ConflictFileList => ConflictFiles != null
            ? string.Join("\n", ConflictFiles.Select(f => $"  - {f}"))
            : string.Empty;

        [ObservableProperty]
        private bool _showDialog;

        public ConflictDialogViewModel(OutputViewModel outputViewModel)
        {
            _outputViewModel = outputViewModel;
        }

        [RelayCommand]
        private void ResolveConflictOpenVS(object? parameter)
        {
            if (ConflictProject == null) return;

            try
            {
                var targetPath = !string.IsNullOrEmpty(ConflictProject.SolutionPath)
                    ? ConflictProject.SolutionPath
                    : ConflictProject.ProjectFilePath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{ConflictProject.Path}\"",
                        UseShellExecute = true
                    });
                    _outputViewModel.AppendLog($"[{ConflictProject.Name}] 打开文件夹解决冲突\n");
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "devenv.exe",
                        Arguments = $"\"{targetPath}\"",
                        UseShellExecute = true
                    });
                    _outputViewModel.AppendLog($"[{ConflictProject.Name}] 用 Visual Studio 打开解决冲突\n");
                }

                _outputViewModel.AppendLog($"[{ConflictProject.Name}] 请解决冲突后重新同步\n");
                Close();
            }
            catch (Exception ex)
            {
                _outputViewModel.AppendLog($"[{ConflictProject.Name}] 打开失败: {ex.Message}\n");
            }
        }

        [RelayCommand]
        private void ResolveConflictAbort()
        {
            // 关闭对话框，具体操作由 MainViewModel 处理
            Close();
        }

        [RelayCommand]
        private void ResolveConflictAutoStash()
        {
            // 关闭对话框，具体操作由 MainViewModel 处理
            Close();
        }

        public void Show(GitProject project, List<string> files)
        {
            ConflictProject = project;
            ConflictFiles = files;
            ShowDialog = true;
        }

        public void Close()
        {
            ShowDialog = false;
            ConflictProject = null;
            ConflictFiles = null;
        }
    }
}
