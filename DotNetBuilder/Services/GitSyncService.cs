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
                await RunGitCommandAsync(project.Path, "fetch");

                // 获取本地分支与远程分支的差异
                var status = await RunGitCommandAsync(project.Path, "status -sb");

                // 解析状态: 如 "## develop...origin/develop [ahead 1, behind 2]"
                var aheadMatch = Regex.Match(status, @"\[ahead (\d+)");
                var behindMatch = Regex.Match(status, @"behind (\d+)");

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
                var pushResult = await RunGitCommandAsync(project.Path, "push");
                if (pushResult.Contains("error") || pushResult.Contains("failed") || pushResult.Contains("rejected"))
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
                var statusResult = await RunGitCommandAsync(project.Path, "status --porcelain");
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
                    var commitResult = await RunGitCommandAsync(project.Path, $"commit -m \"{escapedMessage}\"");

                    // 检查是否真的有新提交（避免空提交）
                    if (!string.IsNullOrWhiteSpace(commitResult) && !commitResult.Contains("nothing to commit"))
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
                        var stashResult = await RunGitCommandAsync(project.Path, "stash push -m \"DotNetBuilder auto stash\"");
                        progress?.Report($"[{project.Name}] stash 结果: {stashResult}");

                        // 执行 pull
                        var pullSuccess = await ExecutePullAsync(project, new SyncOptions(), progress, result);

                        if (pullSuccess)
                        {
                            // 尝试 pop stash
                            var popResult = await RunGitCommandAsync(project.Path, "stash pop");
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

            var pullResult = await RunGitCommandAsync(project.Path, pullArgs);

            // 检查是否有冲突
            if (pullResult.Contains("CONFLICT") ||
                pullResult.Contains("conflict") ||
                pullResult.Contains("合并冲突"))
            {
                var conflictFiles = ExtractConflictFiles(pullResult);
                throw new GitConflictException(
                    $"存在 {conflictFiles.Count} 个冲突文件",
                    conflictFiles);
            }

            // 检查 pull 结果
            if (pullResult.Contains("Already up to date") ||
                pullResult.Contains("已经是最新的") ||
                pullResult.Contains("up to date"))
            {
                result.Message = "已是最新";
            }
            else if (!string.IsNullOrWhiteSpace(pullResult))
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

            var result = output.ToString().Trim();

            // 如果 stderr 有输出但 stdout 为空，可能有错误
            if (string.IsNullOrEmpty(result) && error.Length > 0)
            {
                result = error.ToString().Trim();
            }

            return result;
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
