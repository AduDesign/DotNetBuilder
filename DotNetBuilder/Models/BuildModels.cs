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
    }
}
