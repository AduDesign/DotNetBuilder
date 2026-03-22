using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 同步视图模型 - 负责 Git 同步相关的 UI 状态和命令
    /// </summary>
    public class SyncViewModel : ViewModelBase
    {
        private readonly GitService _gitService;
        private readonly GitSyncService _gitSyncService;
        private readonly Action<string> _appendLog;
        private readonly Func<SyncOptions> _getSyncOptions;
        private readonly Func<IEnumerable<GitProject>> _getSelectedProjects;

        // 冲突状态
        private GitProject? _conflictProject;
        private List<string>? _conflictFiles;
        private bool _showConflictDialog;

        public SyncViewModel(
            GitService gitService,
            GitSyncService gitSyncService,
            Action<string> appendLog,
            Func<SyncOptions> getSyncOptions,
            Func<IEnumerable<GitProject>> getSelectedProjects)
        {
            _gitService = gitService;
            _gitSyncService = gitSyncService;
            _appendLog = appendLog;
            _getSyncOptions = getSyncOptions;
            _getSelectedProjects = getSelectedProjects;

            // 命令
            SyncSelectedCommand = new AsyncRelayCommand(SyncSelectedAsync, () => !IsBusy && _getSelectedProjects().Any());
            SyncSingleCommand = new AsyncRelayCommand(SyncSingleAsync);
            ResolveConflictOpenVSCommand = new RelayCommand(ResolveConflictOpenVS);
            ResolveConflictAbortCommand = new AsyncRelayCommand(ResolveConflictAbortAsync);
            ResolveConflictAutoStashCommand = new AsyncRelayCommand(ResolveConflictAutoStashAsync);
        }

        private IEnumerable<GitProject> SelectedProjects => _getSelectedProjects();

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        // 冲突项目属性
        public GitProject? ConflictProject
        {
            get => _conflictProject;
            set => SetProperty(ref _conflictProject, value);
        }

        public List<string>? ConflictFiles
        {
            get => _conflictFiles;
            set => SetProperty(ref _conflictFiles, value);
        }

        public bool ShowConflictDialog
        {
            get => _showConflictDialog;
            set => SetProperty(ref _showConflictDialog, value);
        }

        public string ConflictFileList => ConflictFiles != null
            ? string.Join("\n", ConflictFiles.Select(f => $"  - {f}"))
            : string.Empty;

        #region Commands

        public ICommand SyncSelectedCommand { get; }
        public ICommand SyncSingleCommand { get; }
        public ICommand ResolveConflictOpenVSCommand { get; }
        public ICommand ResolveConflictAbortCommand { get; }
        public ICommand ResolveConflictAutoStashCommand { get; }

        #endregion

        #region Sync Methods

        private async Task SyncSelectedAsync()
        {
            var projects = SelectedProjects.ToList();
            if (!projects.Any())
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AduMessageBox.Show("请先选择要同步的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return;
            }

            IsBusy = true;
            _appendLog($"\n========== 开始同步 {projects.Count} 个项目 ==========\n");

            try
            {
                var tasks = projects.Select(async project =>
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        project.ClearError();
                        project.IsSyncing = true;
                        project.IsExpanded = true;
                    });

                    try
                    {
                        var progress = new Progress<string>(msg => _appendLog($"[{project.Name}] {msg}\n"));
                        var options = _getSyncOptions();

                        await _gitService.UpdateProjectStatusAsync(project);
                        var result = await _gitSyncService.SyncProjectAsync(
                            project,
                            project.CommitMessage,
                            options,
                            progress);

                        await HandleSyncResultAsync(project, result, progress);
                    }
                    catch (Exception ex)
                    {
                        _appendLog($"[{project.Name}] 同步异常: {ex.Message}\n");
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            project.ErrorMessage = ex.Message;
                            project.IsExpanded = true;
                        });
                    }
                    finally
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => project.IsSyncing = false);
                    }
                });

                await Task.WhenAll(tasks);
                _appendLog($"\n========== 同步完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SyncSingleAsync(object? parameter)
        {
            if (parameter is not GitProject project)
                return;

            _appendLog($"\n========== 同步项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsSyncing = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg => _appendLog($"[{project.Name}] {msg}\n"));
                var options = _getSyncOptions();

                await _gitService.UpdateProjectStatusAsync(project);
                var result = await _gitSyncService.SyncProjectAsync(
                    project,
                    project.CommitMessage,
                    options,
                    progress);

                await HandleSyncResultAsync(project, result, progress);
            }
            catch (Exception ex)
            {
                _appendLog($"[{project.Name}] 同步异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsSyncing = false;
            }
        }

        private async Task HandleSyncResultAsync(GitProject project, GitSyncResult result, IProgress<string> progress)
        {
            if (result.NeedsCommitMessage)
            {
                // 需要用户输入提交信息 - 显示提示
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AduMessageBox.Show(
                        $"{project.Name} 有未提交的更改，请填写提交信息后重试。",
                        "需要提交信息",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // 自动展开项目，让用户输入提交信息
                    project.IsExpanded = true;
                    project.ErrorMessage = "请填写提交信息";
                });
                return;
            }

            if (result.HasConflict)
            {
                // 有冲突，显示冲突对话框
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ConflictProject = project;
                    ConflictFiles = result.ConflictFiles;
                    OnPropertyChanged(nameof(ConflictFileList));
                    ShowConflictDialog = true;

                    project.ErrorMessage = result.ConflictMessage ?? "存在冲突";
                    project.IsExpanded = true;
                });
                return;
            }

            if (result.Success)
            {
                await _gitService.UpdateProjectStatusAsync(project);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    project.IsExpanded = false;
                });
                _appendLog($"[{project.Name}] 同步成功\n");
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    project.ErrorMessage = result.Message;
                    project.IsExpanded = true;
                });
                _appendLog($"[{project.Name}] 同步失败: {result.Message}\n");
            }
        }

        #endregion

        #region Conflict Resolution

        private void ResolveConflictOpenVS(object? parameter)
        {
            if (ConflictProject == null)
                return;

            try
            {
                var targetPath = !string.IsNullOrEmpty(ConflictProject.SolutionPath)
                    ? ConflictProject.SolutionPath
                    : ConflictProject.ProjectFilePath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    // 没有解决方案/项目文件，打开文件夹
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{ConflictProject.Path}\"",
                        UseShellExecute = true
                    });
                    _appendLog($"[{ConflictProject.Name}] 打开文件夹解决冲突\n");
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "devenv.exe",
                        Arguments = $"\"{targetPath}\"",
                        UseShellExecute = true
                    });
                    _appendLog($"[{ConflictProject.Name}] 用 Visual Studio 打开解决冲突\n");
                }

                ShowConflictDialog = false;
                _appendLog($"[{ConflictProject.Name}] 请解决冲突后重新同步\n");
            }
            catch (Exception ex)
            {
                _appendLog($"[{ConflictProject.Name}] 打开失败: {ex.Message}\n");
            }
        }

        private async Task ResolveConflictAbortAsync()
        {
            if (ConflictProject == null)
                return;

            var project = ConflictProject;
            _appendLog($"[{project.Name}] 放弃 pull，保留本地状态...\n");

            try
            {
                var progress = new Progress<string>(msg => _appendLog($"[{project.Name}] {msg}\n"));
                await _gitSyncService.HandleConflictAsync(project, ConflictAction.Abort, progress);

                project.ClearError();
                project.IsExpanded = false;
                _appendLog($"[{project.Name}] 已放弃 pull\n");
            }
            catch (Exception ex)
            {
                _appendLog($"[{project.Name}] 操作失败: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                ShowConflictDialog = false;
                ConflictProject = null;
                ConflictFiles = null;
            }
        }

        private async Task ResolveConflictAutoStashAsync()
        {
            if (ConflictProject == null)
                return;

            var project = ConflictProject;
            _appendLog($"[{project.Name}] 尝试自动 stash 后 pull...\n");

            try
            {
                var progress = new Progress<string>(msg => _appendLog($"[{project.Name}] {msg}\n"));
                var result = await _gitSyncService.HandleConflictAsync(project, ConflictAction.AutoStash, progress);

                if (result.HasConflict)
                {
                    // 仍有冲突
                    ConflictFiles = result.ConflictFiles;
                    OnPropertyChanged(nameof(ConflictFileList));
                    project.ErrorMessage = result.ConflictMessage ?? "Stash pop 时产生冲突";
                    _appendLog($"[{project.Name}] Stash pop 时产生冲突，请手动解决\n");
                }
                else
                {
                    await _gitService.UpdateProjectStatusAsync(project);
                    project.ClearError();
                    project.IsExpanded = false;
                    _appendLog($"[{project.Name}] 自动处理成功\n");
                }
            }
            catch (Exception ex)
            {
                _appendLog($"[{project.Name}] 自动处理失败: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                if (!ShowConflictDialog)
                {
                    ConflictProject = null;
                    ConflictFiles = null;
                }
            }
        }

        #endregion
    }
}
