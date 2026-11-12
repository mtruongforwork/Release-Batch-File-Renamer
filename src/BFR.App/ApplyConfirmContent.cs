namespace BFR.App;

using BFR.Presentation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Nội dung hộp xác nhận áp dụng (spec 7.4) cho <c>ContentDialog</c>: tóm tắt số thư mục bị tác động,
/// số tệp đổi phần mở rộng, gợi ý luôn có thể Hoàn tác và vài ví dụ "tên cũ → tên mới".
/// Tiêu đề (số tệp) do hộp thoại hiển thị; ở đây chỉ dựng phần thân.
/// </summary>
public static class ApplyConfirmContent
{
    public static string Title(ApplySummary summary) => Loc.F("ConfirmDialogMessage", summary.FileCount);

    public static UIElement Build(ApplySummary summary)
    {
        var root = new StackPanel { MinWidth = 380 };

        if (summary.FolderCount > 0) root.Children.Add(Bullet(Loc.F("ConfirmFolders", summary.FolderCount)));
        if (summary.ExtensionChangeCount > 0) root.Children.Add(Bullet(Loc.F("ConfirmExtensions", summary.ExtensionChangeCount)));
        root.Children.Add(Bullet(Loc.T("ConfirmUndoHint")));

        if (summary.Samples.Count > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = Loc.T("ConfirmSamples"),
                Style = (Style)Application.Current.FindResource("PanelHeaderTextStyle"),
                Margin = new Thickness(0, 14, 0, 4)
            });

            foreach (var (oldName, newName) in summary.Samples)
            {
                root.Children.Add(new TextBlock
                {
                    Text = $"{oldName}  →  {newName}",
                    FontFamily = (FontFamily)Application.Current.FindResource("AppFontMono"),
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
                    Margin = new Thickness(0, 1, 0, 1)
                });
            }
        }

        return root;
    }

    private static TextBlock Bullet(string text) => new()
    {
        Text = "• " + text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
        Margin = new Thickness(0, 2, 0, 2)
    };
}
