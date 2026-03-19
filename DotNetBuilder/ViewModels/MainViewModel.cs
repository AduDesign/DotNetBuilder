using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 主窗口ViewModel
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly GitService _gitService;
        private readonly MSBuildService _msbuildService;
        private readonly ConfigService _configService;
        private string _selectedPath = string.Empty;
        private string _logOutput = string.Empty;
        private bool _isBusy;
        private MSBuildVersion? _selectedMSBuildVersion;
        private GitProject? _selectedProject;
        private string _selectedExecutable = string.Empty;

        public MainViewModel()
        {
            _gitService = new GitService();
            _msbuildService = new MSBuildService();
            _configService = new ConfigService();

            // 初始化命令
            SelectDirectoryCommand = new AsyncRelayCommand(SelectDirectoryAsync);
            ScanProjectsCommand = new AsyncRelayCommand(ScanProjectsAsync);
            SyncSelectedCommand = new AsyncRelayCommand(SyncSelectedAsync, () => !IsBusy && SelectedProjects.Any());
            BuildSelectedCommand = new AsyncRelayCommand(BuildSelectedAsync, () => !IsBusy && SelectedProjects.Any(p => p.IsDotNetProject));
            RunSelectedCommand = new RelayCommand(RunSelectedAsync, _ => SelectedProject != null && !string.IsNullOrEmpty(SelectedProject.ExecuteFile));
            MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
            SelectAllCommand = new RelayCommand(SelectAll);
            SelectNoneCommand = new RelayCommand(SelectNone);
            ClearLogCommand = new RelayCommand(_ => LogOutput = string.Empty);
            RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
            SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync, () => Projects.Any());

            // 单个项目操作命令
            SyncSingleCommand = new AsyncRelayCommand(SyncSingleAsync);
            BuildSingleCommand = new AsyncRelayCommand(BuildSingleAsync);

            // 加载MSBuild版本
            LoadMSBuildVersions();

            // 自动加载配置
            _ = LoadConfigAsync();
        }

        #region 属性

        public ObservableCollection<GitProject> Projects { get; } = new();

        public ObservableCollection<MSBuildVersion> MSBuildVersions { get; } = new();

        public ObservableCollection<string> Executables { get; } = new();

        public string SelectedPath
        {
            get => _selectedPath;
            set
            {
                if (SetProperty(ref _selectedPath, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPath));
                }
            }
        }

        public bool HasSelectedPath => !string.IsNullOrEmpty(SelectedPath);

        public string LogOutput
        {
            get => _logOutput;
            set => SetProperty(ref _logOutput, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public MSBuildVersion? SelectedMSBuildVersion
        {
            get => _selectedMSBuildVersion;
            set
            {
                if (SetProperty(ref _selectedMSBuildVersion, value))
                {
                    _msbuildService.SelectedVersion = value;
                }
            }
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

        public string SelectedExecutable
        {
            get => _selectedExecutable;
            set => SetProperty(ref _selectedExecutable, value);
        }

        /// <summary>
        /// 选中的项目列表
        /// </summary>
        public IEnumerable<GitProject> SelectedProjects => Projects.Where(p => p.IsSelected);

        #endregion

        #region 命令

        public ICommand SelectDirectoryCommand { get; }
        public ICommand ScanProjectsCommand { get; }
        public ICommand SyncSelectedCommand { get; }
        public ICommand BuildSelectedCommand { get; }
        public ICommand RunSelectedCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand RefreshStatusCommand { get; }
        public ICommand SyncSingleCommand { get; }
        public ICommand BuildSingleCommand { get; }
        public ICommand SaveConfigCommand { get; }

        #endregion

        #region 方法

        private void LoadMSBuildVersions()
        {
            var versions = _msbuildService.DetectMSBuildVersions();
            MSBuildVersions.Clear();
            foreach (var version in versions)
            {
                MSBuildVersions.Add(version);
            }

            if (MSBuildVersions.Count > 0)
            {
                SelectedMSBuildVersion = MSBuildVersions[0];
            }
        }

        private async Task SelectDirectoryAsync()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择包含Git项目的目录"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedPath = dialog.FolderName;
                await ScanProjectsAsync();
            }
        }

        private async Task ScanProjectsAsync()
        {
            if (string.IsNullOrEmpty(SelectedPath))
                return;

            IsBusy = true;

            // 在UI线程上清空列表
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Projects.Clear();
                LogOutput = $"正在扫描: {SelectedPath}\n";
            });

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    // 进度更新在UI线程上执行
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LogOutput += msg + "\n";
                    });
                });

                // 先获取所有项目（后台线程）
                var projects = await _gitService.ScanGitProjectsAsync(SelectedPath, progress);

                // 逐个添加项目到UI线程
                int sortOrder = 0;
                foreach (var project in projects)
                {
                    // 设置项目文件和解决方案文件路径
                    project.SolutionPath = _gitService.GetSolutionPath(project.Path);
                    project.ProjectFilePath = _gitService.GetProjectFilePath(project.Path);
                    project.SortOrder = sortOrder++;
                    // 设置默认MSBuild版本（如果有可用版本）
                    if (MSBuildVersions.Count > 0)
                    {
                        project.SelectedMSBuildVersion = MSBuildVersions[0];
                    }
                    Projects.Add(project);
                }

                // 在UI线程上更新日志
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LogOutput += $"\n扫描完成，发现 {projects.Count} 个Git项目\n";
                    if (projects.Count > 0)
                    {
                        var dotnetCount = projects.Count(p => p.IsDotNetProject);
                        LogOutput += $"其中 {dotnetCount} 个是.NET项目\n";
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LogOutput += $"\n扫描失败: {ex.Message}\n";
                });
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshStatusAsync()
        {
            IsBusy = true;
            LogOutput += "\n正在刷新项目状态...\n";

            try
            {
                foreach (var project in Projects)
                {
                    await _gitService.UpdateProjectStatusAsync(project);
                }
                LogOutput += "状态刷新完成\n";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SyncSelectedAsync()
        {
            var selectedProjects = SelectedProjects.ToList();
            if (!selectedProjects.Any())
            {
                AduMessageBox.Show("请先选择要同步的项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            LogOutput += $"\n========== 开始同步 {selectedProjects.Count} 个项目 ==========\n";

            try
            {
                // 并行同步所有项目
                var tasks = selectedProjects.Select(async project =>
                {
                    // 设置同步状态
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        project.ClearError();
                        project.IsSyncing = true;
                        project.IsExpanded = true; // 自动展开
                    });

                    try
                    {
                        var progress = new Progress<string>(msg =>
                        {
                            LogOutput += $"[{project.Name}] {msg}\n";
                        });

                        await _gitService.UpdateProjectStatusAsync(project);
                        var commitMsg = project.CommitMessage;
                        var result = await _gitService.SyncProjectAsync(project, commitMsg, progress);

                        if (result.Success)
                        {
                            await _gitService.UpdateProjectStatusAsync(project);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                project.IsExpanded = false; // 成功则收起
                            });
                        }
                        else
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                project.ErrorMessage = result.Message;
                                project.IsExpanded = true; // 失败则保持展开
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOutput += $"[{project.Name}] 同步异常: {ex.Message}\n";
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

                LogOutput += $"\n========== 同步完成 ==========\n";
            }
            catch (Exception ex)
            {
                LogOutput += $"\n同步出错: {ex.Message}\n";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task BuildSelectedAsync()
        {
            var selectedProjects = SelectedProjects.Where(p => p.IsDotNetProject).ToList();
            if (!selectedProjects.Any())
            {
                AduMessageBox.Show("请先选择要构建的.NET项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 检查每个项目是否有选择MSBuild版本
            var projectsWithoutMSBuild = selectedProjects.Where(p => p.SelectedMSBuildVersion == null).ToList();
            if (projectsWithoutMSBuild.Any())
            {
                var names = string.Join(", ", projectsWithoutMSBuild.Select(p => p.Name));
                AduMessageBox.Show($"以下项目未选择MSBuild版本:\n{names}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            LogOutput += $"\n========== 开始构建 {selectedProjects.Count} 个项目 ==========\n";

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    LogOutput += msg + "\n";
                });

                bool buildFailed = false;
                foreach (var project in selectedProjects.OrderBy(p => p.SortOrder))
                {
                    // 如果之前有项目构建失败，跳过后续项目
                    if (buildFailed)
                    {
                        LogOutput += $"[{project.Name}] 跳过多项目中构建（因前置项目构建失败）\n";
                        continue;
                    }

                    // 清除之前的错误
                    await Application.Current.Dispatcher.InvokeAsync(() => project.ClearError());

                    // 设置构建状态
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        project.IsBuilding = true;
                        project.IsExpanded = true; // 自动展开
                    });

                    try
                    {
                        LogOutput += $"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion?.DisplayName}\n";
                        var result = await _msbuildService.BuildProjectAsync(project, "Release", project.SelectedMSBuildVersion, progress);

                        if (result.Success)
                        {
                            LogOutput += $"[{project.Name}] 构建成功，耗时: {result.Duration.TotalSeconds:F1}s\n";
                            await Application.Current.Dispatcher.InvokeAsync(() => project.IsExpanded = false);
                        }
                        else
                        {
                            LogOutput += $"[{project.Name}] 构建失败: {result.ErrorMessage}\n";
                            buildFailed = true;
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                                project.IsExpanded = true; // 保持展开显示错误
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOutput += $"[{project.Name}] 构建异常: {ex.Message}\n";
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

                LogOutput += $"\n========== 构建完成 ==========\n";

                // 刷新选中项目的exe列表
                LoadProjectExecutables();
            }
            catch (Exception ex)
            {
                LogOutput += $"\n构建出错: {ex.Message}\n";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadProjectExecutables()
        {
            Executables.Clear();

            if (SelectedProject == null)
                return;

            var exeFiles = _msbuildService.FindOutputExecutables(SelectedProject.Path);
            foreach (var exe in exeFiles)
            {
                Executables.Add(exe);
            }

            if (Executables.Count > 0)
            {
                SelectedExecutable = Executables[0];
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                await _configService.SaveConfigAsync(Projects, SelectedPath);
                LogOutput += $"\n配置已保存\n";
                AduMessageBox.Show("配置保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogOutput += $"\n保存配置失败: {ex.Message}\n";
                AduMessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                var config = await _configService.LoadConfigAsync();
                if (config == null || string.IsNullOrEmpty(config.SelectedPath))
                    return;

                // 设置路径并扫描
                SelectedPath = config.SelectedPath;
                LogOutput += $"正在加载上次配置...\n";
                await ScanProjectsAsync();

                // 应用项目配置
                foreach (var projectConfig in config.Projects)
                {
                    var project = Projects.FirstOrDefault(p => p.Path == projectConfig.Path);
                    if (project != null)
                    {
                        project.IsSelected = projectConfig.IsSelected;
                        project.ExecuteFile = projectConfig.ExecuteFile ?? string.Empty;

                        // 恢复 MSBuild 版本
                        if (!string.IsNullOrEmpty(projectConfig.SelectedMSBuildVersion))
                        {
                            project.SelectedMSBuildVersion = MSBuildVersions.FirstOrDefault(v => v.DisplayName == projectConfig.SelectedMSBuildVersion);
                        }
                    }
                }

                LogOutput += $"配置加载完成\n";
            }
            catch
            {
                // 加载失败时静默忽略
            }
        }

        private void RunSelectedAsync(object? parameter)
        {
            if (SelectedProject == null || string.IsNullOrEmpty(SelectedProject.ExecuteFile))
                return;

            var exePath = SelectedProject.ExecuteFile;

            try
            {
                LogOutput += $"\n启动程序 [{SelectedProject.Name}]: {exePath}\n";
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogOutput += $"启动失败: {ex.Message}\n";
                AduMessageBox.Show($"启动程序失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void SelectAll(object? parameter)
        {
            foreach (var project in Projects)
            {
                project.IsSelected = true;
            }
        }

        private void SelectNone(object? parameter)
        {
            foreach (var project in Projects)
            {
                project.IsSelected = false;
            }
        }

        /// <summary>
        /// 同步单个项目
        /// </summary>
        private async Task SyncSingleAsync(object? parameter)
        {
            if (parameter is not GitProject project)
                return;

            LogOutput += $"\n========== 同步项目: {project.Name} ==========\n";

            try
            {
                project.ClearError();
                project.IsSyncing = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg =>
                {
                    LogOutput += $"[{project.Name}] {msg}\n";
                });

                await _gitService.UpdateProjectStatusAsync(project);
                var result = await _gitService.SyncProjectAsync(project, project.CommitMessage, progress);

                if (result.Success)
                {
                    await _gitService.UpdateProjectStatusAsync(project);
                    project.IsExpanded = false;
                    LogOutput += $"[{project.Name}] 同步成功\n";
                }
                else
                {
                    project.ErrorMessage = result.Message;
                    LogOutput += $"[{project.Name}] 同步失败: {result.Message}\n";
                }
            }
            catch (Exception ex)
            {
                LogOutput += $"[{project.Name}] 同步异常: {ex.Message}\n";
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsSyncing = false;
            }
        }

        /// <summary>
        /// 构建单个项目
        /// </summary>
        private async Task BuildSingleAsync(object? parameter)
        {
            if (parameter is not GitProject project)
                return;

            if (project.SelectedMSBuildVersion == null)
            {
                AduMessageBox.Show($"请先为 {project.Name} 选择 MSBuild 版本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LogOutput += $"\n========== 构建项目: {project.Name} ==========\n";

            try
            {
                project.ClearError();
                project.IsBuilding = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg =>
                {
                    LogOutput += $"[{project.Name}] {msg}\n";
                });

                LogOutput += $"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion.DisplayName}\n";
                var result = await _msbuildService.BuildProjectAsync(project, "Release", project.SelectedMSBuildVersion, progress);

                if (result.Success)
                {
                    project.IsExpanded = false;
                    LogOutput += $"[{project.Name}] 构建成功，耗时: {result.Duration.TotalSeconds:F1}s\n";
                    // 如果当前选中的是这个项目，刷新exe列表
                    if (SelectedProject == project)
                    {
                        LoadProjectExecutables();
                    }
                }
                else
                {
                    project.ErrorMessage = result.ErrorMessage ?? "构建失败";
                    LogOutput += $"[{project.Name}] 构建失败: {result.ErrorMessage}\n";
                }
            }
            catch (Exception ex)
            {
                LogOutput += $"[{project.Name}] 构建异常: {ex.Message}\n";
                project.ErrorMessage = ex.Message;
            }
            finally
            {
                project.IsBuilding = false;
            }
        }

        #endregion
    }
}
