namespace BFR.Presentation.Tests;

using BFR.Presentation;
using BFR.Core;
using BFR.Core.Licensing;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Test trạng thái hộp thoại nâng cấp Pro (yêu cầu mục 3):
/// chưa dùng thử / đang dùng thử / hết hạn dùng thử / đã là Pro.
/// </summary>
public class UpgradeViewModelTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "bfr-upgrade-" + Guid.NewGuid().ToString("N"));

    public UpgradeViewModelTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private (LicenseService Service, UpgradeViewModel Vm) Create(DateTime? now = null)
    {
        var fs = new RealFileSystem();
        var clock = now ?? new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = new LicenseService(
            new LicenseStore(fs, Path.Combine(_temp, "license.json")),
            clock: () => clock,
            trialEnabled: true);

        var vm = new UpgradeViewModel(new LicenseCoordinator(service), priceProvider: null);
        return (service, vm);
    }

    [Fact]
    public void State_NotTrialed_ShowsBuyAndTrial()
    {
        var (_, vm) = Create();

        Assert.Equal(UpgradeState.NotTrialed, vm.State);
        Assert.True(vm.ShowTrialButton);
        Assert.False(vm.ShowTrialRemaining);
    }

    [Fact]
    public void State_Trialing_HidesTrialAndShowsDaysLeft()
    {
        var (service, vm) = Create();

        service.StartTrial();

        Assert.Equal(UpgradeState.Trialing, vm.State);
        Assert.False(vm.ShowTrialButton);
        Assert.True(vm.ShowTrialRemaining);
        Assert.Equal(30, vm.TrialDaysRemaining);
        Assert.False(string.IsNullOrWhiteSpace(vm.TrialRemainingText));
    }

    [Fact]
    public void State_TrialExpired_OnlyBuyAndLicenseKey()
    {
        var fs = new RealFileSystem();
        var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = new LicenseService(
            new LicenseStore(fs, Path.Combine(_temp, "license.json")),
            clock: () => now,
            trialEnabled: true);

        service.StartTrial();

        now = now.AddDays(31);
        service.Evaluate();

        var vm = new UpgradeViewModel(new LicenseCoordinator(service), priceProvider: null);

        Assert.Equal(UpgradeState.TrialExpired, vm.State);
        Assert.False(vm.ShowTrialButton);
        Assert.False(vm.ShowTrialRemaining);
        Assert.True(vm.ShowLicenseKeyLink);
    }

    [Fact]
    public void State_AlreadyPro_HidesTrial()
    {
        var (service, vm) = Create();

        service.RecordPurchase();

        Assert.Equal(UpgradeState.AlreadyPro, vm.State);
        Assert.False(vm.ShowTrialButton);
        Assert.False(vm.ShowLicenseKeyLink);
    }

    [Fact]
    public void ContextFeature_ProducesContextMessage()
    {
        var fs = new RealFileSystem();
        var service = new LicenseService(new LicenseStore(fs, Path.Combine(_temp, "license.json")), trialEnabled: false);
        var vm = new UpgradeViewModel(new LicenseCoordinator(service), null, ProFeature.ReportExport);

        Assert.True(vm.HasContext);
        Assert.NotNull(vm.ContextMessage);
    }

    [Fact]
    public void FeatureTable_ReflectsRealFreeLimits()
    {
        var (_, vm) = Create();

        Assert.Equal(17, vm.Features.Count);
        Assert.All(vm.Features, row => Assert.False(row.FreeHasIt));
        Assert.All(vm.Features, row => Assert.True(row.ProHasIt));
    }

    [Fact]
    public void ActivateKey_InvalidKey_ShowsErrorAndKeepsFree()
    {
        var (service, vm) = Create();
        var upgraded = false;
        vm.Upgraded += (_, _) => upgraded = true;

        vm.LicenseKeyText = "BFR-XXXXX-XXXXX-XXXXX-XXXXX";
        vm.ActivateKeyCommand.Execute(null);

        Assert.True(vm.HasLicenseKeyError);
        Assert.False(upgraded);
        Assert.Equal(LicenseTier.Free, service.Status.Tier);
    }

    [Fact]
    public void ActivateKey_ValidKey_UpgradesAndRaisesEvent()
    {
        var (service, vm) = Create();
        var upgraded = false;
        vm.Upgraded += (_, _) => upgraded = true;

        vm.LicenseKeyText = LicenseKey.Generate("upgrade-test");
        vm.ActivateKeyCommand.Execute(null);

        Assert.False(vm.HasLicenseKeyError);
        Assert.True(upgraded);
        Assert.Equal(LicenseTier.Pro, service.Status.Tier);
    }

    [Fact]
    public void TrialCommand_StartsTrial_AndRaisesUpgraded()
    {
        var (service, vm) = Create();
        var upgraded = false;
        vm.Upgraded += (_, _) => upgraded = true;

        vm.TrialCommand.Execute(null);

        Assert.True(upgraded);
        Assert.Equal(LicenseTier.Pro, service.Status.Tier);
        Assert.True(service.HasUsedTrial);
    }

    [Fact]
    public void ShelfRow_UsesRealFreeLimit_AndShelfHasContextKey()
    {
        var (_, vm) = Create();

        var row = Assert.Single(vm.Features, r => r.Feature == "FeatureUnlimitedShelf");
        Assert.Equal("FeatureLimitShelf(50)", row.FreeText);   // KeyOnlyStringProvider: "Key(arg)" — số lấy từ LicenseLimits.Free
        Assert.False(row.FreeHasIt);
        Assert.True(row.ProHasIt);
        Assert.Equal("FeatureUnlimitedShelf", UpgradeViewModel.FeatureResourceKey(ProFeature.UnlimitedShelf));
        Assert.Equal(50, LicenseLimits.Free.MaxShelfItems);
    }
}
