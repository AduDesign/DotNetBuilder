using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;
using DotNetBuilder.Models;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// Git操作服务
    /// </summary>
    public class GitService
    {
        /// <summary>
        /// 扫描目录下的所有Git项目（异步）
        /// </summary>
        public async Task<List<GitProject>> ScanGitProjectsAsync(string rootPath, IProgress<string>? progress = null)
        {
            var projects = new List<GitProject>();

            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return projects;

            progress?.Report($"正在扫描目录: {rootPath}");

            try
            {
                // 先检查根目录本身是否就是Git项目目录
                var rootGitDir = Path.Combine(rootPath, ".git");
                bool rootIsGit = await Task.Run(() => Directory.Exists(rootGitDir));

                // 将同步的目录扫描操作放到后台线程
                var directories = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
                    }
                    catch
                    {
                        return Array.Empty<string>();
                    }
                });

                int found = 0;
                foreach (var dir in directories)
                {
                    // 检查是否包含.git目录
                    var gitDir = Path.Combine(dir, ".git");
                    bool hasGit = await Task.Run(() => Directory.Exists(gitDir));

                    if (hasGit)
                    {
                        var project = new GitProject
                        {
                            Name = Path.GetFileName(dir),
                            Path = dir,
                            IsDotNetProject = false, // 稍后异步检测
                            ProjectType = "Git Repository"
                        };

                        projects.Add(project);
                        found++;

                        // 定期报告进度
                        if (found % 10 == 0)
                        {
                            progress?.Report($"已发现 {found} 个Git项目...");
                        }
                    }
                }

                // 如果根目录本身就是Git项目，添加到列表
                if (rootIsGit)
                {
                    var rootProject = new GitProject
                    {
                        Name = Path.GetFileName(rootPath),
                        Path = rootPath,
                        IsDotNetProject = false,
                        ProjectType = "Git Repository"
                    };
                    projects.Add(rootProject);
                    found++;
                }

                // 批量检测.NET项目（异步）
                progress?.Report($"正在检测项目类型...");
                var dotnetProjects = await Task.Run(() =>
                    projects.Where(p => DetectDotNetProject(p.Path)).ToList());

                foreach (var project in dotnetProjects)
                {
                    project.IsDotNetProject = true;
                    project.ProjectType = GetProjectTypeDescription(project.Path);
                }

                // 异步获取所有项目的git状态
                progress?.Report($"正在检查Git状态...");
                var statusTasks = projects.Select(p => UpdateProjectStatusAsync(p));
                await Task.WhenAll(statusTasks);
            }
            catch (Exception ex)
            {
                progress?.Report($"扫描出错: {ex.Message}");
            }

            // 按名称排序
            return projects.OrderBy(p => p.Name).ToList();
        }

        /// <summary>
        /// 手动添加一个Git项目（验证目录包含.git）
        /// </summary>
        public async Task<GitProject?> AddGitProjectAsync(string projectPath, IProgress<string>? progress = null)
        {
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
                return null;

            var gitDir = Path.Combine(projectPath, ".git");
            bool hasGit = await Task.Run(() => Directory.Exists(gitDir));
            if (!hasGit)
                return null;

            var project = new GitProject
            {
                Name = Path.GetFileName(projectPath),
                Path = projectPath,
                IsDotNetProject = false,
                ProjectType = "Git Repository"
            };

            // 检测项目类型
            if (DetectDotNetProject(projectPath))
            {
                project.IsDotNetProject = true;
                project.ProjectType = GetProjectTypeDescription(projectPath);
            }

            await UpdateProjectStatusAsync(project);
            return project;
        }

        /// <summary>
        /// 检测是否为.NET项目
        /// </summary>
        private bool DetectDotNetProject(string path)
        {
            // 查找.sln文件
            var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
                return true;

            // 查找.csproj文件
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
                return true;

            // 查找.vbproj文件
            var vbprojFiles = Directory.GetFiles(path, "*.vbproj", SearchOption.AllDirectories);
            if (vbprojFiles.Length > 0)
                return true;

            return false;
        }

        /// <summary>
        /// 获取项目类型描述
        /// </summary>
        private string GetProjectTypeDescription(string path)
        {
            // 检查解决方案文件
            var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
                return "Solution";

            // 检查C#项目
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
                return "C# Project";

            // 检查VB项目
            var vbprojFiles = Directory.GetFiles(path, "*.vbproj", SearchOption.AllDirectories);
            if (vbprojFiles.Length > 0)
                return "VB Project";

            // 检查其他项目类型
            var fsprojFiles = Directory.GetFiles(path, "*.fsproj", SearchOption.AllDirectories);
            if (fsprojFiles.Length > 0)
                return "F# Project";

            return "Git Repository";
        }

        /// <summary>
        /// 更新项目状态（检查是否有未提交的更改）
        /// </summary>
        public async Task UpdateProjectStatusAsync(GitProject project)
        {
            try
            {
                var result = await RunGitCommandAsync(project.Path, "status --porcelain");
                var hasChanges = !string.IsNullOrWhiteSpace(result);
                var changesCount = string.IsNullOrWhiteSpace(result) ? 0 :
                    result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

                // 注意：由于这是跨线程更新，需要在UI线程上执行
                project.HasChanges = hasChanges;
                project.IsExpanded = hasChanges;
                project.ChangesCount = changesCount;
            }
            catch
            {
                project.IsExpanded = project.HasChanges = false;
                project.ChangesCount = 0;
            }
        }

        /// <summary>
        /// 同步Git项目（有更改则提交，然后pull）
        /// </summary>
        /// <param name="project">项目</param>
        /// <param name="commitMessage">提交信息（可选）</param>
        /// <param name="progress">进度回调</param>
        /// <returns>同步结果</returns>
        [Obsolete("请使用 GitSyncService.SyncProjectAsync 代替")]
        public async Task<GitSyncResult> SyncProjectAsync(GitProject project, string? commitMessage = null, IProgress<string>? progress = null)
        {
            var result = new GitSyncResult { ProjectName = project.Name };

            try
            {
                // 1. 检查git status
                var statusResult = await RunGitCommandAsync(project.Path, "status --porcelain");

                if (!string.IsNullOrWhiteSpace(statusResult))
                {
                    // 有未提交的更改，需要先提交
                    progress?.Report($"[{project.Name}] 发现 {statusResult.Split('\n').Length} 个文件有更改");

                    if (!string.IsNullOrWhiteSpace(commitMessage))
                    {
                        // 2. git add .
                        progress?.Report($"[{project.Name}] 暂存更改...");
                        await RunGitCommandAsync(project.Path, "add .");

                        // 3. git commit
                        progress?.Report($"[{project.Name}] 提交更改: {commitMessage}");
                        var escapedMessage = commitMessage.Replace("\"", "\\\"");
                        await RunGitCommandAsync(project.Path, $"commit -m \"{escapedMessage}\"");

                        result.HasCommit = true;
                        result.CommitMessage = commitMessage;
                    }
                    else
                    {
                        // 没有提交信息，跳过提交
                        progress?.Report($"[{project.Name}] 有未提交的更改但未提供提交信息，跳过提交");
                    }
                }

                // 4. git pull
                progress?.Report($"[{project.Name}] 正在拉取更新...");
                var pullResult = await RunGitCommandAsync(project.Path, "pull --no-commit");

                if (pullResult.Contains("Already up to date") || pullResult.Contains("已经是最新的"))
                {
                    result.Success = true;
                    result.Message = "已是最新";
                }
                else if (!string.IsNullOrWhiteSpace(pullResult))
                {
                    result.Success = true;
                    result.Message = "更新成功";
                    progress?.Report($"[{project.Name}] 更新成功");
                }
                else
                {
                    result.Success = true;
                    result.Message = "同步完成";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"同步失败: {ex.Message}";
                progress?.Report($"[{project.Name}] 同步失败: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 获取项目的解决方案文件路径
        /// </summary>
        public string? GetSolutionPath(string projectPath)
        {
            var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly);
            return slnFiles.FirstOrDefault();
        }

        /// <summary>
        /// 获取项目的项目文件路径
        /// </summary>
        public string? GetProjectFilePath(string projectPath)
        {
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
            return csprojFiles.FirstOrDefault() ??
                   Directory.GetFiles(projectPath, "*.vbproj", SearchOption.AllDirectories).FirstOrDefault() ??
                   Directory.GetFiles(projectPath, "*.fsproj", SearchOption.AllDirectories).FirstOrDefault();
        }

        /// <summary>
        /// 解析项目文件，获取目标框架信息
        /// </summary>
        public ProjectFileInfo? ParseProjectFile(string projectPath)
        {
            var info = new ProjectFileInfo();

            // 解析 .csproj / .vbproj / .fsproj 文件
            var csproj = GetProjectFilePath(projectPath);
            if (csproj != null && File.Exists(csproj))
            {
                info.FilePath = csproj;
                try
                {
                    var doc = XDocument.Load(csproj);
                    var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                    // TargetFramework（.NET 5+/Core，如 net8.0, net9.0-windows）
                    info.TargetFramework = doc.Descendants(ns + "TargetFramework")
                        .FirstOrDefault()?.Value
                        ?? doc.Descendants("TargetFramework").FirstOrDefault()?.Value;

                    // TargetFrameworkVersion（.NET Framework，如 v4.7.2）
                    info.TargetFrameworkVersion = doc.Descendants(ns + "TargetFrameworkVersion")
                        .FirstOrDefault()?.Value
                        ?? doc.Descendants("TargetFrameworkVersion").FirstOrDefault()?.Value;

                    // 支持多目标框架，取第一个
                    var tfw = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value
                        ?? doc.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
                    if (!string.IsNullOrEmpty(tfw) && string.IsNullOrEmpty(info.TargetFramework))
                    {
                        info.TargetFramework = tfw.Split(';').FirstOrDefault()?.Trim();
                    }
                }
                catch { }
            }

            // 解析 .sln 文件中的解决方案格式版本
            var slnPath = GetSolutionPath(projectPath);
            if (slnPath != null && File.Exists(slnPath))
            {
                try
                {
                    var lines = File.ReadAllLines(slnPath);
                    foreach (var line in lines)
                    {
                        // 如 "# Visual Studio Version 17"
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"#\s*Visual\s+Studio\s+Version\s+(\d+)");
                        if (match.Success)
                        {
                            var majorVersion = match.Groups[1].Value;
                            info.SolutionFormatVersion = $"v{majorVersion}.0";
                            break;
                        }
                    }
                }
                catch { }
            }

            // 如果没有任何解析结果，返回 null
            if (string.IsNullOrEmpty(info.TargetFramework) &&
                string.IsNullOrEmpty(info.TargetFrameworkVersion) &&
                string.IsNullOrEmpty(info.SolutionFormatVersion))
                return null;

            return info;
        }

        /// <summary>
        /// 运行Git命令
        /// </summary>
        private async Task<string> RunGitCommandAsync(string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (error.Length > 0)
            {
                var errorMsg = error.ToString();
                // 忽略某些常见的不影响结果的警告
                if (!errorMsg.Contains("warning:") && !errorMsg.Contains("Warning:"))
                {
                    // 可以选择记录或抛出
                }
            }

            return output.ToString().Trim();
        }
    }
}
