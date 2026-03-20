using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AduSkin.Controls;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 日志类型枚举
    /// </summary>
    public enum LogType
    {
        Message,  // 普通消息
        Error,    // 错误
        Warning,  // 警告
        Git,      // Git操作
        Build     // Build编译日志（MSBuild详细输出）
    }

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

        // 日志过滤选项
        private bool _showErrorLog = true;
        private bool _showWarningLog = true;
        private bool _showMessageLog = true;
        private bool _showGitLog = true;
        private bool _showBuildLog = false; // Build编译日志，默认不显示

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
            RemoveProjectCommand = new RelayCommand(RemoveProject);

            // 加载MSBuild版本
            LoadMSBuildVersions();

            // 自动加载配置
            _ = LoadConfigAsync();
        }

        #region 属性

        public ObservableCollection<GitProject> Projects { get; } = new();

        public ObservableCollection<MSBuildVersion> MSBuildVersions { get; } = new();

        public ObservableCollection<string> Executables { get; } = new();

        public ObservableCollection<string> ConfigurationTypes { get; } = new() { "Release", "Debug" };

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

        /// <summary>
        /// 是否显示错误日志
        /// </summary>
        public bool ShowErrorLog
        {
            get => _showErrorLog;
            set => SetProperty(ref _showErrorLog, value);
        }

        /// <summary>
        /// 是否显示警告日志
        /// </summary>
        public bool ShowWarningLog
        {
            get => _showWarningLog;
            set => SetProperty(ref _showWarningLog, value);
        }

        /// <summary>
        /// 是否显示消息日志
        /// </summary>
        public bool ShowMessageLog
        {
            get => _showMessageLog;
            set => SetProperty(ref _showMessageLog, value);
        }

        /// <summary>
        /// 是否显示Git日志
        /// </summary>
        public bool ShowGitLog
        {
            get => _showGitLog;
            set => SetProperty(ref _showGitLog, value);
        }

        /// <summary>
        /// 是否显示Build编译日志（MSBuild详细输出）
        /// </summary>
        public bool ShowBuildLog
        {
            get => _showBuildLog;
            set => SetProperty(ref _showBuildLog, value);
        }

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
        public ICommand RemoveProjectCommand { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 根据日志内容自动分类并输出日志
        /// </summary>
        /// <param name="message">日志消息</param>
        private void AppendLog(string message)
        {
            // 自动根据内容分类日志类型
            var logType = ClassifyLogType(message);

            // 检查是否应该显示该类型的日志
            bool shouldShow = logType switch
            {
                LogType.Error => ShowErrorLog,
                LogType.Warning => ShowWarningLog,
                LogType.Message => ShowMessageLog,
                LogType.Git => ShowGitLog,
                LogType.Build => ShowBuildLog,
                _ => true
            };

            if (shouldShow)
            {
                LogOutput += message;
            }
        }

        /// <summary>
        /// 根据日志内容自动分类日志类型
        /// </summary>
        private LogType ClassifyLogType(string message)
        {
            // 错误日志
            if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("错误") ||
                message.Contains("异常") ||
                message.Contains("失败") ||
                message.Contains("FAILED") ||
                message.Contains("FAIL:"))
            {
                return LogType.Error;
            }

            // 警告日志
            if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("警告") ||
                message.Contains("WARN:"))
            {
                return LogType.Warning;
            }

            // Build编译日志（MSBuild详细输出）
            // 包含文件路径、编译过程、目录信息等
            if (IsBuildDetailedLog(message))
            {
                return LogType.Build;
            }

            // Git相关日志
            if (message.Contains("[NuGet]", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("NuGet") ||
                message.Contains("git", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Git") ||
                message.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("push", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("暂存", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("提交", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("拉取", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("同步", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在扫描", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("扫描完成", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在检测", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在检查", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("发现", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("更新成功", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("还原成功", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("无需还原", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("还原完成", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在还原", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在编译", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("开始构建", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("构建成功", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("构建目标", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("使用 MSBuild", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("输出目录", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("可执行文件", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在拉取更新", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("有未提交的更改", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在扫描", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("正在加载", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("配置加载", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("配置已保存", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("已跳过", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("跳过多项目", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("==========", StringComparison.OrdinalIgnoreCase))
            {
                return LogType.Git;
            }

            return LogType.Message;
        }

        /// <summary>
        /// 判断是否为Build编译详细日志（MSBuild的详细输出）
        /// </summary>
        private bool IsBuildDetailedLog(string message)
        {
            // 去掉开头和结尾的空白
            message = message.Trim();

            // 如果消息太短或太长，不太可能是详细编译日志
            if (message.Length < 10 || message.Length > 500)
                return false;

            // Build详细日志的特征：
            // 1. 包含反斜杠的文件路径 (如 C:\, D:\)
            // 2. 包含编译文件列表 (如 .cs, .xaml, .resx 等)
            // 3. 包含 "->" 箭头（表示输出）
            // 4. 包含 "Copy" 复制操作
            // 5. 包含 "Generate" 生成操作
            // 6. 包含目录路径模式

            // 检查是否包含典型的文件扩展名
            var fileExtensions = new[] { ".cs", ".xaml", ".resx", ".csproj", ".sln", ".json", ".xml", ".config" };
            bool hasFileExtension = fileExtensions.Any(ext =>
                message.Contains(ext, StringComparison.OrdinalIgnoreCase));

            // 检查是否包含文件路径模式 (如 C:\ 或 \bin\)
            bool hasFilePath = (message.Contains(":\\") || message.Contains("\\bin\\") ||
                                message.Contains("\\obj\\") || message.Contains("\\Properties\\"));

            // 检查是否包含 Build 详细日志的关键词
            bool hasBuildKeyword = message.Contains("->") || message.Contains("Copy ") ||
                                   message.Contains("Generate") || message.Contains("Task ") ||
                                   message.Contains("Target ") || message.Contains("CoreCompile") ||
                                   message.Contains("ResolveAssemblyReferences") ||
                                   message.Contains(".dll") || message.Contains(".exe");

            // 如果同时有文件扩展名和文件路径，很可能是详细编译日志
            if (hasFileExtension && hasFilePath)
                return true;

            // 如果有 Build 关键词和文件路径
            if (hasBuildKeyword && hasFilePath)
                return true;

            // 检查是否是多行的详细输出（包含多个路径）
            if (hasFilePath && message.Contains("\n"))
                return true;

            return false;
        }

        /// <summary>
        /// 输出日志（带换行，自动分类）
        /// </summary>
        private void AppendLogLine(string message)
        {
            AppendLog(message + "\n");
        }

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
                AppendLog($"正在扫描: {SelectedPath}\n");
            });

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    // 进度更新在UI线程上执行
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog(msg + "\n");
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
                //选中项
                SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));

                // 在UI线程上更新日志
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AppendLog($"\n扫描完成，发现 {projects.Count} 个Git项目\n");
                    if (projects.Count > 0)
                    {
                        var dotnetCount = projects.Count(p => p.IsDotNetProject);
                        AppendLog($"其中 {dotnetCount} 个是.NET项目\n");
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AppendLog($"\n扫描失败: {ex.Message}\n");
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
            AppendLog("\n正在刷新项目状态...\n");

            try
            {
                foreach (var project in Projects)
                {
                    await _gitService.UpdateProjectStatusAsync(project);
                }
                AppendLog("状态刷新完成\n");
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
            AppendLog($"\n========== 开始同步 {selectedProjects.Count} 个项目 ==========\n");

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
                            AppendLog($"[{project.Name}] {msg}\n");
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
            catch (Exception ex)
            {
                AppendLog($"\n同步出错: {ex.Message}\n");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 生成选择(多选)
        /// </summary>
        /// <returns></returns>
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
            AppendLog($"\n========== 开始构建 {selectedProjects.Count} 个项目 ==========\n");

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    AppendLog(msg + "\n");
                });

                bool buildFailed = false;
                foreach (var project in selectedProjects.OrderBy(p => p.SortOrder))
                {
                    // 如果之前有项目构建失败，跳过后续项目
                    if (buildFailed)
                    {
                        AppendLog($"[{project.Name}] 跳过多项目中构建（因前置项目构建失败）\n");
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
                                project.IsExpanded = true; // 保持展开显示错误
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

                // 刷新选中项目的exe列表
                LoadProjectExecutables();
            }
            catch (Exception ex)
            {
                AppendLog($"\n构建出错: {ex.Message}\n");
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
                AppendLog("\n配置已保存\n");
                AduMessageBox.Show("配置保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"\n保存配置失败: {ex.Message}\n");
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
                AppendLog("正在加载上次配置...\n");
                await ScanProjectsAsync();

                // 收集已移除的项目路径
                var removedPaths = config.Projects
                    .Where(p => p.IsRemoved)
                    .Select(p => p.Path)
                    .ToHashSet();

                // 移除已标记为移除的项目
                var toRemove = Projects.Where(p => removedPaths.Contains(p.Path)).ToList();
                foreach (var project in toRemove)
                {
                    Projects.Remove(project);
                }

                // 应用项目配置（按Order排序）
                var sortedConfigs = config.Projects
                    .Where(p => !p.IsRemoved)  // 跳过已移除的项目
                    .OrderBy(p => p.Order)
                    .ToList();

                foreach (var projectConfig in sortedConfigs)
                {
                    var project = Projects.FirstOrDefault(p => p.Path == projectConfig.Path);
                    if (project != null)
                    {
                        project.IsSelected = projectConfig.IsSelected;
                        project.ExecuteFile = projectConfig.ExecuteFile ?? string.Empty;
                        project.Configuration = projectConfig.Configuration;
                        project.SortOrder = projectConfig.Order;
                        project.IsRemoved = false; // 重置移除状态

                        // 恢复 MSBuild 版本
                        if (!string.IsNullOrEmpty(projectConfig.SelectedMSBuildVersion))
                        {
                            project.SelectedMSBuildVersion = MSBuildVersions.FirstOrDefault(v => v.DisplayName == projectConfig.SelectedMSBuildVersion);
                        }
                    }
                }

                // 按Order重新排序Projects集合
                var sorted = Projects.OrderBy(p => p.SortOrder).ToList();
                Projects.Clear();
                foreach (var project in sorted)
                    Projects.Add(project);
                SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));

                if (removedPaths.Count > 0)
                {
                    AppendLog($"已跳过 {removedPaths.Count} 个已移除的项目\n");
                }

                AppendLog("配置加载完成\n");
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
                AppendLog($"\n启动程序 [{SelectedProject.Name}]: {exePath}\n");
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendLog($"启动失败: {ex.Message}\n");
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
        /// 移除项目（标记为已移除，保存后将不再加载）
        /// </summary>
        private void RemoveProject(object? parameter)
        {
            if (parameter is GitProject project)
            {
                project.IsSelected = false; // 取消选中
                project.IsRemoved = true;   // 标记为已移除
                Projects.Remove(project);    // 从列表移除
                AppendLog($"已移除项目: {project.Name}\n");

                // 如果移除的是当前选中的项目，清空选择
                if (SelectedProject == project)
                    SelectedProject = Projects.FirstOrDefault(s => !string.IsNullOrEmpty(s.ExecuteFile));
            }
        }

        /// <summary>
        /// 同步单个项目
        /// </summary>
        private async Task SyncSingleAsync(object? parameter)
        {
            if (parameter is not GitProject project)
                return;

            AppendLog($"\n========== 同步项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsSyncing = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg =>
                {
                    AppendLog($"[{project.Name}] {msg}\n");
                });

                await _gitService.UpdateProjectStatusAsync(project);
                var result = await _gitService.SyncProjectAsync(project, project.CommitMessage, progress);

                if (result.Success)
                {
                    await _gitService.UpdateProjectStatusAsync(project);
                    project.IsExpanded = false;
                    AppendLog($"[{project.Name}] 同步成功\n");
                }
                else
                {
                    project.ErrorMessage = result.Message;
                    AppendLog($"[{project.Name}] 同步失败: {result.Message}\n");
                }
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

            AppendLog($"\n========== 构建项目: {project.Name} ==========\n");

            try
            {
                project.ClearError();
                project.IsBuilding = true;
                project.IsExpanded = true;

                var progress = new Progress<string>(msg =>
                {
                    AppendLog($"[{project.Name}] {msg}\n");
                });

                AppendLog($"[{project.Name}] 使用 MSBuild: {project.SelectedMSBuildVersion.DisplayName}, 配置: {project.Configuration}\n");
                var result = await _msbuildService.BuildProjectAsync(project, project.Configuration, project.SelectedMSBuildVersion, progress);

                if (result.Success)
                {
                    project.IsExpanded = false;
                    // 如果当前选中的是这个项目，刷新exe列表
                    if (SelectedProject == project)
                    {
                        LoadProjectExecutables();
                    }
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

        #endregion
    }
}
