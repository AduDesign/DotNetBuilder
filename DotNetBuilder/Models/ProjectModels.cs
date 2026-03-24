using System.Text.Json.Serialization;

namespace DotNetBuilder.Models
{
    /// <summary>
    /// 项目信息
    /// </summary>
    public class ProjectInfo
    {
        /// <summary>
        /// 项目名称
        /// </summary>
        public string Name { get; set; } = "未命名项目";

        /// <summary>
        /// 项目文件路径
        /// </summary>
        [JsonIgnore]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 根目录路径
        /// </summary>
        public string RootPath { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 项目版本
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Git 项目列表
        /// </summary>
        public List<ProjectConfig> Projects { get; set; } = new();

        /// <summary>
        /// 全局拉取策略
        /// </summary>
        public PullStrategy GlobalPullStrategy { get; set; } = PullStrategy.Auto;

        /// <summary>
        /// 全局冲突处理方式
        /// </summary>
        public ConflictAction GlobalConflictAction { get; set; } = ConflictAction.Prompt;

        /// <summary>
        /// 无提交信息时自动提交
        /// </summary>
        public bool GlobalAutoCommitWhenNoMessage { get; set; }

        /// <summary>
        /// 同步后推送
        /// </summary>
        public bool GlobalPushOnSync { get; set; }
    }

    /// <summary>
    /// 项目配置
    /// </summary>
    public class ProjectConfig
    {
        /// <summary>
        /// Git 项目路径
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// MSBuild 版本显示名称
        /// </summary>
        public string? SelectedMSBuildVersion { get; set; }

        /// <summary>
        /// 执行文件路径
        /// </summary>
        public string? ExecuteFile { get; set; }

        /// <summary>
        /// 配置类型 (Release/Debug)
        /// </summary>
        public string Configuration { get; set; } = "Release";

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Pull 策略
        /// </summary>
        public PullStrategy PullStrategy { get; set; } = PullStrategy.Auto;

        /// <summary>
        /// 冲突处理方式
        /// </summary>
        public ConflictAction ConflictAction { get; set; } = ConflictAction.Prompt;

        /// <summary>
        /// 无提交信息时自动提交
        /// </summary>
        public bool AutoCommitWhenNoMessage { get; set; }
    }

    /// <summary>
    /// 最近项目记录
    /// </summary>
    public class RecentProject
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastOpenedAt { get; set; }
    }

    /// <summary>
    /// App 配置
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 最近打开的项目
        /// </summary>
        public List<RecentProject> RecentProjects { get; set; } = new();

        /// <summary>
        /// 上次打开的项目路径
        /// </summary>
        public string? LastProjectPath { get; set; }

        /// <summary>
        /// 窗口大小
        /// </summary>
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 900;
    }
}
