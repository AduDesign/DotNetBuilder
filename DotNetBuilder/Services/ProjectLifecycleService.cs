using System.IO;
using System.Windows;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.ViewModels;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 项目生命周期服务 - 处理项目的创建、打开、保存、添加、刷新
    /// </summary>
    public class ProjectLifecycleService
    {
        private readonly GitService _gitService;
        private readonly GitSyncService _gitSyncService;
        private readonly ProjectService _projectService;
        private readonly OutputViewModel _outputViewModel;
        private readonly ProjectListViewModel _projectListViewModel;
        private readonly NewProjectDialogViewModel _newProjectDialogViewModel;

        public string SelectedPath { get; set; } = string.Empty;
        public bool HasProject => _projectService.CurrentProject != null;

        public event Action<ProjectInfo>? OnProjectLoaded;
        public event Action? OnProjectClosed;

        public ProjectLifecycleService(
            GitService gitService,
            GitSyncService gitSyncService,
            ProjectService projectService,
            OutputViewModel outputViewModel,
            ProjectListViewModel projectListViewModel,
            NewProjectDialogViewModel newProjectDialogViewModel)
        {
            _gitService = gitService;
            _gitSyncService = gitSyncService;
            _projectService = projectService;
            _outputViewModel = outputViewModel;
            _projectListViewModel = projectListViewModel;
            _newProjectDialogViewModel = newProjectDialogViewModel;

            // 订阅 ProjectService 事件
            _projectService.OnProjectLoaded += info => OnProjectLoaded?.Invoke(info);
            _projectService.OnProjectClosed += () => OnProjectClosed?.Invoke();
        }

        private void AppendLog(string message) => _outputViewModel.AppendLog(message);

        /// <summary>
        /// 创建新项目
        /// </summary>
        public void CreateNewProject(string name, string rootPath)
        {
            _projectListViewModel.ClearProjects();

            var project = _projectService.CreateProject(name, rootPath);
            _projectService.SetCurrentProject(project);

            _ = ScanProjectsAsync();
        }

        /// <summary>
        /// 打开项目文件
        /// </summary>
        public async Task OpenProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
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

            _projectListViewModel.ClearProjects();

            try
            {
                // 加载保存的项目配置（不再重新扫描，避免把已删除的项目加回来）
                await _projectListViewModel.LoadSavedProjectsAsync(project.Projects);

                // 添加到最近项目
                await _projectService.AddRecentProjectAsync(project.Name, filePath);

                AppendLog($"\n========== 项目加载完成 ==========\n");
            }
            catch (Exception ex)
            {
                AppendLog($"加载项目时出错: {ex.Message}\n");
            }
        }

        /// <summary>
        /// 保存当前项目
        /// </summary>
        public async Task SaveProjectAsync(string projectName, string rootPath, IEnumerable<GitProject> projects)
        {
            try
            {
                var projectInfo = new ProjectInfo
                {
                    Name = projectName.Replace(" *", ""),
                    RootPath = rootPath,
                    Projects = projects.Select(p => new Models.ProjectConfig
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

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存项目",
                    Filter = _projectService.GetProjectFileFilter(),
                    DefaultExt = ".bdproj",
                    FileName = projectInfo.Name
                };

                if (dialog.ShowDialog() == true)
                {
                    await _projectService.SaveProjectAsync(projectInfo, dialog.FileName);
                    AppendLog("\n配置已保存\n");
                    AduToastService.ShowSuccess($"配置保存成功！", "提示");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"\n保存配置失败: {ex.Message}\n");
                AduToastService.ShowError($"保存配置失败: {ex.Message}", "提示");
            }
        }

        /// <summary>
        /// 添加项目到当前列表
        /// </summary>
        public async Task AddProjectAsync()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择Git项目目录"
            };

            if (dialog.ShowDialog() != true)
                return;

            var projectPath = dialog.FolderName;

            if (_projectListViewModel.Projects.Any(p => p.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase)))
            {
                AduMessageBox.Show("该项目已在列表中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppendLog($"\n正在添加项目: {projectPath}\n");

            await _projectListViewModel.AddSingleProjectAsync(projectPath);
        }

        /// <summary>
        /// 刷新所有项目状态
        /// </summary>
        public async Task RefreshStatusAsync(IEnumerable<GitProject> projects)
        {
            AppendLog("\n正在刷新项目状态...\n");

            var projectList = projects.ToList();
            int unpushedCount = 0;
            int uncommittedCount = 0;

            foreach (var project in projectList)
            {
                // 更新本地更改状态
                await _gitService.UpdateProjectStatusAsync(project);

                // 获取远程状态（未推送的提交数量）
                try
                {
                    var remoteStatus = await _gitSyncService.GetRemoteStatusAsync(project);
                    project.RemoteStatus = remoteStatus;
                    if (remoteStatus.LocalAheadCount > 0)
                    {
                        unpushedCount++;
                    }
                }
                catch
                {
                    project.RemoteStatus = null;
                }

                if (project.HasChanges)
                {
                    uncommittedCount++;
                }
            }

            AppendLog($"状态刷新完成：{uncommittedCount} 个项目有待提交更改，{unpushedCount} 个项目有待推送提交\n");
        }

        /// <summary>
        /// 关闭当前项目
        /// </summary>
        public void CloseProject()
        {
            _projectService.SetCurrentProject(null);
        }

        /// <summary>
        /// 扫描项目
        /// </summary>
        private async Task ScanProjectsAsync()
        {
            if (string.IsNullOrEmpty(SelectedPath))
                return;

            _projectListViewModel.ClearProjects();

            await _projectListViewModel.ScanProjectsAsync(SelectedPath);
        }
    }
}
