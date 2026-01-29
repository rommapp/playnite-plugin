using System;
using System.Globalization;
using System.Windows.Data;

namespace RomM.Downloads
{
    /// <summary>
    /// values[0] = ProgressValue
    /// values[1] = ProgressMaximum
    /// values[2] = IsIndeterminate
    /// </summary>
    internal sealed class ProgressLineMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 3)
                {
                    return string.Empty;
                }

                // IsIndeterminate
                if (values[2] is bool isIndeterminate && isIndeterminate)
                {
                    return string.Empty;
                }

                double current = ToDouble(values[0]);
                double maximum = ToDouble(values[1]);

                if (maximum <= 0)
                {
                    return string.Empty;
                }

                double pct = Math.Max(0, Math.Min(1, current / maximum)) * 100.0;

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} / {1} ({2:0}%)",
                    FormatBytes(current),
                    FormatBytes(maximum),
                    pct
                );
            }
            catch
            {
                return string.Empty;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static double ToDouble(object value)
        {
            if (value == null)
            {
                return 0;
            }

            if (value is double d) return d;
            if (value is float f) return f;
            if (value is long l) return l;
            if (value is int i) return i;

            double.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double result
            );

            return result;
        }

        private static string FormatBytes(double bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / GB);
            if (bytes >= MB) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / MB);
            if (bytes >= KB) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / KB);
            return string.Format(CultureInfo.InvariantCulture, "{0:0} B", bytes);
        }
    }
}
