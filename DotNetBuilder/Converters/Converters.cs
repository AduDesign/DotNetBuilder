using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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
}
