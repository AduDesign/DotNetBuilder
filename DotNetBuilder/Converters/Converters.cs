using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DotNetBuilder.Models;

namespace DotNetBuilder.Converters
{
    /// <summary>
    /// 布尔值取反转换器（InverseBoolConverter）
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }
    }

    /// <summary>
    /// 布尔值反向可见性转换器（true->Collapsed, false->Visible）
    /// </summary>
    public class InverseBoolVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值转克隆按钮文本
    /// </summary>
    public class BoolToTextCloneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCloning)
                return isCloning ? "克隆中..." : "克隆";
            return "克隆";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值取反转换器（别名）
    /// </summary>
    public class BoolToInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return value;
        }
    }

    /// <summary>
    /// 布尔值转可见性（反向：true->Collapsed, false->Visible）
    /// </summary>
    public class BooleanToReVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v != Visibility.Visible;
            return true;
        }
    }

    /// <summary>
    /// 布尔值转可见性
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                bool invert = parameter?.ToString() == "Invert";
                return (b ^ invert) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;
            return false;
        }
    }

    /// <summary>
    /// 有更改时显示不同的图标或颜色
    /// </summary>
    public class HasChangesToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasChanges && hasChanges)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA500")); // 橙色
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // 绿色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 有更改时显示图标
    /// </summary>
    public class HasChangesToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool hasChanges && hasChanges)
            {
                return "\uE8B7"; // 修改图标
            }
            return "\uE8FB"; // 勾选图标
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 项目类型转显示文本
    /// </summary>
    public class ProjectTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDotNet)
            {
                return isDotNet ? ".NET" : "Git";
            }
            return "Git";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 空字符串转可见性
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue = !string.IsNullOrEmpty(value as string);
            bool invert = parameter?.ToString() == "Invert";
            return (hasValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 数量转显示文本
    /// </summary>
    public class ChangesCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count > 0)
            {
                return $"{count} 个更改";
            }
            return "无更改";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 远程状态转提示文本
    /// </summary>
    public class RemoteStatusTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RemoteStatusInfo status)
            {
                var parts = new List<string>();
                if (status.LocalAheadCount > 0)
                    parts.Add($"↑ {status.LocalAheadCount} 个提交待推送");
                if (status.RemoteAheadCount > 0)
                    parts.Add($"↓ {status.RemoteAheadCount} 个提交待拉取");

                if (status.FetchFailed)
                    parts.Add("⚠ 连接远程失败");

                return parts.Count > 0 ? string.Join("\n", parts) : "远程状态正常";
            }
            return "远程状态";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 判断是否为目录并转换为图标路径数据（文件夹图标）
    /// </summary>
    public class IsDirectoryToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDirectory && isDirectory)
            {
                // 文件夹图标 - Segoe MDL2 Assets
                return Geometry.Parse("M10,4H4C2.9,4 2,4.9 2,6V18C2,19.1 2.9,20 4,20H20C21.1,20 22,19.1 22,18V8C22,6.9 21.1,6 20,6H12L10,4Z");
            }
            // 文件图标
            return Geometry.Parse("M14,2H6C4.9,2 4,2.9 4,4V20C4,21.1 4.9,22 6,22H18C19.1,22 20,21.1 20,20V8L14,2M18,20H6V4H13V9H18V20Z");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 判断是否为目录并转换为图标颜色
    /// </summary>
    public class IsDirectoryToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDirectory && isDirectory)
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107")); // 黄色 - 文件夹
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#90CAF9")); // 蓝色 - 文件
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 7z 状态转换为颜色（绿色可用，红色不可用）
    /// </summary>
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAvailable)
            {
                if (isAvailable)
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // 绿色
                }
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")); // 红色
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 7z 状态转换为文本
    /// </summary>
    public class BoolToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAvailable)
            {
                return isAvailable ? "✓ 7z 已就绪" : "✗ 7z 未安装";
            }
            return "状态未知";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 打包按钮文本转换器
    /// </summary>
    public class BoolToPackButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPacking)
            {
                return isPacking ? "压缩中..." : "开始打包";
            }
            return "开始打包";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
