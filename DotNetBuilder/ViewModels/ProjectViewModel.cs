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
    /// 项目视图模型 - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class ProjectViewModel : ObservableObject
    {
        private readonly ProjectService _projectService;
        private readonly Action<string> _appendLog;

        [ObservableProperty]
        private ProjectInfo? _currentProject;

        [ObservableProperty]
        private string _projectName = "未命名项目";

        [ObservableProperty]
        private bool _hasUnsavedChanges;

        [ObservableProperty]
        private bool _showNewProjectDialog;

        [ObservableProperty]
        private bool _showRecentProjectsMenu;

        public ObservableCollection<RecentProject> RecentProjects { get; } = new();

        public bool HasProject => CurrentProject != null;

        public string ProjectDisplayName
        {
            get
            {
                if (CurrentProject == null)
                    return "未打开项目";
                var suffix = HasUnsavedChanges ? " *" : "";
                return $"{CurrentProject.Name}{suffix}";
            }
        }

        /// <summary>
        /// 对话框显示回调
        /// </summary>
        public event Action? OnShowNewProjectDialogRequested;

        public ProjectViewModel(ProjectService projectService, Action<string> appendLog)
        {
            _projectService = projectService;
            _appendLog = appendLog;

            _ = LoadRecentProjectsAsync();
        }

        partial void OnCurrentProjectChanged(ProjectInfo? value)
        {
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(ProjectDisplayName));
        }

        partial void OnHasUnsavedChangesChanged(bool value)
        {
            OnPropertyChanged(nameof(ProjectDisplayName));
        }

        partial void OnProjectNameChanged(string value)
        {
            if (CurrentProject != null)
            {
                CurrentProject.Name = value;
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void NewProject()
        {
            OnShowNewProjectDialogRequested?.Invoke();
            ShowNewProjectDialog = true;
        }

        [RelayCommand]
        private async Task OpenProjectAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开项目",
                Filter = _projectService.GetProjectFileFilter(),
                DefaultExt = ".bdproj"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadProjectAsync(dialog.FileName);
            }
        }

        [RelayCommand(CanExecute = nameof(CanSaveProject))]
        private async Task SaveProjectAsync()
        {
            if (CurrentProject == null)
                return;

            if (string.IsNullOrEmpty(CurrentProject.FilePath))
            {
                await SaveProjectAsAsync();
                return;
            }

            await DoSaveProjectAsync(CurrentProject.FilePath);
        }
        private bool CanSaveProject() => HasProject && HasUnsavedChanges;

        [RelayCommand]
        private async Task SaveProjectAsAsync()
        {
            if (CurrentProject == null)
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存项目",
                Filter = _projectService.GetProjectFileFilter(),
                DefaultExt = ".bdproj",
                FileName = CurrentProject.Name
            };

            if (dialog.ShowDialog() == true)
            {
                await DoSaveProjectAsync(dialog.FileName);
            }
        }

        [RelayCommand(CanExecute = nameof(CanCloseProject))]
        private void CloseProject()
        {
            CurrentProject = null;
            ProjectName = "未命名项目";
            HasUnsavedChanges = false;
            _appendLog("已关闭项目\n");
        }
        private bool CanCloseProject() => HasProject;

        /// <summary>
        /// 创建新项目
        /// </summary>
        public void CreateNewProject(string name, string rootPath)
        {
            CurrentProject = _projectService.CreateProject(name, rootPath);
            ProjectName = name;
            HasUnsavedChanges = true;
            ShowNewProjectDialog = false;

            _appendLog($"已创建新项目: {name}\n");
            _appendLog($"根目录: {rootPath}\n");
        }

        public async Task LoadProjectAsync(string filePath)
        {
            try
            {
                var project = await _projectService.OpenProjectAsync(filePath);
                if (project != null)
                {
                    // 如果有未保存的当前项目，先提示保存
                    if (HasProject && HasUnsavedChanges)
                    {
                        var result = AduMessageBox.Show(
                            "当前项目有未保存的更改，是否保存？",
                            "提示",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            await SaveProjectAsync();
                        }
                        else if (result == MessageBoxResult.Cancel)
                        {
                            return;
                        }
                    }

                    CurrentProject = project;
                    ProjectName = project.Name;
                    HasUnsavedChanges = false;

                    await _projectService.AddRecentProjectAsync(project.Name, filePath);
                    await LoadRecentProjectsAsync();

                    _appendLog($"已打开项目: {project.Name}\n");
                    _appendLog($"项目文件: {filePath}\n");
                    _appendLog($"包含 {project.Projects.Count} 个 Git 项目\n");
                }
                else
                {
                    AduMessageBox.Show("无法打开项目文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _appendLog($"打开项目失败: {ex.Message}\n");
                AduMessageBox.Show($"打开项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DoSaveProjectAsync(string filePath)
        {
            if (CurrentProject == null)
                return;

            try
            {
                await _projectService.SaveProjectAsync(CurrentProject, filePath);
                await _projectService.AddRecentProjectAsync(CurrentProject.Name, filePath);
                await LoadRecentProjectsAsync();

                HasUnsavedChanges = false;
                OnPropertyChanged(nameof(ProjectDisplayName));

                _appendLog($"已保存项目: {filePath}\n");
            }
            catch (Exception ex)
            {
                _appendLog($"保存项目失败: {ex.Message}\n");
                AduMessageBox.Show($"保存项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadRecentProjectsAsync()
        {
            var recent = await _projectService.GetRecentProjectsAsync();
            RecentProjects.Clear();
            foreach (var item in recent)
            {
                RecentProjects.Add(item);
            }
        }

        public async Task OpenFromCommandLineAsync(string[] args)
        {
            var projectPath = _projectService.GetProjectFromArgs(args);
            if (!string.IsNullOrEmpty(projectPath))
            {
                await LoadProjectAsync(projectPath);
            }
        }
    }
}
