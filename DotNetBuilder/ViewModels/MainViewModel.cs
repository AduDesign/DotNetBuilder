using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 主窗口ViewModel - 纯粹的视图组合器，使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        // 服务实例
        private readonly GitService _gitService;
        private readonly GitSyncService _gitSyncService;
        private readonly MSBuildService _msbuildService;
        private readonly ConfigService _configService;
        private readonly ProjectService _projectService;
        private readonly FileAssociationService _fileAssociationService;
        private readonly NavigationService _navigationService;
        private readonly DialogService _dialogService;
        private readonly ProjectLifecycleService _lifecycleService;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasProject))]
        private string _selectedPath = string.Empty;

        private bool _projectService_HasProject;
        public bool HasProject => _projectService_HasProject;

        // 子 ViewModels
        public WelcomeViewModel WelcomeViewModel { get; }
        public ToolbarViewModel ToolbarViewModel { get; }
        public ProjectListViewModel ProjectListViewModel { get; }
        public OutputViewModel OutputViewModel { get; }
        public ConflictDialogViewModel ConflictViewModel { get; }
        public NewProjectDialogViewModel NewProjectViewModel { get; }

        public MainViewModel()
        {
            // 初始化服务
            _gitService = new GitService();
            _gitSyncService = new GitSyncService();
            _msbuildService = new MSBuildService();
            _configService = new ConfigService();
            _projectService = new ProjectService();
            _fileAssociationService = new FileAssociationService();
            _navigationService = new NavigationService();

            // 初始化子 ViewModels（必须先于 ProjectLifecycleService）
            OutputViewModel = new OutputViewModel();
            ProjectListViewModel = new ProjectListViewModel(_gitService, _msbuildService, OutputViewModel, AppendLog);
            ConflictViewModel = new ConflictDialogViewModel();
            NewProjectViewModel = new NewProjectDialogViewModel();
            WelcomeViewModel = new WelcomeViewModel(_projectService, _navigationService);
            ToolbarViewModel = new ToolbarViewModel(_navigationService);

            // 初始化对话框服务
            _dialogService = new DialogService(ShowNewProjectDialogDialog, ShowConflictDialogDialog);

            // 初始化 ProjectLifecycleService
            _lifecycleService = new ProjectLifecycleService(
                _gitService,
                _projectService,
                AppendLog,
                () => HasProject,
                () => ProjectListViewModel,
                () => NewProjectViewModel,
                () => SelectedPath);

            // 订阅导航服务事件
            _navigationService.OnOpenProjectRequested += async (filePath) => await OpenProjectAsync(filePath);

            // 订阅生命周期服务事件
            _lifecycleService.OnProjectLoaded += OnProjectLoaded;
            _lifecycleService.OnProjectClosed += OnProjectClosed;

            // 设置对话框回调
            ConflictViewModel.SetAppendLog(AppendLog);
            NewProjectViewModel.SetOnConfirmCallback(CreateNewProject);

            // 设置 ViewModel 回调
            WelcomeViewModel.SetOnNewProject(() => NewProjectViewModel.Show());
            ToolbarViewModel.SetOnNewProject(() => NewProjectViewModel.Show());
            ToolbarViewModel.SetOnSaveProject(() => SaveProjectCommand.Execute(null));
            ToolbarViewModel.SetOnAddProject(async () => await _lifecycleService.AddProjectAsync());
            ToolbarViewModel.SetOnRefreshStatus(async () => await _lifecycleService.RefreshStatusAsync(Projects));

            // 加载 MSBuild 版本
            LoadMSBuildVersions();
        }

        partial void OnIsBusyChanged(bool value)
        {
            ToolbarViewModel.IsEnabled = !value;
            ProjectListViewModel.IsEnabled = !value;
            OnPropertyChanged(nameof(HasProject));
        }

        partial void OnSelectedPathChanged(string value)
        {
            ToolbarViewModel.SelectedPath = value;
        }

        #region 属性

        public ObservableCollection<RecentProject> RecentProjects => WelcomeViewModel.RecentProjects;

        public ObservableCollection<GitProject> Projects => ProjectListViewModel.Projects;

        public ObservableCollection<MSBuildVersion> MSBuildVersions => ProjectListViewModel.MSBuildVersions;

        public ObservableCollection<string> ConfigurationTypes => ProjectListViewModel.ConfigurationTypes;

        public GitProject? SelectedProject
        {
            get => ProjectListViewModel.SelectedProject;
            set => ProjectListViewModel.SelectedProject = value;
        }

        public string LogOutput => OutputViewModel.LogOutput;

        public bool? IsSelectedAll
        {
            get => ProjectListViewModel.IsSelectedAll;
            set => ProjectListViewModel.IsSelectedAll = value;
        }

        #endregion

        #region 命令

        [RelayCommand(CanExecute = nameof(CanBuildSelected))]
        private async Task BuildSelectedAsync()
        {
            OutputViewModel.LogOutput = string.Empty;

            var selectedProjects = Projects.Where(p => p.IsSelected && p.IsDotNetProject).ToList();
            if (!selectedProjects.Any())
            {
                AduMessageBox.Show("请先选择要构建的.NET项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var projectsWithoutMSBuild = selectedProjects.Where(p => p.SelectedMSBuildVersion == null).ToList();
            if (projectsWithoutMSBuild.Any())
            {
                var names = string.Join(", ", projectsWithoutMSBuild.Select(p => p.Name));
                AduMessageBox.Show($"以下项目未选择MSBuild版本:\n{names}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

                    await Application.Current.Dispatcher.InvokeAsync(() => project.ClearError());
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        project.IsBuilding = true;
                        project.IsExpanded = true;
                    });

                    try
                    {
                        var progress = new Progress<string>(msg => AppendLog(msg + "\n"));
                        AppendLog($"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion?.DisplayName}, 配置: {project.Configuration}\n");
                        var result = await _msbuildService.BuildProjectAsync(project, project.Configuration, project.SelectedMSBuildVersion, progress);

                        if (result.Success)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() => project.IsExpanded = false);
                        }
                        else
                        {
                            AppendLog($"[{project.Name}] 构建失败: {result.ErrorMessage}\n");
                            buildFailed = true;
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                                project.IsExpanded = true;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[{project.Name}] 构建异常: {ex.Message}\n");
                        buildFailed = true;
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            project.ErrorMessage = ex.Message;
                            project.IsExpanded = true;
                        });
                    }
                    finally
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => project.IsBuilding = false);
                    }
                }

                AppendLog($"\n========== 构建完成 ==========\n");
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
                AduMessageBox.Show("请先选择要同步的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            AppendLog($"\n========== 开始同步 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                var tasks = selectedProjects.Select(async project =>
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        project.ClearError();
                        project.IsSyncing = true;
                        project.IsExpanded = true;
                    });

                    try
                    {
                        var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                        var options = OutputViewModel.GetSyncOptions();

                        await _gitService.UpdateProjectStatusAsync(project);
                        var result = await _gitSyncService.SyncProjectAsync(project, project.CommitMessage, options, progress);

                        await HandleSyncResultAsync(project, result, progress);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[{project.Name}] 同步异常: {ex.Message}\n");
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
                AppendLog($"\n========== 同步完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }
        private bool CanSyncSelected() => !IsBusy && Projects.Any(p => p.IsSelected);

        [RelayCommand]
        private async Task SyncSingleAsync(GitProject? project)
        {
            if (project == null)
                return;

            AppendLog($"\n========== 同步项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsSyncing = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                var options = OutputViewModel.GetSyncOptions();

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

        [RelayCommand]
        private async Task BuildSingleAsync(GitProject? project)
        {
            if (project == null)
                return;

            OutputViewModel.LogOutput = string.Empty;

            if (project.SelectedMSBuildVersion == null)
            {
                AduMessageBox.Show($"请先为 {project.Name} 选择 MSBuild 版本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppendLog($"\n========== 构建项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsBuilding = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg => AppendLog($"[{project.Name}] {msg}\n"));
                AppendLog($"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion.DisplayName}, 配置: {project.Configuration}\n");
                var result = await _msbuildService.BuildProjectAsync(project, project.Configuration, project.SelectedMSBuildVersion, progress);

                if (result.Success)
                {
                    project.IsExpanded = false;
                }
                else
                {
                    project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                    AppendLog($"[{project.Name}] 构建失败: {result.ErrorMessage}\n");
                }
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

        [RelayCommand(CanExecute = nameof(CanRunSelected))]
        private async Task RunSelectedAsync()
        {
            if (SelectedProject == null || string.IsNullOrEmpty(SelectedProject.ExecuteFile))
            {
                AduMessageBox.Show("请先选择要运行的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppendLog($"\n========== 运行项目: {SelectedProject.Name} ==========\n");

            try
            {
                var exePath = SelectedProject.ExecuteFile;
                var workingDir = System.IO.Path.GetDirectoryName(exePath) ?? SelectedProject.Path;

                AppendLog($"[{SelectedProject.Name}] 启动: {exePath}\n");

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
                AppendLog($"[{SelectedProject.Name}] 运行失败: {ex.Message}\n");
            }
        }
        private bool CanRunSelected() => SelectedProject != null && !string.IsNullOrEmpty(SelectedProject.ExecuteFile);

        [RelayCommand(CanExecute = nameof(CanSaveProject))]
        private async Task SaveProjectAsync()
        {
            try
            {
                var currentProject = _projectService.CurrentProject;
                var filePath = currentProject?.FilePath;

                // 如果没有现有文件路径，才弹出保存对话框
                if (string.IsNullOrEmpty(filePath))
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "保存项目",
                        Filter = _projectService.GetProjectFileFilter(),
                        DefaultExt = ".bdproj",
                        FileName = ToolbarViewModel.ProjectDisplayName.Replace(" *", "")
                    };

                    if (dialog.ShowDialog() != true)
                        return;

                    filePath = dialog.FileName;
                }

                var projectInfo = new ProjectInfo
                {
                    Name = ToolbarViewModel.ProjectDisplayName.Replace(" *", ""),
                    RootPath = SelectedPath,
                    FilePath = filePath,
                    Projects = Projects.Select(p => new Models.ProjectConfig
                    {
                        Path = p.Path,
                        IsSelected = p.IsSelected,
                        SelectedMSBuildVersion = p.SelectedMSBuildVersion?.DisplayName,
                        ExecuteFile = p.ExecuteFile,
                        Configuration = p.Configuration,
                        Order = p.SortOrder,
                        PullStrategy = p.PullStrategy,
                        ConflictAction = p.ConflictAction,
                        AutoCommitWhenNoMessage = p.AutoCommitWhenNoMessage
                    }).ToList()
                };

                await _projectService.SaveProjectAsync(projectInfo, filePath);
                AppendLog($"\n配置已保存: {filePath}\n");
            }
            catch (Exception ex)
            {
                AppendLog($"\n保存配置失败: {ex.Message}\n");
                AduMessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private bool CanSaveProject() => HasProject;

        [RelayCommand]
        private void ClearLog()
        {
            OutputViewModel.LogOutput = string.Empty;
        }

        [RelayCommand]
        private void RegisterFileAssociation()
        {
            if (_fileAssociationService.RegisterFileAssociation())
            {
                AppendLog("已成功注册 .bdproj 文件关联\n");
                AduMessageBox.Show(
                    "已成功注册 .bdproj 文件关联。\n双击 .bdproj 文件即可用 DotNetBuilder 打开。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                AduMessageBox.Show(
                    "注册文件关联失败，请重试。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void CloseProject()
        {
            _lifecycleService.CloseProject();
        }

        #endregion

        #region 方法

        private void AppendLog(string message)
        {
            OutputViewModel.AppendLog(message);
        }

        private void LoadMSBuildVersions()
        {
            var versions = _msbuildService.DetectMSBuildVersions();
            ProjectListViewModel.LoadMSBuildVersions(versions);
        }

        private void ShowNewProjectDialogDialog()
        {
            NewProjectViewModel.Show();
        }

        private void ShowConflictDialogDialog(string projectName, List<string> conflictFiles)
        {
            var project = Projects.FirstOrDefault(p => p.Name == projectName);
            if (project != null)
            {
                ConflictViewModel.Show(project, conflictFiles);
            }
        }

        public async Task OpenProjectAsync(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
            {
                AppendLog($"项目文件不存在: {filePath}\n");
                return;
            }

            AppendLog($"\n========== 打开项目: {filePath} ==========\n");

            var project = await _projectService.OpenProjectAsync(filePath);
            if (project == null)
            {
                AppendLog("项目加载失败\n");
                return;
            }

            _projectService.SetCurrentProject(project);
            OnProjectLoaded(project);

            IsBusy = true;
            ProjectListViewModel.ClearProjects();

            try
            {
                SelectedPath = project.RootPath;
                await ProjectListViewModel.LoadSavedProjectsAsync(project.Projects);

                SelectedProject = ProjectListViewModel.Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
                await WelcomeViewModel.LoadRecentProjectsAsync();
                AppendLog($"\n========== 项目加载完成 ==========\n");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void CreateNewProject(string name, string rootPath)
        {
            ProjectListViewModel.ClearProjects();
            SelectedPath = rootPath;
            var project = _projectService.CreateProject(name, rootPath);

            // 直接保存项目文件到 rootPath
            var filePath = System.IO.Path.Combine(rootPath, $"{name}.bdproj");
            await _projectService.SaveProjectAsync(project, filePath);
            await _projectService.AddRecentProjectAsync(project.Name, filePath);
            AppendLog($"\n项目已保存: {filePath}\n");

            _projectService.SetCurrentProject(project);
            await ScanProjectsAsync();
        }

        private async Task ScanProjectsAsync()
        {
            if (string.IsNullOrEmpty(SelectedPath))
                return;

            IsBusy = true;
            ProjectListViewModel.ClearProjects();

            try
            {
                await ProjectListViewModel.ScanProjectsAsync(SelectedPath);
                SelectedProject = ProjectListViewModel.Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task HandleSyncResultAsync(GitProject project, GitSyncResult result, IProgress<string> progress)
        {
            if (result.NeedsCommitMessage)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AduMessageBox.Show(
                        $"{project.Name} 有未提交的更改，请填写提交信息后重试。",
                        "需要提交信息",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    project.IsExpanded = true;
                    project.ErrorMessage = "请填写提交信息";
                });
                return;
            }

            if (result.HasConflict)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ConflictViewModel.Show(project, result.ConflictFiles ?? new List<string>());
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

        private void OnProjectLoaded(ProjectInfo project)
        {
            _projectService_HasProject = true;
            OnPropertyChanged(nameof(HasProject));
            ToolbarViewModel.HasProject = true;
            ToolbarViewModel.ProjectDisplayName = project.Name;
        }

        private void OnProjectClosed()
        {
            _projectService_HasProject = false;
            OnPropertyChanged(nameof(HasProject));
            ToolbarViewModel.HasProject = false;
            ProjectListViewModel.ClearProjects();
        }

        #endregion
    }
}
