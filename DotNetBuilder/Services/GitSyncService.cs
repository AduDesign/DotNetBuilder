using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DotNetBuilder.Models;

namespace DotNetBuilder.Services
{
    /// <summary>
    /// Git同步服务 - 负责提交、pull、冲突处理
    /// </summary>
    public class GitSyncService
    {
        private readonly string _defaultCommitMessage;

        public GitSyncService()
        {
            _defaultCommitMessage = $"Sync changes {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }

        /// <summary>
        /// 获取远程仓库状态
        /// </summary>
        public async Task<RemoteStatusInfo> GetRemoteStatusAsync(GitProject project)
        {
            var info = new RemoteStatusInfo();

            try
            {
                // 先 fetch 获取最新远程信息
                var (fetchOutput, fetchExitCode) = await RunGitCommandAsync(project.Path, "fetch");

                if (fetchExitCode != 0)
                {
                    // Fetch 失败，可能是网络问题
                    info.HasRemoteChanges = true;
                    return info;
                }

                // 获取本地分支与远程分支的差异
                var (statusOutput, _) = await RunGitCommandAsync(project.Path, "status -sb");

                // 解析状态: 如 "## develop...origin/develop [ahead 1, behind 2]"
                var aheadMatch = Regex.Match(statusOutput, @"\[ahead (\d+)");
                var behindMatch = Regex.Match(statusOutput, @"behind (\d+)");

                info.LocalAheadCount = aheadMatch.Success ? int.Parse(aheadMatch.Groups[1].Value) : 0;
                info.RemoteAheadCount = behindMatch.Success ? int.Parse(behindMatch.Groups[1].Value) : 0;
                info.HasRemoteChanges = info.RemoteAheadCount > 0;
                info.HasLocalUnpushedChanges = info.LocalAheadCount > 0;
            }
            catch
            {
                // 如果获取远程状态失败，假设可以直接 pull
                info.HasRemoteChanges = true;
            }

            return info;
        }

        /// <summary>
        /// 推送项目到远程
        /// </summary>
        public async Task<bool> PushProjectAsync(GitProject project, IProgress<string>? progress = null)
        {
            try
            {
                var (pushResult, exitCode) = await RunGitCommandAsync(project.Path, "push");

                // 检查退出码和输出内容
                if (exitCode != 0 ||
                    pushResult.Contains("error") ||
                    pushResult.Contains("failed") ||
                    pushResult.Contains("rejected") ||
                    pushResult.Contains("fatal") ||
                    pushResult.Contains("Connection") ||
                    pushResult.Contains("connection") ||
                    pushResult.Contains("timed out") ||
                    pushResult.Contains("Network") ||
                    pushResult.Contains("network") ||
                    pushResult.Contains("Could not resolve") ||
                    pushResult.Contains("Permission denied") ||
                    pushResult.Contains("Authentication"))
                {
                    progress?.Report($"[{project.Name}] Push失败: {pushResult}");
                    return false;
                }
                progress?.Report($"[{project.Name}] Push成功");
                return true;
            }
            catch (Exception ex)
            {
                progress?.Report($"[{project.Name}] Push异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 提交项目（仅提交，不执行pull）
        /// </summary>
        public async Task<GitSyncResult> CommitProjectAsync(
            GitProject project,
            string? commitMessage,
            bool autoCommitWhenNoMessage,
            IProgress<string>? progress = null)
        {
            var result = new GitSyncResult { ProjectName = project.Name };

            try
            {
                // 1. 检查本地是否有未提交的更改
                var (statusResult, _) = await RunGitCommandAsync(project.Path, "status --porcelain");
                var hasLocalChanges = !string.IsNullOrWhiteSpace(statusResult);

                if (!hasLocalChanges)
                {
                    result.Success = true;
                    result.Message = "无需提交（没有更改）";
                    progress?.Report($"[{project.Name}] 没有需要提交的更改");
                    return result;
                }

                var changesCount = statusResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                progress?.Report($"[{project.Name}] 发现 {changesCount} 个文件有更改");

                // 2. 决定是否提交
                if (string.IsNullOrWhiteSpace(commitMessage))
                {
                    if (autoCommitWhenNoMessage)
                    {
                        commitMessage = $"Sync changes {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                        progress?.Report($"[{project.Name}] 未填写提交信息，使用默认: {commitMessage}");
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = "有未提交的更改，请填写提交信息";
                        result.NeedsCommitMessage = true;
                        return result;
                    }
                }

                // 3. git add .
                progress?.Report($"[{project.Name}] 暂存更改...");
                await RunGitCommandAsync(project.Path, "add .");

                // 4. git commit
                progress?.Report($"[{project.Name}] 提交更改: {commitMessage}");
                var escapedMessage = commitMessage.Replace("\"", "\\\"");
                var (commitResult, commitExitCode) = await RunGitCommandAsync(project.Path, $"commit -m \"{escapedMessage}\"");

                // 检查是否真的有新提交（避免空提交）
                if (commitExitCode == 0 && !string.IsNullOrWhiteSpace(commitResult) && !commitResult.Contains("nothing to commit"))
                {
                    result.HasCommit = true;
                    result.CommitMessage = commitMessage;
                    result.Success = true;
                    result.Message = "提交成功";
                    progress?.Report($"[{project.Name}] 提交成功: {commitMessage}");
                }
                else if (commitExitCode != 0)
                {
                    result.Success = false;
                    result.Message = $"提交失败: {commitResult}";
                    progress?.Report($"[{project.Name}] 提交失败: {commitResult}");
                    return result;
                }
                else
                {
                    result.Success = true;
                    result.Message = "无需提交";
                    progress?.Report($"[{project.Name}] 没有新的提交");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"提交失败: {ex.Message}";
                progress?.Report($"[{project.Name}] 提交异常: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 获取变更文件列表
        /// </summary>
        public async Task<List<string>> GetChangedFilesAsync(GitProject project)
        {
            var files = new List<string>();
            try
            {
                var (statusResult, exitCode) = await RunGitCommandAsync(project.Path, "status --porcelain");
                if (exitCode != 0 || string.IsNullOrWhiteSpace(statusResult))
                    return files;

                var lines = statusResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // 格式: "XY filename" 如 "M  file.cs", "A  new.cs", "?? untracked.cs"
                    var parts = line.Split(' ', 2);
                    if (parts.Length >= 2)
                    {
                        files.Add(parts[1].Trim());
                    }
                }
            }
            catch { }

            return files;
        }

        /// <summary>
        /// 同步项目
        /// </summary>
        public async Task<GitSyncResult> SyncProjectAsync(
            GitProject project,
            string? commitMessage,
            SyncOptions options,
            IProgress<string>? progress = null)
        {
            var result = new GitSyncResult { ProjectName = project.Name };
            result.RemoteStatus = new RemoteStatusInfo();

            try
            {
                // 1. 检查本地是否有未提交的更改
                var (statusResult, _) = await RunGitCommandAsync(project.Path, "status --porcelain");
                var hasLocalChanges = !string.IsNullOrWhiteSpace(statusResult);

                // SkipCommit 策略跳过提交逻辑
                if (options.PullStrategy == PullStrategy.SkipCommit)
                {
                    if (hasLocalChanges)
                    {
                        var changesCount = statusResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                        progress?.Report($"[{project.Name}] 发现 {changesCount} 个文件有更改（跳过提交）");
                    }
                }
                else if (hasLocalChanges)
                {
                    var changesCount = statusResult.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                    progress?.Report($"[{project.Name}] 发现 {changesCount} 个文件有更改");

                    // 决定是否提交
                    if (string.IsNullOrWhiteSpace(commitMessage))
                    {
                        if (options.AutoCommitWhenNoMessage)
                        {
                            // 使用默认提交信息
                            commitMessage = _defaultCommitMessage;
                            progress?.Report($"[{project.Name}] 未填写提交信息，使用默认: {commitMessage}");
                        }
                        else
                        {
                            // 需要用户输入提交信息
                            result.Success = false;
                            result.Message = "有未提交的更改，请填写提交信息";
                            result.NeedsCommitMessage = true;
                            return result;
                        }
                    }

                    // 2. git add .
                    progress?.Report($"[{project.Name}] 暂存更改...");
                    await RunGitCommandAsync(project.Path, "add .");

                    // 3. git commit
                    progress?.Report($"[{project.Name}] 提交更改: {commitMessage}");
                    var escapedMessage = commitMessage.Replace("\"", "\\\"");
                    var (commitResult, commitExitCode) = await RunGitCommandAsync(project.Path, $"commit -m \"{escapedMessage}\"");

                    // 检查是否真的有新提交（避免空提交）
                    if (commitExitCode == 0 && !string.IsNullOrWhiteSpace(commitResult) && !commitResult.Contains("nothing to commit"))
                    {
                        result.HasCommit = true;
                        result.CommitMessage = commitMessage;
                    }
                }

                // 4. 检查远程状态
                progress?.Report($"[{project.Name}] 检查远程状态...");
                result.RemoteStatus = await GetRemoteStatusAsync(project);

                // 5. 根据策略执行 pull
                if (options.PullStrategy == PullStrategy.CommitOnly)
                {
                    // 仅提交模式，不 pull
                    result.Success = true;
                    result.Message = result.HasCommit ? "提交成功（已跳过pull）" : "无需提交（已跳过pull）";
                    return result;
                }

                // 6. 执行 pull
                var pullResult = await ExecutePullAsync(project, options, progress, result);

                if (!pullResult)
                {
                    return result;
                }

                // 7. 执行 push（如果启用）
                if (options.PushOnSync)
                {
                    progress?.Report($"[{project.Name}] 推送到远程...");
                    var pushPushResult = await PushProjectAsync(project, progress);
                    if (!pushPushResult)
                    {
                        result.Success = false;
                        result.Message = "Pull成功，但Push失败";
                        return result;
                    }
                }

                result.Success = true;
                result.Message = options.PushOnSync ? "同步完成（已推送）" : "同步完成";
            }
            catch (GitConflictException ex)
            {
                result.Success = false;
                result.HasConflict = true;
                result.ConflictMessage = ex.Message;
                result.ConflictFiles = ex.ConflictFiles;
                result.Message = $"冲突: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"同步失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 处理冲突
        /// </summary>
        public async Task<GitSyncResult> HandleConflictAsync(
            GitProject project,
            ConflictAction action,
            IProgress<string>? progress = null)
        {
            var result = new GitSyncResult { ProjectName = project.Name };

            try
            {
                switch (action)
                {
                    case ConflictAction.Abort:
                        // 放弃 pull，恢复状态
                        progress?.Report($"[{project.Name}] 放弃 pull，恢复本地状态...");
                        await RunGitCommandAsync(project.Path, "merge --abort");
                        result.Success = true;
                        result.Message = "已放弃 pull，保留本地状态";
                        break;

                    case ConflictAction.AutoStash:
                        // 自动 stash
                        progress?.Report($"[{project.Name}] 尝试自动 stash 后 pull...");
                        var (stashResult, _) = await RunGitCommandAsync(project.Path, "stash push -m \"DotNetBuilder auto stash\"");
                        progress?.Report($"[{project.Name}] stash 结果: {stashResult}");

                        // 执行 pull
                        var pullSuccess = await ExecutePullAsync(project, new SyncOptions(), progress, result);

                        if (pullSuccess)
                        {
                            // 尝试 pop stash
                            var (popResult, _) = await RunGitCommandAsync(project.Path, "stash pop");
                            if (popResult.Contains("CONFLICT") || popResult.Contains("conflict"))
                            {
                                throw new GitConflictException("Stash pop 时产生冲突", ExtractConflictFiles(popResult));
                            }
                            result.Message = "Stash pop 成功，可能存在冲突请检查";
                        }
                        else
                        {
                            // Pull 失败，恢复 stash
                            await RunGitCommandAsync(project.Path, "stash pop");
                            result.Message = "Pull 失败，已恢复本地更改";
                        }
                        break;

                    case ConflictAction.Prompt:
                    default:
                        // 提示用户手动解决
                        result.Success = false;
                        result.HasConflict = true;
                        result.Message = "请手动解决冲突后重试";
                        break;
                }
            }
            catch (GitConflictException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"处理冲突失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 执行 pull 操作
        /// </summary>
        private async Task<bool> ExecutePullAsync(
            GitProject project,
            SyncOptions options,
            IProgress<string>? progress,
            GitSyncResult result)
        {
            progress?.Report($"[{project.Name}] 正在拉取更新...");

            string pullArgs = options.PullStrategy switch
            {
                PullStrategy.Merge => "pull --no-commit --no-ff",
                PullStrategy.Rebase => "pull --rebase --no-commit",
                _ => "pull --no-commit"
            };

            var (pullOutput, pullExitCode) = await RunGitCommandAsync(project.Path, pullArgs);

            // 检查退出码
            if (pullExitCode != 0)
            {
                // 检查是否是网络错误
                if (pullOutput.Contains("Connection") ||
                    pullOutput.Contains("connection") ||
                    pullOutput.Contains("timed out") ||
                    pullOutput.Contains("Network") ||
                    pullOutput.Contains("Could not resolve") ||
                    pullOutput.Contains("Permission denied") ||
                    pullOutput.Contains("fatal"))
                {
                    progress?.Report($"[{project.Name}] 拉取失败（网络错误）: {pullOutput}");
                    result.Success = false;
                    result.Message = $"拉取失败: {pullOutput}";
                    return false;
                }

                // 其他错误
                progress?.Report($"[{project.Name}] 拉取失败: {pullOutput}");
                result.Success = false;
                result.Message = $"拉取失败: {pullOutput}";
                return false;
            }

            // 检查是否有冲突
            if (pullOutput.Contains("CONFLICT") ||
                pullOutput.Contains("conflict") ||
                pullOutput.Contains("合并冲突"))
            {
                var conflictFiles = ExtractConflictFiles(pullOutput);
                throw new GitConflictException(
                    $"存在 {conflictFiles.Count} 个冲突文件",
                    conflictFiles);
            }

            // 检查 pull 结果
            if (pullOutput.Contains("Already up to date") ||
                pullOutput.Contains("已经是最新的") ||
                pullOutput.Contains("up to date"))
            {
                result.Message = "已是最新";
            }
            else if (!string.IsNullOrWhiteSpace(pullOutput))
            {
                result.Message = "更新成功";
                progress?.Report($"[{project.Name}] 更新成功");
            }

            return true;
        }

        /// <summary>
        /// 从 git 输出中提取冲突文件列表
        /// </summary>
        private List<string> ExtractConflictFiles(string output)
        {
            var files = new List<string>();
            var lines = output.Split('\n');

            foreach (var line in lines)
            {
                // 冲突文件通常格式: "both modified:   file.cs"
                // 或: "CONFLICT (content): file.cs"
                if (line.Contains("CONFLICT") ||
                    line.Contains("both modified") ||
                    line.Contains("both added") ||
                    line.Contains("deleted by us") ||
                    line.Contains("deleted by them"))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        var file = parts[^1].Trim();
                        if (!string.IsNullOrEmpty(file) && !files.Contains(file))
                        {
                            files.Add(file);
                        }
                    }
                }
            }

            return files;
        }

        /// <summary>
        /// 运行 Git 命令
        /// </summary>
        private async Task<(string Output, int ExitCode)> RunGitCommandAsync(string workingDirectory, string arguments)
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

            var result = output.ToString().Trim();
            var errorOutput = error.ToString().Trim();

            // 如果 stderr 有输出但 stdout 为空，可能有错误
            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(errorOutput))
            {
                result = errorOutput;
            }

            // 合并错误输出到结果中，便于检查
            if (!string.IsNullOrEmpty(errorOutput))
            {
                result = string.IsNullOrEmpty(result)
                    ? errorOutput
                    : result + Environment.NewLine + errorOutput;
            }

            return (result, process.ExitCode);
        }
    }

    /// <summary>
    /// 同步选项
    /// </summary>
    public class SyncOptions
    {
        /// <summary>
        /// Pull 策略
        /// </summary>
        public PullStrategy PullStrategy { get; set; } = PullStrategy.Auto;

        /// <summary>
        /// 无提交信息时是否自动提交
        /// </summary>
        public bool AutoCommitWhenNoMessage { get; set; } = false;

        /// <summary>
        /// 冲突时的处理方式
        /// </summary>
        public ConflictAction ConflictAction { get; set; } = ConflictAction.Prompt;

        /// <summary>
        /// 同步后是否推送到远程
        /// </summary>
        public bool PushOnSync { get; set; } = false;
    }

    /// <summary>
    /// Git 冲突异常
    /// </summary>
    public class GitConflictException : Exception
    {
        public List<string> ConflictFiles { get; }

        public GitConflictException(string message, List<string> conflictFiles)
            : base(message)
        {
            ConflictFiles = conflictFiles;
        }
    }
}
