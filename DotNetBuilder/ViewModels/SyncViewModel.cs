using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 同步视图模型 - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class SyncViewModel : ObservableObject
    {
        private readonly GitService _gitService;
        private readonly GitSyncService _gitSyncService;
        private readonly Action<string> _appendLog;
        private readonly Func<SyncOptions> _getSyncOptions;
        private readonly Func<IEnumerable<GitProject>> _getSelectedProjects;

        [ObservableProperty]
        private GitProject? _conflictProject;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConflictFileList))]
        private List<string>? _conflictFiles;

        [ObservableProperty]
        private bool _showConflictDialog;

        [ObservableProperty]
        private bool _isBusy;

        public string ConflictFileList => ConflictFiles != null
            ? string.Join("\n", ConflictFiles.Select(f => $"  - {f}"))
            : string.Empty;

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
        }

        [RelayCommand(CanExecute = nameof(CanSyncSelected))]
        private async Task SyncSelectedAsync()
        {
            var projects = SelectedProjects.ToList();
            if (!projects.Any())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AduSkin.Controls.AduMessageBox.Show("请先选择要同步的项目", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                });
                return;
            }

            IsBusy = true;
            _appendLog($"\n========== 开始同步 {projects.Count} 个项目 ==========\n");

            try
            {
                var tasks = projects.Select(async project =>
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            project.ErrorMessage = ex.Message;
                            project.IsExpanded = true;
                        });
                    }
                    finally
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => project.IsSyncing = false);
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
        private bool CanSyncSelected() => !IsBusy && SelectedProjects.Any();

        [RelayCommand]
        private async Task SyncSingleAsync(GitProject? project)
        {
            if (project == null)
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

        [RelayCommand]
        private void ResolveConflictOpenVS()
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

        [RelayCommand]
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

        [RelayCommand]
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
                    ConflictFiles = result.ConflictFiles;
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

        private async Task HandleSyncResultAsync(GitProject project, GitSyncResult result, IProgress<string> progress)
        {
            if (result.NeedsCommitMessage)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AduSkin.Controls.AduMessageBox.Show(
                        $"{project.Name} 有未提交的更改，请填写提交信息后重试。",
                        "需要提交信息",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);

                    project.IsExpanded = true;
                    project.ErrorMessage = "请填写提交信息";
                });
                return;
            }

            if (result.HasConflict)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ConflictProject = project;
                    ConflictFiles = result.ConflictFiles;
                    ShowConflictDialog = true;

                    project.ErrorMessage = result.ConflictMessage ?? "存在冲突";
                    project.IsExpanded = true;
                });
                return;
            }

            if (result.Success)
            {
                await _gitService.UpdateProjectStatusAsync(project);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    project.IsExpanded = false;
                });
                _appendLog($"[{project.Name}] 同步成功\n");
            }
            else
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    project.ErrorMessage = result.Message;
                    project.IsExpanded = true;
                });
                _appendLog($"[{project.Name}] 同步失败: {result.Message}\n");
            }
        }

        private IEnumerable<GitProject> SelectedProjects => _getSelectedProjects();
    }
}
