namespace BFR.App;

using BFR.Presentation;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

/// <summary>
/// Nội dung hộp thoại nâng cấp Pro, hiện trong <c>ContentDialog</c> của cửa sổ chính (không còn cửa sổ riêng).
///
/// - Bố cục và nội dung do <see cref="UpgradeViewModel"/> quyết định (Free/Pro, trạng thái dùng thử).
/// - Mọi vùng có AutomationProperties.Name; lỗi kích hoạt có thông báo riêng.
/// - <see cref="CloseRequested"/> báo cho nơi chứa đóng hộp thoại (bấm "Để sau" hoặc sau khi nâng cấp xong).
/// </summary>
public partial class UpgradeView : UserControl
{
    private readonly UpgradeViewModel _vm;

    public event EventHandler? CloseRequested;

    public UpgradeView(UpgradeViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.Upgraded += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        Loaded += async (_, _) => await _vm.LoadPriceAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Buy_Click(object sender, RoutedEventArgs e) => _vm.BuyCommand.Execute(null);

    private void Trial_Click(object sender, RoutedEventArgs e) => _vm.TrialCommand.Execute(null);

    private void ShowLicenseKey_Click(object sender, RoutedEventArgs e) => _vm.ShowLicenseKeyCommand.Execute(null);

    private void ActivateKey_Click(object sender, RoutedEventArgs e) => _vm.ActivateKeyCommand.Execute(null);

    private void LicenseKey_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        _vm.ActivateKeyCommand.Execute(null);
        e.Handled = true;
    }
}
