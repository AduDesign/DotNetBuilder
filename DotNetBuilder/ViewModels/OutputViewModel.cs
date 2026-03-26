using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetBuilder.Models;
using DotNetBuilder.Services;

namespace DotNetBuilder.ViewModels
{
    /// <summary>
    /// 日志类型枚举
    /// </summary>
    public enum LogType
    {
        Message,
        Error,
        Warning,
        Git,
        Build
    }

    /// <summary>
    /// 输出面板 ViewModel - 使用 CommunityToolkit.Mvvm
    /// </summary>
    public partial class OutputViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _logOutput = string.Empty;

        [ObservableProperty]
        private bool _showErrorLog = true;

        [ObservableProperty]
        private bool _showWarningLog = true;

        [ObservableProperty]
        private bool _showMessageLog = true;

        [ObservableProperty]
        private bool _showGitLog = true;

        [ObservableProperty]
        private bool _showBuildLog = false;

        [ObservableProperty]
        private PullStrategy _globalPullStrategy = PullStrategy.Auto;

        [ObservableProperty]
        private ConflictAction _globalConflictAction = ConflictAction.Prompt;

        [ObservableProperty]
        private bool _globalAutoCommitWhenNoMessage = false;

        // 日志缓冲，用于批量更新
        private readonly StringBuilder _logBuffer = new();
        private readonly DispatcherTimer _flushTimer;
        private bool _isDirty;
        private readonly object _lockObj = new();

        public ObservableCollection<PullStrategy> PullStrategies { get; }
        public ObservableCollection<ConflictAction> ConflictActions { get; }

        public OutputViewModel()
        {
            PullStrategies = new ObservableCollection<PullStrategy>
            {
                PullStrategy.Auto,
                PullStrategy.Merge,
                PullStrategy.Rebase,
                PullStrategy.CommitOnly
            };

            ConflictActions = new ObservableCollection<ConflictAction>
            {
                ConflictAction.Prompt,
                ConflictAction.AutoStash,
                ConflictAction.Abort
            };

            // 使用定时器批量刷新日志，50ms 刷新一次
            _flushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _flushTimer.Tick += (s, e) => FlushLog();
            _flushTimer.Start();
        }

        [RelayCommand]
        private void ClearLog()
        {
            lock (_lockObj)
            {
                _logBuffer.Clear();
                _isDirty = true;
            }
            LogOutput = string.Empty;
        }

        public SyncOptions GetSyncOptions() => new()
        {
            PullStrategy = GlobalPullStrategy,
            ConflictAction = GlobalConflictAction,
            AutoCommitWhenNoMessage = GlobalAutoCommitWhenNoMessage
        };

        /// <summary>
        /// 追加日志到缓冲区，使用批量更新避免频繁 UI 刷新
        /// </summary>
        public void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // 分隔符标题始终显示
            if (message.Contains("=========="))
            {
                lock (_lockObj)
                {
                    _logBuffer.Append(message);
                    _isDirty = true;
                }
                return;
            }

            var logType = ClassifyLogType(message);
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
                lock (_lockObj)
                {
                    _logBuffer.Append(message);
                    _isDirty = true;
                }
            }
        }

        /// <summary>
        /// 强制刷新缓冲区到 UI（立即显示所有待处理的日志）
        /// </summary>
        public void ForceFlush()
        {
            FlushLog();
        }

        /// <summary>
        /// 批量刷新日志到 UI
        /// </summary>
        private void FlushLog()
        {
            if (!_isDirty)
                return;

            string toAppend;
            lock (_lockObj)
            {
                if (_logBuffer.Length == 0)
                {
                    _isDirty = false;
                    return;
                }

                toAppend = _logBuffer.ToString();
                _logBuffer.Clear();
                _isDirty = false;
            }

            // 批量追加到现有输出
            LogOutput += toAppend;
        }

        private LogType ClassifyLogType(string message)
        {
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

            if (message.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("警告") ||
                message.Contains("WARN:"))
            {
                return LogType.Warning;
            }

            if (IsBuildDetailedLog(message))
            {
                return LogType.Build;
            }

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
                message.Contains("==========", StringComparison.OrdinalIgnoreCase))
            {
                return LogType.Git;
            }

            return LogType.Message;
        }

        private bool IsBuildDetailedLog(string message)
        {
            message = message.Trim();
            if (message.Length < 10 || message.Length > 500)
                return false;

            var fileExtensions = new[] { ".cs", ".xaml", ".resx", ".csproj", ".sln", ".json", ".xml", ".config" };
            bool hasFileExtension = fileExtensions.Any(ext =>
                message.Contains(ext, StringComparison.OrdinalIgnoreCase));

            bool hasFilePath = (message.Contains(":\\") || message.Contains("\\bin\\") ||
                                message.Contains("\\obj\\") || message.Contains("\\Properties\\"));

            bool hasBuildKeyword = message.Contains("->") || message.Contains("Copy ") ||
                                   message.Contains("Generate") || message.Contains("Task ") ||
                                   message.Contains("Target ") || message.Contains("CoreCompile") ||
                                   message.Contains("ResolveAssemblyReferences") ||
                                   message.Contains(".dll") || message.Contains(".exe");

            if (hasFileExtension && hasFilePath)
                return true;

            if (hasBuildKeyword && hasFilePath)
                return true;

            if (hasFilePath && message.Contains("\n"))
                return true;

            return false;
        }
    }
}
