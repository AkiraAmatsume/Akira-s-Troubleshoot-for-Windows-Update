using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WindowsUpdateFixer
{
    /// <summary>
    /// 把资源键名（如 "SuccessBrush"）转换为对应的 Brush，供卡片绑定。
    /// </summary>
    public class BrushKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string;
            if (string.IsNullOrEmpty(key)) return Brushes.Transparent;

            try
            {
                var res = Application.Current.TryFindResource(key);
                if (res is Brush) return res;
            }
            catch { }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
