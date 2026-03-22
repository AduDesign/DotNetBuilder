namespace DotNetBuilder.Models
{
    /// <summary>
    /// MSBuild版本信息
    /// </summary>
    public class MSBuildVersion
    {
        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// MSBuild.exe完整路径
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Visual Studio版本
        /// </summary>
        public string VisualStudioVersion { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 构建结果
    /// </summary>
    public class BuildResult
    {
        public bool Success { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Output { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Git同步结果
    /// </summary>
    public class GitSyncResult
    {
        public bool Success { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool HasCommit { get; set; }
        public string? CommitMessage { get; set; }

        /// <summary>
        /// 是否有冲突
        /// </summary>
        public bool HasConflict { get; set; }

        /// <summary>
        /// 冲突信息
        /// </summary>
        public string? ConflictMessage { get; set; }

        /// <summary>
        /// 冲突文件列表
        /// </summary>
        public List<string>? ConflictFiles { get; set; }

        /// <summary>
        /// 远程状态信息
        /// </summary>
        public RemoteStatusInfo? RemoteStatus { get; set; }

        /// <summary>
        /// 是否需要用户输入提交信息
        /// </summary>
        public bool NeedsCommitMessage { get; set; }
    }

    /// <summary>
    /// 远程仓库状态信息
    /// </summary>
    public class RemoteStatusInfo
    {
        /// <summary>
        /// 远程是否有新提交
        /// </summary>
        public bool HasRemoteChanges { get; set; }

        /// <summary>
        /// 本地是否有未推送的提交
        /// </summary>
        public bool HasLocalUnpushedChanges { get; set; }

        /// <summary>
        /// 远程新提交数量
        /// </summary>
        public int RemoteAheadCount { get; set; }

        /// <summary>
        /// 本地未推送提交数量
        /// </summary>
        public int LocalAheadCount { get; set; }

        /// <summary>
        /// 需要 merge 还是 rebase
        /// </summary>
        public bool NeedsMerge => HasRemoteChanges && HasLocalUnpushedChanges;

        /// <summary>
        /// 仅远程有更新（可以直接 pull）
        /// </summary>
        public bool CanFastForward => HasRemoteChanges && !HasLocalUnpushedChanges;

        /// <summary>
        /// 仅本地有提交（可以 push）
        /// </summary>
        public bool CanPushOnly => !HasRemoteChanges && HasLocalUnpushedChanges;
    }

    /// <summary>
    /// Pull 策略
    /// </summary>
    public enum PullStrategy
    {
        /// <summary>
        /// 自动选择（无冲突用 fast-forward，有冲突提示）
        /// </summary>
        Auto,

        /// <summary>
        /// Merge（创建 merge commit）
        /// </summary>
        Merge,

        /// <summary>
        /// Rebase（线性历史，可能产生冲突）
        /// </summary>
        Rebase,

        /// <summary>
        /// 仅提交本地，不 pull
        /// </summary>
        CommitOnly
    }

    /// <summary>
    /// 冲突时的处理方式
    /// </summary>
    public enum ConflictAction
    {
        /// <summary>
        /// 提示用户
        /// </summary>
        Prompt,

        /// <summary>
        /// 自动 stash 后 pull，再 pop（可能产生冲突）
        /// </summary>
        AutoStash,

        /// <summary>
        /// 放弃 pull，保留本地状态
        /// </summary>
        Abort
    }
}
