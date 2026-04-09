using System.Collections.ObjectModel;
using System.Windows;
using AduSkin.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 项目列表 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class ProjectListViewModel : ObservableObject
    {
        private readonly GitService _gitService;
        private readonly GitSyncService _gitSyncService;
        private readonly MSBuildService _msbuildService;
        private readonly OutputViewModel _outputViewModel;
        private readonly ConflictDialogViewModel _conflictViewModel;

        [ObservableProperty]
        private bool _isBusy; 

        [ObservableProperty]
        private GitProject? _selectedProject;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private bool? _isSelectedAll;

        public OutputViewModel OutputViewModel => _outputViewModel;

        // 全局同步选项（供 XAML 直接绑定）
        public ObservableCollection<PullStrategy> PullStrategies { get; } = new()
        {
            PullStrategy.Auto,
            PullStrategy.Merge,
            PullStrategy.Rebase,
            PullStrategy.CommitOnly,
            PullStrategy.SkipCommit
        };

        public ObservableCollection<ConflictAction> ConflictActions { get; } = new()
        {
            ConflictAction.Prompt,
            ConflictAction.AutoStash,
            ConflictAction.Abort
        };

        [ObservableProperty]
        private PullStrategy _globalPullStrategy = PullStrategy.Auto;

        [ObservableProperty]
        private ConflictAction _globalConflictAction = ConflictAction.Prompt;

        [ObservableProperty]
        private bool _globalAutoCommitWhenNoMessage = false;

        [ObservableProperty]
        private bool _globalPushOnSync = false;

        public ObservableCollection<GitProject> Projects { get; } = new();
        public ObservableCollection<MSBuildVersion> MSBuildVersions { get; } = new();
        public ObservableCollection<string> ConfigurationTypes { get; } = new() { "Debug", "Release" };
        public ObservableCollection<string> Executables { get; } = new();

        public IEnumerable<GitProject> SelectedProjects => Projects.Where(p => p.IsSelected);

        public ProjectListViewModel(
            GitService gitService,
            GitSyncService gitSyncService,
            MSBuildService msbuildService,
            OutputViewModel outputViewModel,
            ConflictDialogViewModel conflictViewModel)
        {
            _gitService = gitService;
            _gitSyncService = gitSyncService;
            _msbuildService = msbuildService;
            _outputViewModel = outputViewModel;
            _conflictViewModel = conflictViewModel;
        }

        private void AppendLog(string message) => _outputViewModel.AppendLog(message);

        partial void OnSelectedProjectChanged(GitProject? value)
        {
            LoadProjectExecutables();
        }

        partial void OnIsSelectedAllChanged(bool? value)
        {
            if (value == null) return;

            foreach (var item in Projects)
            {
                item.SetIsSelected(value.Value);
            }
        }

        [RelayCommand]
        private void MoveUp(GitProject? project)
        {
            if (project == null) return;

            var index = Projects.IndexOf(project);
            if (index > 0)
            {
                Projects.Move(index, index - 1);
                UpdateSortOrders();
            }
        }

        [RelayCommand]
        private void MoveDown(GitProject? project)
        {
            if (project == null) return;

            var index = Projects.IndexOf(project);
            if (index < Projects.Count - 1)
            {
                Projects.Move(index, index + 1);
                UpdateSortOrders();
            }
        }

        /// <summary>
        /// 将项目移动到指定索引位置（用于拖拽排序）
        /// </summary>
        public void MoveTo(GitProject project, int targetIndex)
        {
            if (project == null) return;

            var currentIndex = Projects.IndexOf(project);
            if (currentIndex < 0 || currentIndex == targetIndex) return;

            // 确保索引在有效范围内
            targetIndex = Math.Max(0, Math.Min(targetIndex, Projects.Count - 1));

            Projects.Move(currentIndex, targetIndex);
            UpdateSortOrders();
        }

        [RelayCommand]
        private void RemoveProject(GitProject? project)
        {
            if (project == null) return;

            project.IsSelected = false;
            project.IsRemoved = true;
            Projects.Remove(project);
            AppendLog($"已移除项目: {project.Name}\n");

            if (SelectedProject == project)
                SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
        }

        [RelayCommand]
        private void OpenFolder(GitProject? project)
        {
            if (project == null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{project.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"打开文件夹失败: {ex.Message}\n");
            }
        }

        [RelayCommand]
        private void OpenVS(GitProject? project)
        {
            if (project == null) return;

            try
            {
                var targetPath = !string.IsNullOrEmpty(project.SolutionPath)
                    ? project.SolutionPath
                    : project.ProjectFilePath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    AppendLog($"[{project.Name}] 未找到解决方案或项目文件\n");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "devenv.exe",
                    Arguments = $"\"{targetPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"用 VisualStudio 打开失败: {ex.Message}\n");
            }
        }

        [RelayCommand]
        private void OpenVSCode(GitProject? project)
        {
            if (project == null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "code",
                    Arguments = $"\"{project.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"用 VSCode 打开失败: {ex.Message}\n");
            }
        }

        [RelayCommand]
        private async Task RunSelectedAsync(GitProject? project)
        {
            GitProject? targetProject = project ?? SelectedProject;
            if (targetProject == null || string.IsNullOrEmpty(targetProject.ExecuteFile))
            {
                AppendLog("无可运行的项目\n");
                return;
            }

            AppendLog($"\n========== 运行项目: {targetProject.Name} ==========\n");

            try
            {
                var exePath = targetProject.ExecuteFile;
                var workingDir = System.IO.Path.GetDirectoryName(exePath) ?? targetProject.Path;

                AppendLog($"[{targetProject.Name}] 启动: {exePath}\n");

                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = workingDir,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                AppendLog($"[{targetProject.Name}] 运行失败: {ex.Message}\n");
            }
        }

        public void LoadMSBuildVersions(IEnumerable<MSBuildVersion> versions)
        {
            MSBuildVersions.Clear();
            foreach (var version in versions)
            {
                MSBuildVersions.Add(version);
            }
        }

        public void AddProject(GitProject project)
        {
            project.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(GitProject.IsSelected))
                {
                    CommitSelectedCommand.NotifyCanExecuteChanged();
                    PushSelectedCommand.NotifyCanExecuteChanged();
                    SyncSelectedCommand.NotifyCanExecuteChanged();
                    BuildSelectedCommand.NotifyCanExecuteChanged();
                    UpdateIsSelectedAll();
                }
            };
            Projects.Add(project);
            UpdateIsSelectedAll();
        }

        public void ClearProjects()
        {
            Projects.Clear();
            UpdateIsSelectedAll();
        }

        private void UpdateIsSelectedAll()
        {
            if (Projects.Count == 0)
            {
                IsSelectedAll = false;
                return;
            }

            var selectedCount = Projects.Count(p => p.IsSelected);
            if (selectedCount == Projects.Count)
                IsSelectedAll = true;
            else if (selectedCount > 0)
                IsSelectedAll = null;
            else
                IsSelectedAll = false;
        }

        private void UpdateSortOrders()
        {
            for (int i = 0; i < Projects.Count; i++)
            {
                Projects[i].SortOrder = i;
            }
        }

        private void LoadProjectExecutables()
        {
            Executables.Clear();

            if (SelectedProject == null) return;

            var exeFiles = _msbuildService.FindOutputExecutables(SelectedProject.Path);
            foreach (var exe in exeFiles)
            {
                Executables.Add(exe);
            }
        }

        public async Task ScanProjectsAsync(string path)
        {
            AppendLog($"正在扫描: {path}\n");

            try
            {
                var progress = new Progress<string>(msg => AppendLog(msg + "\n"));
                var projects = await _gitService.ScanGitProjectsAsync(path, progress);

                int sortOrder = 0;
                foreach (var project in projects)
                {
                    project.SolutionPath = _gitService.GetSolutionPath(project.Path);
                    project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
                    project.SortOrder = sortOrder++;

                    // 智能匹配 MSBuild 版本
                    var projectInfo = _gitService.ParseProjectFile(project.Path);
                    var matched = _msbuildService.MatchBestMSBuild(projectInfo, MSBuildVersions.ToList());
                    project.SelectedMSBuildVersion = matched ?? MSBuildVersions.FirstOrDefault();

                    AddProject(project);
                }

                SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));

                AppendLog($"\n扫描完成，发现 {projects.Count} 个Git项目\n");
                if (projects.Count > 0)
                {
                    var dotnetCount = projects.Count(p => p.IsDotNetProject);
                    AppendLog($"其中 {dotnetCount} 个是.NET项目\n");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"\n扫描失败: {ex.Message}\n");
            }
        }

        public async Task AddSingleProjectAsync(string projectPath)
        {
            var progress = new Progress<string>(msg => AppendLog(msg + "\n"));
            var project = await _gitService.AddGitProjectAsync(projectPath, progress);

            if (project == null)
            {
                AppendLog("添加失败: 所选目录不是Git项目目录\n");
                return;
            }

            project.SolutionPath = _gitService.GetSolutionPath(project.Path);
            project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
            project.SortOrder = Projects.Count;

            // 智能匹配 MSBuild 版本
            var projectInfo = _gitService.ParseProjectFile(project.Path);
            var matched = _msbuildService.MatchBestMSBuild(projectInfo, MSBuildVersions.ToList());
            project.SelectedMSBuildVersion = matched ?? MSBuildVersions.FirstOrDefault();

            AddProject(project);
            AppendLog($"已添加项目: {project.Name}\n");
        }

        /// <summary>
        /// 加载保存的项目配置（不重新扫描目录）
        /// </summary>
        public async Task LoadSavedProjectsAsync(IEnumerable<Models.ProjectConfig> savedProjects)
        {
            AppendLog("正在加载保存的项目配置...\n");

            var msbuildVersionsList = MSBuildVersions.ToList();

            foreach (var config in savedProjects)
            {
                // 检查路径是否存在
                if (!System.IO.Directory.Exists(config.Path))
                {
                    AppendLog($"路径不存在，跳过: {config.Path}\n");
                    continue;
                }

                // 创建 GitProject
                var project = await _gitService.AddGitProjectAsync(config.Path, null);
                if (project == null)
                {
                    AppendLog($"非Git项目目录，跳过: {config.Path}\n");
                    continue;
                }

                // 恢复保存的配置
                project.SolutionPath = _gitService.GetSolutionPath(project.Path);
                project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
                project.SortOrder = config.Order;
                project.Configuration = config.Configuration;
                project.ExecuteFile = config.ExecuteFile ?? string.Empty;
                project.PullStrategy = config.PullStrategy;
                project.ConflictAction = config.ConflictAction;
                project.AutoCommitWhenNoMessage = config.AutoCommitWhenNoMessage;
                project.OutputDirectory = config.OutputDirectory ?? string.Empty;
                project.SetIsSelected(config.IsSelected);

                // 恢复 MSBuild 版本
                if (!string.IsNullOrEmpty(config.SelectedMSBuildVersion))
                {
                    var msbuild = msbuildVersionsList.FirstOrDefault(v => v.DisplayName == config.SelectedMSBuildVersion);
                    if (msbuild != null)
                    {
                        project.SelectedMSBuildVersion = msbuild;
                    }
                    else
                    {
                        // 已保存版本在当前系统中未找到，尝试智能匹配
                        var projectInfo = _gitService.ParseProjectFile(project.Path);
                        var matched = _msbuildService.MatchBestMSBuild(projectInfo, msbuildVersionsList);
                        project.SelectedMSBuildVersion = matched ?? msbuildVersionsList.FirstOrDefault();
                    }
                }
                else if (msbuildVersionsList.Count > 0)
                {
                    var projectInfo = _gitService.ParseProjectFile(project.Path);
                    var matched = _msbuildService.MatchBestMSBuild(projectInfo, msbuildVersionsList);
                    project.SelectedMSBuildVersion = matched ?? msbuildVersionsList[0];
                }

                // 更新 Git 状态
                await _gitService.UpdateProjectStatusAsync(project);

                AddProject(project);
            }

            SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
            AppendLog($"已加载 {Projects.Count} 个项目\n");
        }

        #region 公共操作方法

        /// <summary>
        /// 执行单个项目的构建操作
        /// </summary>
        public async Task ExecuteBuildAsync(GitProject project, bool clearLog = true)
        {
            if (project.SelectedMSBuildVersion == null)
            {
                AppendLog($"[{project.Name}] 请先选择 MSBuild 版本\n");
                return;
            }

            if (clearLog)
                _outputViewModel.LogOutput = string.Empty;
            AppendLog($"\n========== 构建项目: {project.Name} ==========\n");

            project.ClearError();
            project.IsBuilding = true;
            project.IsExpanded = true;

            try
            {
                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                AppendLog($"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion.DisplayName}, 配置: {project.Configuration}\n");
                var result = await _msbuildService.BuildProjectAsync(project, project.Configuration, project.SelectedMSBuildVersion, progress);

                if (result.Success)
                {
                    project.IsExpanded = false;
                    AppendLog($"[{project.Name}] 构建成功\n");
                }
                else
                {
                    project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                    AppendLog($"[{project.Name}] 构建失败: {result.ErrorMessage}\n");
                }
                _outputViewModel.ForceFlush();
            }
            catch (Exception ex)
            {
                AppendLog($"[{project.Name}] 构建异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsBuilding = false;
            }
        }

        /// <summary>
        /// 执行单个项目的同步操作
        /// </summary>
        public async Task ExecuteSyncAsync(GitProject project)
        {
            AppendLog($"\n========== 同步项目: {project.Name} ==========\n");

            project.ClearError();
            project.IsSyncing = true;
            project.IsExpanded = true;

            try
            {
                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                var options = GetSyncOptions();

                await _gitService.UpdateProjectStatusAsync(project);
                var result = await _gitSyncService.SyncProjectAsync(project, project.CommitMessage, options, progress);

                await HandleSyncResultAsync(project, result, progress);
            }
            catch (Exception ex)
            {
                AppendLog($"[{project.Name}] 同步异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsSyncing = false;
            }
        }

        /// <summary>
        /// 执行单个项目的推送操作
        /// </summary>
        public async Task ExecutePushAsync(GitProject project)
        {
            AppendLog($"\n========== 推送项目: {project.Name} ==========\n");

            project.ClearError();
            project.IsSyncing = true;
            project.IsExpanded = true;

            try
            {
                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                var success = await _gitSyncService.PushProjectAsync(project, progress);

                if (success)
                {
                    AppendLog($"[{project.Name}] 推送成功\n");
                    project.IsExpanded = false;
                }
                else
                {
                    project.ErrorMessage = "推送失败";
                    AppendLog($"[{project.Name}] 推送失败\n");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[{project.Name}] 推送异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsSyncing = false;
            }
        }

        /// <summary>
        /// 执行单个项目的提交操作
        /// </summary>
        public async Task ExecuteCommitAsync(GitProject project)
        {
            AppendLog($"\n========== 提交项目: {project.Name} ==========\n");

            project.ClearError();
            project.IsCommitting = true;
            project.IsExpanded = true;

            try
            {
                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));

                var result = await _gitSyncService.CommitProjectAsync(
                    project,
                    project.CommitMessage,
                    project.AutoCommitWhenNoMessage,
                    progress);

                if (result.Success)
                {
                    if (result.HasCommit)
                    {
                        project.CommitMessage = "";
                        AppendLog($"[{project.Name}] 提交完成\n");
                    }
                    else
                    {
                        AppendLog($"[{project.Name}] {result.Message}\n");
                    }
                    project.IsExpanded = false;
                }
                else
                {
                    if (result.NeedsCommitMessage)
                    {
                        AppendLog($"[{project.Name}] 需要填写提交信息\n");
                        project.IsExpanded = true;
                    }
                    else
                    {
                        project.ErrorMessage = result.Message;
                        AppendLog($"[{project.Name}] 提交失败: {result.Message}\n");
                    }
                }

                await _gitService.UpdateProjectStatusAsync(project);
            }
            catch (Exception ex)
            {
                AppendLog($"[{project.Name}] 提交异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsCommitting = false;
            }
        }

        #endregion

        #region 单项目命令

        [RelayCommand]
        private async Task BuildSingleAsync(GitProject? project)
        {
            if (project == null) return;
            await ExecuteBuildAsync(project);
        }

        [RelayCommand]
        private async Task SyncSingleAsync(GitProject? project)
        {
            if (project == null) return;
            await ExecuteSyncAsync(project);
        }

        [RelayCommand]
        private async Task PushProjectAsync(GitProject? project)
        {
            if (project == null) return;
            await ExecutePushAsync(project);
        }

        [RelayCommand]
        private async Task CommitSingleAsync(GitProject? project)
        {
            if (project == null) return;
            await ExecuteCommitAsync(project);
        }

        [RelayCommand]
        private async Task RefreshAsync(GitProject? project)
        {
            if (project == null) return;

            try
            {
                // 更新本地更改状态
                await _gitService.UpdateProjectStatusAsync(project);

                // 获取远程状态（未推送的提交数量）
                var remoteStatus = await _gitSyncService.GetRemoteStatusAsync(project);
                project.RemoteStatus = remoteStatus;

                AppendLog($"[{project.Name}] 状态已刷新");
            }
            catch (Exception ex)
            {
                AppendLog($"[{project.Name}] 刷新状态失败: {ex.Message}\n");
            }
        }

        [RelayCommand]
        private void Pack(GitProject? project)
        {
            GitProject? targetProject = project ?? SelectedProject;
            if (targetProject == null) return;

            var packWindow = new Views.PackWindow();
            packWindow.Initialize(
                targetProject.Name,
               targetProject.OutputDirectory,
                string.IsNullOrEmpty(targetProject.OutputDirectory) ? null : targetProject.OutputDirectory);

            // 显示窗口
            packWindow.ShowDialog();
        }

        #endregion

        #region 一键命令（批量操作）

        [RelayCommand(CanExecute = nameof(CanBuildSelected))]
        private async Task BuildSelectedAsync()
        {
            _outputViewModel.LogOutput = string.Empty;

            var selectedProjects = Projects.Where(p => p.IsSelected && p.IsDotNetProject).ToList();
            if (!selectedProjects.Any())
            {
                AduToastService.ShowWarning("请先选择要构建的.NET项目", "提示");
                return;
            }

            var projectsWithoutMSBuild = selectedProjects.Where(p => p.SelectedMSBuildVersion == null).ToList();
            if (projectsWithoutMSBuild.Any())
            {
                var names = string.Join(", ", projectsWithoutMSBuild.Select(p => p.Name));
                AduToastService.ShowError($"以下项目未选择MSBuild版本:\n{names}", "提示");
                return;
            }

            IsBusy = true;
            AppendLog($"\n========== 开始构建 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                bool buildFailed = false;
                foreach (var project in selectedProjects.OrderBy(p => p.SortOrder))
                {
                    if (buildFailed)
                    {
                        AppendLog($"[{project.Name}] 跳过多项目中构建（因前置项目构建失败）\n");
                        continue;
                    }

                    await ExecuteBuildAsync(project, clearLog: false);

                    // 检查是否有错误
                    if (!string.IsNullOrEmpty(project.ErrorMessage))
                    {
                        buildFailed = true;
                    }
                }

                AppendLog($"\n========== 构建完成 ==========\n");
                _outputViewModel.ForceFlush();
            }
            finally
            {
                IsBusy = false;
            }
        }
        private bool CanBuildSelected() => !IsBusy && Projects.Any(p => p.IsSelected && p.IsDotNetProject);

        [RelayCommand(CanExecute = nameof(CanSyncSelected))]
        private async Task SyncSelectedAsync()
        {
            var selectedProjects = Projects.Where(p => p.IsSelected).ToList();
            if (!selectedProjects.Any())
            {
                AduToastService.ShowWarning("请先选择要同步的项目", "提示");
                return;
            }

            IsBusy = true;
            AppendLog($"\n========== 开始同步 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                var tasks = selectedProjects.Select(project => ExecuteSyncAsync(project));
                await Task.WhenAll(tasks);
                AppendLog($"\n========== 同步完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }
        private bool CanSyncSelected() => !IsBusy && Projects.Any(p => p.IsSelected);

        [RelayCommand(CanExecute = nameof(CanPushSelected))]
        private async Task PushSelectedAsync()
        {
            var selectedProjects = Projects.Where(p => p.IsSelected).ToList();
            if (!selectedProjects.Any())
            {
                AduToastService.ShowWarning("请先选择要推送的项目", "提示");
                return;
            }

            IsBusy = true;
            AppendLog($"\n========== 开始推送 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                var tasks = selectedProjects.Select(project => ExecutePushAsync(project));
                await Task.WhenAll(tasks);
                AppendLog($"\n========== 推送完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }
        private bool CanPushSelected() => !IsBusy && Projects.Any(p => p.IsSelected);

        [RelayCommand(CanExecute = nameof(CanCommitSelected))]
        private async Task CommitSelectedAsync()
        {
            var selectedProjects = Projects.Where(p => p.IsSelected).ToList();
            if (!selectedProjects.Any())
            {
                AduToastService.ShowError("请先选择要提交的项目", "提示");
                return;
            }

            var needsMessage = selectedProjects.Where(p => p.HasChanges && string.IsNullOrWhiteSpace(p.CommitMessage) && !p.AutoCommitWhenNoMessage).ToList();
            if (needsMessage.Any())
            {
                var names = string.Join(", ", needsMessage.Select(p => p.Name));
                AduMessageBox.Show($"以下项目有未提交的更改且未勾选自动提交，请填写提交信息或勾选自动提交:\n{names}", "需要提交信息", MessageBoxButton.OK, MessageBoxImage.Information);
                foreach (var p in needsMessage)
                    p.IsExpanded = true;
                return;
            }

            IsBusy = true;
            AppendLog($"\n========== 开始提交 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                var tasks = selectedProjects.Select(project => ExecuteCommitAsync(project));
                await Task.WhenAll(tasks);
                AppendLog($"\n========== 提交完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }
        private bool CanCommitSelected() => !IsBusy && Projects.Any(p => p.IsSelected);

        private SyncOptions GetSyncOptions() => new()
        {
            PullStrategy = GlobalPullStrategy,
            ConflictAction = GlobalConflictAction,
            AutoCommitWhenNoMessage = GlobalAutoCommitWhenNoMessage,
            PushOnSync = GlobalPushOnSync
        };

        private async Task HandleSyncResultAsync(GitProject project, GitSyncResult result, IProgress<string> progress)
        {
            if (result.NeedsCommitMessage)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AduToastService.ShowWarning($"{project.Name} 有未提交的更改，请填写提交信息后重试。", "需要提交信息");
                    project.IsExpanded = true;
                    project.ErrorMessage = "请填写提交信息";
                });
                return;
            }

            if (result.HasConflict)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _conflictViewModel.Show(project, result.ConflictFiles ?? new List<string>());
                    project.ErrorMessage = result.ConflictMessage ?? "存在冲突";
                    project.IsExpanded = true;
                });
                return;
            }

            if (result.Success)
            {
                await _gitService.UpdateProjectStatusAsync(project);
                await Application.Current.Dispatcher.InvokeAsync(() => project.IsExpanded = false);
                AppendLog($"[{project.Name}] 同步成功\n");
            }
            else
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    project.ErrorMessage = result.Message;
                    project.IsExpanded = true;
                });
                AppendLog($"[{project.Name}] 同步失败: {result.Message}\n");
            }
        }
        #endregion
    }
}