using System.IO;
using System.Text.Json;
using DotNetBuilder.Models;
using ConfigService = DotNetBuilder.Services.ConfigService;
using AppConfigModel = DotNetBuilder.Models.AppConfig;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 项目服务 - 负责项目文件的创建、打开、保存
    /// </summary>
    public class ProjectService
    {
        private const string ProjectExtension = ".bdproj";
        private const string AppConfigFileName = "app.json";
        private const int MaxRecentProjects = 10;

        private readonly string _appConfigPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private ProjectInfo? _currentProject;

        public event Action<ProjectInfo>? OnProjectLoaded;
        public event Action? OnProjectClosed;

        public ProjectService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DotNetBuilder");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _appConfigPath = Path.Combine(appDataPath, AppConfigFileName);

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        /// <summary>
        /// 当前是否有打开的项目
        /// </summary>
        public bool HasProject => _currentProject != null;

        /// <summary>
        /// 当前项目
        /// </summary>
        public ProjectInfo? CurrentProject => _currentProject;

        /// <summary>
        /// 设置当前项目
        /// </summary>
        public void SetCurrentProject(ProjectInfo? project)
        {
            var hadProject = _currentProject != null;
            _currentProject = project;
            if (project != null)
            {
                OnProjectLoaded?.Invoke(project);
            }
            else if (hadProject)
            {
                OnProjectClosed?.Invoke();
            }
        }

        /// <summary>
        /// 创建新项目
        /// </summary>
        public ProjectInfo CreateProject(string name, string rootPath)
        {
            return new ProjectInfo
            {
                Name = name,
                RootPath = rootPath,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };
        }

        /// <summary>
        /// 保存项目
        /// </summary>
        public async Task SaveProjectAsync(ProjectInfo project, string filePath)
        {
            project.FilePath = filePath;
            project.ModifiedAt = DateTime.Now;

            // 更新当前项目的 FilePath
            if (_currentProject != null && _currentProject.Name == project.Name)
            {
                _currentProject.FilePath = filePath;
            }

            var json = JsonSerializer.Serialize(project, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// 打开项目
        /// </summary>
        public async Task<ProjectInfo?> OpenProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            var project = JsonSerializer.Deserialize<ProjectInfo>(json, _jsonOptions);

            if (project != null)
            {
                project.FilePath = filePath;
            }

            return project;
        }

        /// <summary>
        /// 加载 App 配置
        /// </summary>
        public async Task<AppConfigModel> LoadAppConfigAsync()
        {
            if (!File.Exists(_appConfigPath))
                return new AppConfigModel();

            try
            {
                var json = await File.ReadAllTextAsync(_appConfigPath);
                return JsonSerializer.Deserialize<AppConfigModel>(json, _jsonOptions) ?? new AppConfigModel();
            }
            catch
            {
                return new AppConfigModel();
            }
        }

        /// <summary>
        /// 保存 App 配置
        /// </summary>
        public async Task SaveAppConfigAsync(AppConfigModel config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_appConfigPath, json);
        }

        /// <summary>
        /// 添加最近项目
        /// </summary>
        public async Task AddRecentProjectAsync(string name, string filePath)
        {
            var config = await LoadAppConfigAsync();

            // 移除已存在的同名项目
            config.RecentProjects.RemoveAll(p => p.FilePath == filePath);

            // 添加到列表开头
            config.RecentProjects.Insert(0, new RecentProject
            {
                Name = name,
                FilePath = filePath,
                LastOpenedAt = DateTime.Now
            });

            // 限制数量
            if (config.RecentProjects.Count > MaxRecentProjects)
            {
                config.RecentProjects = config.RecentProjects.Take(MaxRecentProjects).ToList();
            }

            config.LastProjectPath = filePath;

            await SaveAppConfigAsync(config);
        }

        /// <summary>
        /// 获取最近项目
        /// </summary>
        public async Task<List<RecentProject>> GetRecentProjectsAsync()
        {
            var config = await LoadAppConfigAsync();
            return config.RecentProjects
                .Where(p => File.Exists(p.FilePath))
                .ToList();
        }

        /// <summary>
        /// 获取文件过滤器
        /// </summary>
        public string GetProjectFileFilter()
        {
            return $"DotNetBuilder 项目 (*.bdproj)|*.bdproj|所有文件 (*.*)|*.*";
        }

        /// <summary>
        /// 从命令行参数获取项目文件路径
        /// </summary>
        public string? GetProjectFromArgs(string[] args)
        {
            if (args.Length > 0)
            {
                var arg = args[0];
                if (File.Exists(arg) && arg.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return arg;
                }
            }
            return null;
        }
    }
}
