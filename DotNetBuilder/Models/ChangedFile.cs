using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DotNetBuilder.Models
{
    /// <summary>
    /// 文件更改状态
    /// </summary>
    public enum FileChangeStatus
    {
        /// <summary>
        /// 新增文件（未跟踪或已暂存的新文件）
        /// </summary>
        Added,

        /// <summary>
        /// 修改文件
        /// </summary>
        Modified,

        /// <summary>
        /// 删除文件
        /// </summary>
        Deleted,

        /// <summary>
        /// 重命名文件
        /// </summary>
        Renamed,

        /// <summary>
        /// 未跟踪文件
        /// </summary>
        Untracked,

        /// <summary>
        /// 冲突文件
        /// </summary>
        Conflicted
    }

    /// <summary>
    /// 更改的文件信息
    /// </summary>
    public class ChangedFile : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        /// <summary>
        /// 文件路径（相对于项目根目录）
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 文件更改状态
        /// </summary>
        public FileChangeStatus Status { get; set; }

        /// <summary>
        /// 原始索引状态（git status 的 XY 标记）
        /// </summary>
        public string RawStatus { get; set; } = string.Empty;

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 状态显示文本
        /// </summary>
        public string StatusText => Status switch
        {
            FileChangeStatus.Added => "新增",
            FileChangeStatus.Modified => "修改",
            FileChangeStatus.Deleted => "删除",
            FileChangeStatus.Renamed => "重命名",
            FileChangeStatus.Untracked => "未跟踪",
            FileChangeStatus.Conflicted => "冲突",
            _ => "未知"
        };

        /// <summary>
        /// 简写
        /// </summary>
        public string StatusShortText => Status switch
        {
            FileChangeStatus.Added => "A",
            FileChangeStatus.Modified => "M",
            FileChangeStatus.Deleted => "D",
            FileChangeStatus.Renamed => "R",
            FileChangeStatus.Untracked => "N",
            FileChangeStatus.Conflicted => "C",
            _ => "U"
        };

        /// <summary>
        /// 状态颜色
        /// </summary>
        public string StatusColor => Status switch
        {
            FileChangeStatus.Added => "#28A745",      // 绿色
            FileChangeStatus.Modified => "#0078D4",   // 蓝色
            FileChangeStatus.Deleted => "#CB2431",    // 红色
            FileChangeStatus.Renamed => "#6F42C1",    // 紫色
            FileChangeStatus.Untracked => "#95000000",  // 灰色
            FileChangeStatus.Conflicted => "#D73A49", // 深红色
            _ => "#666666"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}