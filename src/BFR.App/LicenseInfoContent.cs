namespace BFR.App;

using BFR.Core.Licensing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Nội dung hộp thông tin license cho người dùng đã là Pro (mở khi bấm badge "Pro"): chỉ hiển thị
/// trạng thái, ngày mua/dùng thử và nguồn kích hoạt. Dựng cho <c>ContentDialog</c>.
/// </summary>
public static class LicenseInfoContent
{
    public static UIElement Build(LicenseService license, LicenseStatus status)
    {
        var root = new StackPanel { MinWidth = 400 };


        root.Children.Add(new TextBlock
        {
            Text = Loc.T("LicenseInfoSubtitle"),
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary")
        });

        AddRow(root, Loc.T("LicenseInfoStatus"), Loc.T("LicenseInfoActive"));
        if (status.IsTrial && status.TrialEndsUtc is not null)
        {
            AddRow(root, Loc.T("LicenseInfoTrial"), status.TrialEndsUtc.Value.ToLocalTime().ToString("dd/MM/yyyy"));
        }
        else if (license.PurchasedUtc is not null)
        {
            AddRow(root, Loc.T("LicenseInfoPurchased"), license.PurchasedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy"));
        }
        AddRow(root, Loc.T("LicenseInfoSource"), license.ActivatedByKey ? Loc.T("LicenseInfoSourceKey") : Loc.T("LicenseInfoSourceStore"));
        // UserControl (không phải Panel) để có automation peer: Panel không lộ ra UI Automation.
        var host = new UserControl { Content = root };
        System.Windows.Automation.AutomationProperties.SetName(host, "License information");
        return host;
    }

    private static void AddRow(Panel parent, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var l = new TextBlock { Text = label, Foreground = (Brush)Application.Current.FindResource("TextSecondary") };
        var v = new TextBlock { Text = value, Foreground = (Brush)Application.Current.FindResource("TextPrimary") };
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
        parent.Children.Add(grid);
    }
}
