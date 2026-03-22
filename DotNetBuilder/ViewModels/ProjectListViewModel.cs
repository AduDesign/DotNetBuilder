using System.Collections.ObjectModel;
using System.Windows.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 项目列表 ViewModel
    /// </summary>
    public class ProjectListViewModel : ViewModelBase
    {
        private readonly GitService _gitService;
        private readonly MSBuildService _msbuildService;
        private readonly OutputViewModel _outputViewModel;
        private readonly Action<string> _appendLog;
        private GitProject? _selectedItem;
        private GitProject? _selectedProject;
        private bool _isEnabled = true;

        public ProjectListViewModel(
            GitService gitService,
            MSBuildService msbuildService,
            OutputViewModel outputViewModel,
            Action<string> appendLog)
        {
            _gitService = gitService;
            _msbuildService = msbuildService;
            _outputViewModel = outputViewModel;
            _appendLog = appendLog;

            MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
            RemoveProjectCommand = new RelayCommand(RemoveProject);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            OpenVSCommand = new RelayCommand(OpenVS);
            OpenVSCodeCommand = new RelayCommand(OpenVSCode);
            BuildSingleCommand = new AsyncRelayCommand(BuildSingleAsync);
            RunSelectedCommand = new AsyncRelayCommand(RunSelectedAsync);
        }

        public ObservableCollection<GitProject> Projects { get; } = new();
        public ObservableCollection<MSBuildVersion> MSBuildVersions { get; } = new();
        public ObservableCollection<string> ConfigurationTypes { get; } = new() { "Release", "Debug" };
        public ObservableCollection<string> Executables { get; } = new();

        public GitProject? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public GitProject? SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (SetProperty(ref _selectedProject, value))
                {
                    LoadProjectExecutables();
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool? IsSelectedAll
        {
            get
            {
                if (Projects.Count(s => s.IsSelected) == Projects.Count)
                    return true;
                else if (Projects.Any(s => s.IsSelected))
                    return null;
                else
                    return false;
            }
            set
            {
                bool? oldValue = IsSelectedAll;
                foreach (var item in Projects)
                {
                    if (oldValue == true)
                        item.SetIsSelected(false);
                    else if (oldValue == false)
                        item.SetIsSelected(true);
                    else if (oldValue == null)
                        item.SetIsSelected(true);
                }
                OnPropertyChanged();
            }
        }

        public IEnumerable<GitProject> SelectedProjects => Projects.Where(p => p.IsSelected);

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand RemoveProjectCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand OpenVSCommand { get; }
        public ICommand OpenVSCodeCommand { get; }
        public ICommand BuildSingleCommand { get; }
        public ICommand RunSelectedCommand { get; }

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
                    OnPropertyChanged(nameof(IsSelectedAll));
            };
            Projects.Add(project);
            OnPropertyChanged(nameof(IsSelectedAll));
        }

        public void ClearProjects()
        {
            Projects.Clear();
            OnPropertyChanged(nameof(IsSelectedAll));
        }

        public async Task ScanProjectsAsync(string path)
        {
            _appendLog($"正在扫描: {path}\n");

            try
            {
                var progress = new Progress<string>(msg => _appendLog(msg + "\n"));
                var projects = await _gitService.ScanGitProjectsAsync(path, progress);

                int sortOrder = 0;
                foreach (var project in projects)
                {
                    project.SolutionPath = _gitService.GetSolutionPath(project.Path);
                    project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
                    project.SortOrder = sortOrder++;
                    if (MSBuildVersions.Count > 0)
                        project.SelectedMSBuildVersion = MSBuildVersions[0];
                    AddProject(project);
                }

                SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));

                _appendLog($"\n扫描完成，发现 {projects.Count} 个Git项目\n");
                if (projects.Count > 0)
                {
                    var dotnetCount = projects.Count(p => p.IsDotNetProject);
                    _appendLog($"其中 {dotnetCount} 个是.NET项目\n");
                }
            }
            catch (Exception ex)
            {
                _appendLog($"\n扫描失败: {ex.Message}\n");
            }
        }

        public async Task AddSingleProjectAsync(string projectPath)
        {
            var progress = new Progress<string>(msg => _appendLog(msg + "\n"));
            var project = await _gitService.AddGitProjectAsync(projectPath, progress);

            if (project == null)
            {
                _appendLog("添加失败: 所选目录不是Git项目目录\n");
                return;
            }

            project.SolutionPath = _gitService.GetSolutionPath(project.Path);
            project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
            project.SortOrder = Projects.Count;
            if (MSBuildVersions.Count > 0)
                project.SelectedMSBuildVersion = MSBuildVersions[0];
            AddProject(project);
            _appendLog($"已添加项目: {project.Name}\n");
        }

        private void MoveUp(object? parameter)
        {
            if (parameter is GitProject project)
            {
                var index = Projects.IndexOf(project);
                if (index > 0)
                {
                    Projects.Move(index, index - 1);
                    UpdateSortOrders();
                }
            }
        }

        private bool CanMoveUp(object? parameter)
        {
            if (parameter is GitProject project)
            {
                return Projects.IndexOf(project) > 0;
            }
            return false;
        }

        private void MoveDown(object? parameter)
        {
            if (parameter is GitProject project)
            {
                var index = Projects.IndexOf(project);
                if (index < Projects.Count - 1)
                {
                    Projects.Move(index, index + 1);
                    UpdateSortOrders();
                }
            }
        }

        private bool CanMoveDown(object? parameter)
        {
            if (parameter is GitProject project)
            {
                return Projects.IndexOf(project) < Projects.Count - 1;
            }
            return false;
        }

        private void UpdateSortOrders()
        {
            for (int i = 0; i < Projects.Count; i++)
            {
                Projects[i].SortOrder = i;
            }
        }

        private void RemoveProject(object? parameter)
        {
            if (parameter is GitProject project)
            {
                project.IsSelected = false;
                project.IsRemoved = true;
                Projects.Remove(project);
                _appendLog($"已移除项目: {project.Name}\n");

                if (SelectedProject == project)
                    SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
            }
        }

        private void OpenFolder(object? parameter)
        {
            if (parameter is not GitProject project) return;

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
                _appendLog($"打开文件夹失败: {ex.Message}\n");
            }
        }

        private void OpenVS(object? parameter)
        {
            if (parameter is not GitProject project) return;

            try
            {
                var targetPath = !string.IsNullOrEmpty(project.SolutionPath)
                    ? project.SolutionPath
                    : project.ProjectFilePath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    _appendLog($"[{project.Name}] 未找到解决方案或项目文件\n");
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
                _appendLog($"用 VisualStudio 打开失败: {ex.Message}\n");
            }
        }

        private void OpenVSCode(object? parameter)
        {
            if (parameter is not GitProject project) return;

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
                _appendLog($"用 VSCode 打开失败: {ex.Message}\n");
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

        private async Task BuildSingleAsync(object? parameter)
        {
            if (parameter is not GitProject project) return;

            if (project.SelectedMSBuildVersion == null)
            {
                _appendLog($"[{project.Name}] 请先选择 MSBuild 版本\n");
                return;
            }

            _outputViewModel.LogOutput = string.Empty;
            _appendLog($"\n========== 构建项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsBuilding = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg => _appendLog($"[{project.Name}] {msg}\n"));
                _appendLog($"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion.DisplayName}, 配置: {project.Configuration}\n");
                var result = await _msbuildService.BuildProjectAsync(project, project.Configuration, project.SelectedMSBuildVersion, progress);

                if (result.Success)
                {
                    project.IsExpanded = false;
                    _appendLog($"[{project.Name}] 构建成功\n");
                }
                else
                {
                    project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                    _appendLog($"[{project.Name}] 构建失败: {result.ErrorMessage}\n");
                }
            }
            catch (Exception ex)
            {
                _appendLog($"[{project.Name}] 构建异常: {ex.Message}\n");
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsBuilding = false;
            }
        }

        private async Task RunSelectedAsync(object? parameter)
        {
            GitProject? project = parameter as GitProject ?? SelectedItem;
            if (project == null || string.IsNullOrEmpty(project.ExecuteFile))
            {
                _appendLog("无可运行的项目\n");
                return;
            }

            _appendLog($"\n========== 运行项目: {project.Name} ==========\n");

            try
            {
                var exePath = project.ExecuteFile;
                var workingDir = System.IO.Path.GetDirectoryName(exePath) ?? project.Path;

                _appendLog($"[{project.Name}] 启动: {exePath}\n");

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
                _appendLog($"[{project.Name}] 运行失败: {ex.Message}\n");
            }
        }
    }
}
