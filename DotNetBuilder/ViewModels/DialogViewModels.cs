using System.Windows.Input;
using DotNetBuilder.Models;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 冲突对话框 ViewModel
    /// </summary>
    public class ConflictDialogViewModel : ViewModelBase
    {
        private GitProject? _conflictProject;
        private List<string>? _conflictFiles;
        private bool _showDialog;

        private Action<string>? _appendLog;

        public ConflictDialogViewModel()
        {
            ResolveConflictOpenVSCommand = new RelayCommand(ResolveConflictOpenVS);
            ResolveConflictAbortCommand = new RelayCommand(_ => ResolveConflictAbort());
            ResolveConflictAutoStashCommand = new RelayCommand(_ => ResolveConflictAutoStash());
        }

        public void SetAppendLog(Action<string> appendLog)
        {
            _appendLog = appendLog;
        }

        public GitProject? ConflictProject
        {
            get => _conflictProject;
            set => SetProperty(ref _conflictProject, value);
        }

        public List<string>? ConflictFiles
        {
            get => _conflictFiles;
            set
            {
                if (SetProperty(ref _conflictFiles, value))
                {
                    OnPropertyChanged(nameof(ConflictFileList));
                }
            }
        }

        public string ConflictFileList => ConflictFiles != null
            ? string.Join("\n", ConflictFiles.Select(f => $"  - {f}"))
            : string.Empty;

        public bool ShowDialog
        {
            get => _showDialog;
            set => SetProperty(ref _showDialog, value);
        }

        public ICommand ResolveConflictOpenVSCommand { get; }
        public ICommand ResolveConflictAbortCommand { get; }
        public ICommand ResolveConflictAutoStashCommand { get; }

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
                    _appendLog?.Invoke($"[{ConflictProject.Name}] 打开文件夹解决冲突\n");
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "devenv.exe",
                        Arguments = $"\"{targetPath}\"",
                        UseShellExecute = true
                    });
                    _appendLog?.Invoke($"[{ConflictProject.Name}] 用 Visual Studio 打开解决冲突\n");
                }

                _appendLog?.Invoke($"[{ConflictProject.Name}] 请解决冲突后重新同步\n");
                Close();
            }
            catch (Exception ex)
            {
                _appendLog?.Invoke($"[{ConflictProject.Name}] 打开失败: {ex.Message}\n");
            }
        }

        private void ResolveConflictAbort()
        {
            if (ConflictProject == null) return;
            // 关闭对话框，具体操作由 MainViewModel 处理
            Close();
        }

        private void ResolveConflictAutoStash()
        {
            if (ConflictProject == null) return;
            // 关闭对话框，具体操作由 MainViewModel 处理
            Close();
        }
    } 
}
