using AduSkin.Controls;
using AduSkin.Languages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

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
        public CloneDialogViewModel CloneDialogViewModel { get; }

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
            ConflictViewModel = new ConflictDialogViewModel();
            ProjectListViewModel = new ProjectListViewModel(_gitService, _gitSyncService, _msbuildService, OutputViewModel, ConflictViewModel, AppendLog);
            NewProjectViewModel = new NewProjectDialogViewModel();
            CloneDialogViewModel = new CloneDialogViewModel();
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
            CloneDialogViewModel.SetOnCloneCallback(CloneRepositoryAsync);
            CloneDialogViewModel.SetOnCloneSuccessCallback(OnCloneSuccess);

            // 设置 ViewModel 回调
            WelcomeViewModel.SetOnNewProject(() => NewProjectViewModel.Show());
            ToolbarViewModel.SetOnNewProject(() => NewProjectViewModel.Show());
            ToolbarViewModel.SetOnSaveProject(() => SaveProjectCommand.Execute(null));
            ToolbarViewModel.SetOnAddProject(async () => await _lifecycleService.AddProjectAsync());
            ToolbarViewModel.SetOnRefreshStatus(async () => await _lifecycleService.RefreshStatusAsync(Projects));
            ToolbarViewModel.SetOnScanAndAddProjects(async path => await ScanAndAddProjectsAsync(path));
            ToolbarViewModel.SetOnCloneProject(() => CloneDialogViewModel.ShowWithDefaultDirectory(SelectedPath));

            // 加载 MSBuild 版本
            LoadMSBuildVersions();

            LanguageManager.Instance.SwitchLanguage("zh-CN");
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
                    GlobalPullStrategy = ProjectListViewModel.GlobalPullStrategy,
                    GlobalConflictAction = ProjectListViewModel.GlobalConflictAction,
                    GlobalAutoCommitWhenNoMessage = ProjectListViewModel.GlobalAutoCommitWhenNoMessage,
                    GlobalPushOnSync = ProjectListViewModel.GlobalPushOnSync,
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

        [RelayCommand(CanExecute = nameof(CanScanCurrentPath))]
        private async Task ScanCurrentPathAsync()
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
        private bool CanScanCurrentPath() => !IsBusy && !string.IsNullOrEmpty(SelectedPath);


        [RelayCommand]
        public void About()
        {
            AduMessageBox.Show(
                ".NET Project Builder v1.0\n\n批量管理 Git 仓库和 .NET 项目\n支持并行构建和冲突处理",
                "关于",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        [RelayCommand]
        public void ToastLoaded(object obj)
        {
            if (obj is AduToastContainer container)
                AduToastService.Initialize(container);
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

                // 恢复全局同步设置
                ProjectListViewModel.GlobalPullStrategy = project.GlobalPullStrategy;
                ProjectListViewModel.GlobalConflictAction = project.GlobalConflictAction;
                ProjectListViewModel.GlobalAutoCommitWhenNoMessage = project.GlobalAutoCommitWhenNoMessage;
                ProjectListViewModel.GlobalPushOnSync = project.GlobalPushOnSync;

                SelectedProject = ProjectListViewModel.Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
                await _projectService.AddRecentProjectAsync(project.Name, filePath);
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
            await ScanAndAddProjectsAsync(rootPath);
        }

        private async Task ScanAndAddProjectsAsync(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                return;

            IsBusy = true;

            try
            {
                AppendLog($"\n========== 扫描目录: {rootPath} ==========\n");
                var progress = new Progress<string>(msg => AppendLog(msg + "\n"));
                var discovered = await _gitService.ScanGitProjectsAsync(rootPath, progress);

                int addedCount = 0;
                int skipCount = 0;
                var existingPaths = Projects.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var msbuildVersions = ProjectListViewModel.MSBuildVersions.ToList();

                foreach (var project in discovered)
                {
                    if (existingPaths.Contains(project.Path))
                    {
                        skipCount++;
                        continue;
                    }

                    project.SolutionPath = _gitService.GetSolutionPath(project.Path);
                    project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
                    project.SortOrder = Projects.Count;
                    if (msbuildVersions.Count > 0)
                        project.SelectedMSBuildVersion = msbuildVersions[0];

                    ProjectListViewModel.AddProject(project);
                    addedCount++;
                }

                AppendLog($"\n扫描完成，发现 {discovered.Count} 个 Git 项目，其中 {addedCount} 个已添加，{skipCount} 个已存在\n");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<bool> CloneRepositoryAsync(string repositoryUrl, string localDirectory, IProgress<string>? progress, CancellationToken? cancellationToken)
        {
            if (string.IsNullOrEmpty(repositoryUrl) || string.IsNullOrEmpty(localDirectory))
                return false;

            AppendLog($"\n========== 克隆仓库: {repositoryUrl} ==========\n");

            return await _gitService.CloneRepositoryAsync(repositoryUrl, localDirectory, progress, cancellationToken);
        }

        private async void OnCloneSuccess(string repositoryUrl, string localDirectory)
        {
            // 添加克隆的项目到列表
            var url = repositoryUrl.TrimEnd('/');
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url[..^4];
            var repoName = Path.GetFileName(url);
            var fullPath = Path.Combine(localDirectory, repoName);

            var project = await _gitService.AddGitProjectAsync(fullPath, null);
            if (project != null)
            {
                project.SolutionPath = _gitService.GetSolutionPath(fullPath);
                project.ProjectFilePath = _gitService.GetProjectFilePath(fullPath);
                project.SortOrder = Projects.Count;

                var msbuildVersions = ProjectListViewModel.MSBuildVersions.ToList();
                if (msbuildVersions.Count > 0)
                    project.SelectedMSBuildVersion = msbuildVersions[0];

                ProjectListViewModel.AddProject(project);
                AppendLog($"\n已添加克隆的项目: {project.Name}\n");

                // 自动保存项目
                await SaveProjectCommand.ExecuteAsync(null);
            }
            else
            {
                AppendLog($"\n克隆成功但非 Git 项目: {fullPath}\n");
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
