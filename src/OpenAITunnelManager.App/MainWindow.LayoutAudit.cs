using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private void NormalizeRemainingResponsiveLayouts()
    {
        NormalizeDiagnosticsSelector();
        NormalizeConnectionDetailColumns();
        NormalizeDashboardConnectionRows();
    }

    private void NormalizeDiagnosticsSelector()
    {
        var selector = DiagnosticsPage.Children
            .OfType<ComboBox>()
            .FirstOrDefault(item => Grid.GetRow(item) == 2);
        if (selector is null) return;

        selector.Width = double.NaN;
        selector.MaxWidth = double.PositiveInfinity;
        selector.HorizontalAlignment = HorizontalAlignment.Stretch;
        selector.MinWidth = 0;
    }

    private void NormalizeConnectionDetailColumns()
    {
        if (_connectionsDetailsPanel is null) return;

        foreach (var grid in FindDescendants<Grid>(_connectionsDetailsPanel))
        {
            if (grid.ColumnDefinitions.Count != 2) continue;
            var first = grid.ColumnDefinitions[0];
            var second = grid.ColumnDefinitions[1];
            if (!first.Width.IsAbsolute || !second.Width.IsStar) continue;
            if (first.Width.Value < 100 || first.Width.Value > 180) continue;

            first.Width = new GridLength(28, GridUnitType.Star);
            second.Width = new GridLength(72, GridUnitType.Star);
        }
    }

    private void NormalizeDashboardConnectionRows()
    {
        foreach (var grid in FindDescendants<Grid>(DashboardPage))
        {
            if (grid.ColumnDefinitions.Count != 3) continue;
            var last = grid.ColumnDefinitions[2];
            if (!last.Width.IsAbsolute || last.Width.Value < 100 || last.Width.Value > 160) continue;

            grid.ColumnDefinitions[0].Width = new GridLength(60, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(20, GridUnitType.Star);
            grid.ColumnDefinitions[2].Width = new GridLength(20, GridUnitType.Star);
        }
    }
}
