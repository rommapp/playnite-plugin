using System;
using System.Globalization;
using System.Windows.Data;

namespace RomM.Downloads
{
    /// <summary>
    /// MultiValueConverter used to display download progress as a single text line.
    ///
    /// Expected bindings (in this exact order):
    ///  values[0] = ProgressValue     (bytes downloaded so far)
    ///  values[1] = ProgressMaximum  (total bytes)
    ///  values[2] = IsIndeterminate  (true when total size is unknown)
    ///
    /// Example output:
    ///  "123.4 MB / 512.0 MB (24%)"
    ///
    /// This converter is intended to be used with MultiBinding so the UI updates
    /// whenever any progress-related property changes.
    /// </summary>
    internal sealed class ProgressLineMultiConverter : IMultiValueConverter
    {
        /// <summary>
        /// Converts progress values into a human-readable string.
        /// This method is automatically called by WPF whenever any of the bound
        /// values (ProgressValue, ProgressMaximum, IsIndeterminate) change.
        /// </summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Safety check: MultiBinding must provide all required values.
                if (values == null || values.Length < 3)
                {
                    return string.Empty;
                }

                // If progress is indeterminate, do not show numeric progress.
                if (values[2] is bool isIndeterminate && isIndeterminate)
                {
                    return string.Empty;
                }

                // Normalize numeric values to double (they may be long, int, etc.)
                double current = ToDouble(values[0]);
                double maximum = ToDouble(values[1]);

                // Invalid or unknown total size -> do not display progress.
                if (maximum <= 0)
                {
                    return string.Empty;
                }

                // Calculate percentage, clamped between 0 and 100.
                double pct = Math.Max(0, Math.Min(1, current / maximum)) * 100.0;

                // Build final progress string.
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
                // Value converters must NEVER throw exceptions in WPF.
                // Any failure results in an empty string.
                return string.Empty;
            }
        }

        /// <summary>
        /// ConvertBack is not supported because this converter is used for display only.
        /// </summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Safely converts various numeric types (int, long, float, double, string)
        /// into a double value.
        /// </summary>
        private static double ToDouble(object value)
        {
            if (value == null)
            {
                return 0;
            }

            // Fast paths for common numeric types.
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is long l) return l;
            if (value is int i) return i;

            // Fallback: attempt to parse from string.
            double.TryParse(
                value.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double result
            );

            return result;
        }

        /// <summary>
        /// Formats a byte count into a human-readable string:
        /// B, KB, MB, or GB.
        /// </summary>
        private static string FormatBytes(double bytes)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;

            if (bytes >= GB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / GB);

            if (bytes >= MB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / MB);

            if (bytes >= KB)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} KB", bytes / KB);

            return string.Format(CultureInfo.InvariantCulture, "{0:0} B", bytes);
        }
    }
}
