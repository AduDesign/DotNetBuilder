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
        private readonly ProjectService _projectService;
        private readonly Action<string> _appendLog;
        private readonly Func<bool> _hasProject;
        private readonly Func<ProjectListViewModel> _getProjectListViewModel;
        private readonly Func<NewProjectDialogViewModel> _getNewProjectDialogViewModel;
        private readonly Func<string> _getSelectedPath;

        public event Action<ProjectInfo>? OnProjectLoaded;
        public event Action? OnProjectClosed;

        public ProjectLifecycleService(
            GitService gitService,
            ProjectService projectService,
            Action<string> appendLog,
            Func<bool> hasProject,
            Func<ProjectListViewModel> getProjectListViewModel,
            Func<NewProjectDialogViewModel> getNewProjectDialogViewModel,
            Func<string> getSelectedPath)
        {
            _gitService = gitService;
            _projectService = projectService;
            _appendLog = appendLog;
            _hasProject = hasProject;
            _getProjectListViewModel = getProjectListViewModel;
            _getNewProjectDialogViewModel = getNewProjectDialogViewModel;
            _getSelectedPath = getSelectedPath;

            // 订阅 ProjectService 事件
            _projectService.OnProjectLoaded += info => OnProjectLoaded?.Invoke(info);
            _projectService.OnProjectClosed += () => OnProjectClosed?.Invoke();
        }

        /// <summary>
        /// 创建新项目
        /// </summary>
        public void CreateNewProject(string name, string rootPath)
        {
            var projectList = _getProjectListViewModel();
            projectList.ClearProjects();

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
                _appendLog($"项目文件不存在: {filePath}\n");
                return;
            }

            _appendLog($"\n========== 打开项目: {filePath} ==========\n");

            var project = await _projectService.OpenProjectAsync(filePath);
            if (project == null)
            {
                _appendLog("项目加载失败\n");
                return;
            }

            _projectService.SetCurrentProject(project);

            var projectList = _getProjectListViewModel();
            projectList.ClearProjects();

            try
            {
                // 加载保存的项目配置（不再重新扫描，避免把已删除的项目加回来）
                await projectList.LoadSavedProjectsAsync(project.Projects);

                // 添加到最近项目
                await _projectService.AddRecentProjectAsync(project.Name, filePath);

                _appendLog($"\n========== 项目加载完成 ==========\n");
            }
            catch (Exception ex)
            {
                _appendLog($"加载项目时出错: {ex.Message}\n");
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
                    _appendLog("\n配置已保存\n");
                    AduToastService.ShowSuccess($"配置保存成功！", "提示"); 
                }
            }
            catch (Exception ex)
            {
                _appendLog($"\n保存配置失败: {ex.Message}\n");
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
            var projectList = _getProjectListViewModel();

            if (projectList.Projects.Any(p => p.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase)))
            {
                AduMessageBox.Show("该项目已在列表中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _appendLog($"\n正在添加项目: {projectPath}\n");

            await projectList.AddSingleProjectAsync(projectPath);
        }

        /// <summary>
        /// 刷新所有项目状态
        /// </summary>
        public async Task RefreshStatusAsync(IEnumerable<GitProject> projects)
        {
            _appendLog("\n正在刷新项目状态...\n");

            foreach (var project in projects)
            {
                await _gitService.UpdateProjectStatusAsync(project);
            }

            _appendLog("状态刷新完成\n");
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
            var rootPath = _getSelectedPath();
            if (string.IsNullOrEmpty(rootPath))
                return;

            var projectList = _getProjectListViewModel();
            projectList.ClearProjects();

            await projectList.ScanProjectsAsync(rootPath);
        }
    }
}
