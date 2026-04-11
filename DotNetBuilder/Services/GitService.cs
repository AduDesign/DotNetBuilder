using System.Collections.ObjectModel;
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

                // 解析改动文件列表
                var changedFiles = new ObservableCollection<ChangedFile>();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        // 格式: "XY filename" 如 "M  file.cs", "A  new.cs", "?? untracked.cs"
                        // X = index status, Y = work tree status
                        if (line.Length < 3) continue;

                        var xyStatus = line.Substring(0, 2);
                        var filePath = line.Substring(3).Trim();

                        // 处理重命名情况 "R  old -> new"
                        string? oldPath = null;
                        if (xyStatus[0] == 'R' && filePath.Contains(" -> "))
                        {
                            var parts = filePath.Split(new[] { " -> " }, 2, StringSplitOptions.None);
                            if (parts.Length == 2)
                            {
                                oldPath = parts[0];
                                filePath = parts[1];
                            }
                        }

                        var status = ParseFileStatus(xyStatus);

                        changedFiles.Add(new ChangedFile
                        {
                            FilePath = filePath,
                            Status = status,
                            RawStatus = xyStatus,
                            IsSelected = true
                        });

                        // 如果是重命名，也添加旧文件记录
                        if (oldPath != null)
                        {
                            changedFiles.Add(new ChangedFile
                            {
                                FilePath = oldPath + " → " + filePath,
                                Status = FileChangeStatus.Renamed,
                                RawStatus = xyStatus,
                                IsSelected = true
                            });
                        }
                    }
                }

                // 注意：由于这是跨线程更新，需要在UI线程上执行
                project.HasChanges = hasChanges;
                project.IsExpanded = hasChanges;
                project.ChangesCount = changesCount;
                project.ChangedFiles = changedFiles;
            }
            catch
            {
                project.IsExpanded = project.HasChanges = false;
                project.ChangesCount = 0;
                project.ChangedFiles = new ObservableCollection<ChangedFile>();
            }
        }

        /// <summary>
        /// 解析 git status 的 XY 状态码
        /// 注意：X = 索引状态（staging），Y = 工作区状态（work tree）
        /// </summary>
        private FileChangeStatus ParseFileStatus(string xyStatus)
        {
            if (xyStatus.Length < 2) return FileChangeStatus.Modified;

            var x = xyStatus[0]; // index status
            var y = xyStatus[1]; // work tree status

            // 优先检查索引状态（X）- 这决定了文件的 git 追踪状态
            // X='A' 表示已暂存的新文件（从未被提交过）
            if (x == 'A') return FileChangeStatus.Added;

            // X='D' 表示索引中已删除（文件从版本控制中移除）
            if (x == 'D') return FileChangeStatus.Deleted;

            // X='R' 表示重命名
            if (x == 'R') return FileChangeStatus.Renamed;

            // X='C' 表示复制
            if (x == 'C') return FileChangeStatus.Conflicted;

            // X='M' 表示索引中有修改
            if (x == 'M') return FileChangeStatus.Modified;

            // X='?' 且 Y='?' 表示未跟踪文件
            if (x == '?' && y == '?') return FileChangeStatus.Untracked;

            // 工作区状态（Y）- 文件在 git 的版本控制中，但工作区有变化
            // Y='?' 未跟踪（文件存在但未被 git 管理）
            if (y == '?') return FileChangeStatus.Untracked;

            // Y='D' 工作区中已删除（但索引中还有）
            if (y == 'D') return FileChangeStatus.Deleted;

            // Y='M' 工作区中有修改
            if (y == 'M') return FileChangeStatus.Modified;

            // Y='A' 罕见情况：新增文件
            if (y == 'A') return FileChangeStatus.Added;

            // Y='C' 冲突
            if (y == 'C') return FileChangeStatus.Conflicted;

            // 默认返回修改
            return FileChangeStatus.Modified;
        }

        /// <summary>
        /// 撤销单个文件的更改
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="filePath">文件路径（相对路径）</param>
        /// <param name="status">文件状态</param>
        /// <param name="progress">进度回调（用于输出错误信息）</param>
        public async Task<bool> RevertFileAsync(string projectPath, string filePath, FileChangeStatus status, IProgress<string>? progress = null)
        {
            try
            {
                string command;
                string fullPath = Path.Combine(projectPath, filePath);

                if (status == FileChangeStatus.Untracked)
                {
                    // 未跟踪文件：直接删除
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        progress?.Report($"已删除文件: {filePath}");
                    }
                    else if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                        progress?.Report($"已删除目录: {filePath}");
                    }
                    return true;
                }
                else if (status == FileChangeStatus.Added)
                {
                    // 新增文件（已暂存但从未提交）：从暂存区移除
                    command = $"reset HEAD -- \"{filePath}\"";
                    await RunGitCommandAsync(projectPath, command);

                    // 删除实际文件（如果存在）
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        progress?.Report($"已取消暂存并删除: {filePath}");
                    }
                    return true;
                }
                else if (status == FileChangeStatus.Deleted)
                {
                    // 已删除的文件：恢复到最近提交的版本
                    command = $"checkout HEAD -- \"{filePath}\"";
                    await RunGitCommandAsync(projectPath, command);
                    progress?.Report($"已恢复文件: {filePath}");
                    return true;
                }
                else if (status == FileChangeStatus.Modified || status == FileChangeStatus.Renamed)
                {
                    // 修改或重命名的文件：先尝试 git checkout HEAD
                    // 如果失败（文件从未提交过），则取消暂存
                    try
                    {
                        command = $"checkout HEAD -- \"{filePath}\"";
                        await RunGitCommandAsync(projectPath, command);
                        progress?.Report($"已恢复文件: {filePath}");
                    }
                    catch (GitCommandException ex) when (ex.Message.Contains("did not match any file"))
                    {
                        // 文件在 HEAD 中不存在，可能是已暂存的新文件
                        progress?.Report($"文件在 HEAD 中不存在，取消暂存: {filePath}");
                        command = $"reset HEAD -- \"{filePath}\"";
                        await RunGitCommandAsync(projectPath, command);

                        // 删除工作区中的修改
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            progress?.Report($"已取消暂存，删除工作区文件: {filePath}");
                        }
                    }
                    return true;
                }
                else if (status == FileChangeStatus.Conflicted)
                {
                    // 冲突文件：使用 git checkout --ours
                    command = $"checkout --ours \"{filePath}\"";
                    await RunGitCommandAsync(projectPath, command);
                    progress?.Report($"已解决冲突（使用本地版本）: {filePath}");
                    return true;
                }

                return true;
            }
            catch (GitCommandException ex)
            {
                progress?.Report($"撤销失败 [{filePath}]: {ex.Message}");
                progress?.Report($"  命令: git {ex.Command}");
                progress?.Report($"  退出码: {ex.ExitCode}");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"撤销失败 [{filePath}]: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除单个文件（从磁盘删除）
        /// </summary>
        public async Task<bool> DeleteFileAsync(string projectPath, string filePath)
        {
            try
            {
                var fullPath = System.IO.Path.Combine(projectPath, filePath);
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                    return true;
                }
                else if (System.IO.Directory.Exists(fullPath))
                {
                    System.IO.Directory.Delete(fullPath, true);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 撤销多个文件的更改
        /// </summary>
        public async Task<int> RevertFilesAsync(string projectPath, IEnumerable<ChangedFile> files, IProgress<string>? progress = null)
        {
            int successCount = 0;
            foreach (var file in files.Where(f => f.IsSelected))
            {
                if (await RevertFileAsync(projectPath, file.FilePath, file.Status, progress))
                {
                    successCount++;
                }
            }
            return successCount;
        }

        /// <summary>
        /// 删除多个文件
        /// </summary>
        public async Task<int> DeleteFilesAsync(string projectPath, IEnumerable<ChangedFile> files)
        {
            int successCount = 0;
            foreach (var file in files.Where(f => f.IsSelected))
            {
                if (await DeleteFileAsync(projectPath, file.FilePath))
                {
                    successCount++;
                }
            }
            return successCount;
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
        /// 从 URL 克隆 Git 仓库
        /// </summary>
        /// <param name="repositoryUrl">仓库 URL</param>
        /// <param name="targetDirectory">目标目录（父目录）</param>
        /// <param name="progress">进度回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>克隆成功返回 true，失败返回 false</returns>
        public async Task<bool> CloneRepositoryAsync(string repositoryUrl, string targetDirectory, IProgress<string>? progress = null, CancellationToken? cancellationToken = null)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                progress?.Report("仓库 URL 不能为空");
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                progress?.Report("目标目录不能为空");
                return false;
            }

            if (!Directory.Exists(targetDirectory))
            {
                progress?.Report($"目标目录不存在: {targetDirectory}");
                return false;
            }

            try
            {
                // 检查取消
                if (cancellationToken?.IsCancellationRequested == true)
                {
                    progress?.Report("已取消克隆");
                    return false;
                }

                // 提取仓库名称作为克隆后的文件夹名
                var repoName = ExtractRepoName(repositoryUrl);
                var clonePath = Path.Combine(targetDirectory, repoName);

                // 检查目录是否已存在
                if (Directory.Exists(clonePath))
                {
                    progress?.Report($"目录已存在: {clonePath}");
                    return false;
                }

                progress?.Report($"正在克隆: {repositoryUrl}");
                progress?.Report($"目标目录: {clonePath}");

                // 确保目标目录有写入权限
                try
                {
                    var testFile = Path.Combine(targetDirectory, ".git_clone_test_" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    progress?.Report($"目录权限检查失败: {ex.Message}");
                    return false;
                }

                await RunGitCloneAsync(repositoryUrl, targetDirectory, repoName, progress, cancellationToken);

                if (cancellationToken?.IsCancellationRequested == true)
                {
                    progress?.Report("已取消克隆");
                    return false;
                }

                if (Directory.Exists(clonePath))
                {
                    progress?.Report($"克隆成功: {clonePath}");
                    return true;
                }

                progress?.Report($"克隆失败，目录未创建");
                return false;
            }
            catch (OperationCanceledException)
            {
                progress?.Report("已取消克隆");
                return false;
            }
            catch (Exception ex)
            {
                progress?.Report($"克隆异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从 URL 中提取仓库名称
        /// </summary>
        private string ExtractRepoName(string repositoryUrl)
        {
            // 移除 .git 后缀
            var url = repositoryUrl.TrimEnd('/');
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                url = url[..^4];
            }

            // 获取最后一段路径
            var lastSlash = url.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < url.Length - 1)
            {
                return url[(lastSlash + 1)..];
            }

            return "repository";
        }

        /// <summary>
        /// 运行 git clone 命令（支持进度输出和取消）
        /// </summary>
        private async Task RunGitCloneAsync(string repositoryUrl, string targetDirectory, string repoName, IProgress<string>? progress, CancellationToken? cancellationToken = null)
        {
            // 直接使用简单的 clone 命令，不带 --progress
            var arguments = $"clone \"{repositoryUrl}\" \"{repoName}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = targetDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var outputLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add(e.Data);
                    progress?.Report(e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add("[ERROR] " + e.Data);
                    progress?.Report(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 使用取消令牌等待
            if (cancellationToken.HasValue)
            {
                await process.WaitForExitAsync(cancellationToken.Value);
            }
            else
            {
                await process.WaitForExitAsync();
            }

            // 输出最后几行（可能包含错误信息）
            if (process.ExitCode != 0)
            {
                var lastLines = outputLines.TakeLast(5).ToList();
                var errorInfo = string.Join(Environment.NewLine, lastLines);
                progress?.Report($"克隆失败 (ExitCode: {process.ExitCode})");
                if (!string.IsNullOrEmpty(errorInfo))
                {
                    progress?.Report(errorInfo);
                }
            }
        }

        /// <summary>
        /// 运行Git命令
        /// </summary>
        /// <exception cref="GitCommandException">当 git 命令返回非零退出码时抛出</exception>
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

            // 检查退出码
            if (process.ExitCode != 0)
            {
                var errorMsg = error.ToString().Trim();
                var outputMsg = output.ToString().Trim();

                // 优先使用 stderr，如果没有则使用 stdout
                var message = !string.IsNullOrEmpty(errorMsg) ? errorMsg : outputMsg;

                if (string.IsNullOrEmpty(message))
                {
                    message = $"Git command failed with exit code {process.ExitCode}";
                }

                throw new GitCommandException(arguments, process.ExitCode, message);
            }

            // 即使退出码为 0，也检查 stderr 是否有错误信息（非 warning）
            var stderrContent = error.ToString().Trim();
            if (!string.IsNullOrEmpty(stderrContent) &&
                !stderrContent.Contains("warning:", StringComparison.OrdinalIgnoreCase) &&
                !stderrContent.Contains("Warning:", StringComparison.OrdinalIgnoreCase))
            {
                // 有些命令会输出到 stderr 但仍然成功，如 `git status`
                // 这里可以记录但不抛出异常
            }

            return output.ToString().Trim();
        }

        /// <summary>
        /// 运行 Git 命令（忽略错误，用于某些不关心失败的场景）
        /// </summary>
        private async Task<string> RunGitCommandIgnoreErrorAsync(string workingDirectory, string arguments)
        {
            try
            {
                return await RunGitCommandAsync(workingDirectory, arguments);
            }
            catch (GitCommandException)
            {
                // 忽略错误，返回空字符串
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Git 命令执行异常
    /// </summary>
    public class GitCommandException : Exception
    {
        public string Command { get; }
        public int ExitCode { get; }

        public GitCommandException(string command, int exitCode, string message)
            : base(message)
        {
            Command = command;
            ExitCode = exitCode;
        }
    }
}
