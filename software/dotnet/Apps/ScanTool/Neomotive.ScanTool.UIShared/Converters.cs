using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Neomotive.ScanTool.UI;

public static class Converters
{
    public static readonly IValueConverter FaultBackgroundConverter =
        new FuncValueConverter<bool, IBrush>(hasFaults =>
            hasFaults ? new SolidColorBrush(Color.Parse("#4A1818")) : new SolidColorBrush(Color.Parse("#1B382B")));

    public static readonly IValueConverter FaultForegroundConverter =
        new FuncValueConverter<bool, IBrush>(hasFaults =>
            hasFaults ? new SolidColorBrush(Color.Parse("#FF6B6B")) : new SolidColorBrush(Color.Parse("#52B788")));
}
