using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetBuilder.Models;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// 配置服务 - 保存和加载项目配置
    /// </summary>
    public class ConfigService
    {
        private readonly string _configFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public ConfigService()
        {
            // 保存到应用程序目录下的 config.json
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(appDir, "config.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public async Task SaveConfigAsync(IEnumerable<GitProject> projects, string selectedPath)
        {
            try
            {
                var config = new AppConfig
                {
                    SelectedPath = selectedPath,
                    Projects = projects.Select(p => new ProjectConfig
                    {
                        Path = p.Path,
                        IsSelected = p.IsSelected,
                        ExecuteFile = p.ExecuteFile,
                        SelectedMSBuildVersion = p.SelectedMSBuildVersion?.DisplayName,
                        Configuration = p.Configuration
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存配置失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        public async Task<AppConfig?> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                    return null;

                var json = await File.ReadAllTextAsync(_configFilePath);
                return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 配置文件是否存在
        /// </summary>
        public bool ConfigExists => File.Exists(_configFilePath);
    }

    /// <summary>
    /// 应用程序配置
    /// </summary>
    public class AppConfig
    {
        public string? SelectedPath { get; set; }
        public List<ProjectConfig> Projects { get; set; } = new();
    }

    /// <summary>
    /// 项目配置
    /// </summary>
    public class ProjectConfig
    {
        public string Path { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
        public string? ExecuteFile { get; set; }
        public string? SelectedMSBuildVersion { get; set; }
        public string Configuration { get; set; } = "Release";
    }
}
