namespace BFR.Core.Tests;

using BFR.Core;
using BFR.Core.Licensing;
using Xunit;

public class LicenseV2Tests
{
    private static LicenseService Service(LicenseTier tier) =>
        new(new LicenseStore(new FakeFileSystem(), "/license.json"), tierOverride: () => tier, trialEnabled: false);

    [Fact]
    public void FreeLimits_MatchTheV2Spec()
    {
        var free = LicenseLimits.Free;
        Assert.Equal(2, free.MaxOrganizeDepth);
        Assert.Equal(1000, free.MaxDuplicateFiles);
        Assert.Equal(20, free.MaxImagesPerBatch);
        Assert.False(free.AllowSimilarImages);
        Assert.False(free.AllowExifWrite);
        Assert.False(free.AllowArchiveAdvanced);
        Assert.False(free.AllowAnalyzeExport);
        Assert.False(free.AllowWorkflow);
        Assert.Equal(QuarantineStore.DefaultRetentionDays, free.MaxQuarantineDays);
    }

    [Fact]
    public void ProHasEverythingAndTheFeatureGatesFollowTheTier()
    {
        var free = Service(LicenseTier.Free);
        var pro = Service(LicenseTier.Pro);

        foreach (var feature in new[]
        {
            ProFeature.OrganizeDeepTemplate, ProFeature.DuplicatesUnlimited, ProFeature.SimilarImages, ProFeature.HardLinkDedupe,
            ProFeature.ImageUnlimited, ProFeature.ExifWrite, ProFeature.ArchiveAdvanced, ProFeature.AnalyzeExport,
            ProFeature.Workflow, ProFeature.QuarantineRetention
        })
        {
            Assert.False(free.Allows(feature), feature.ToString());
            Assert.True(pro.Allows(feature), feature.ToString());
            Assert.NotEqual("Tính năng này cần bản Pro.", FeatureMessages.For(feature));
        }

        Assert.Equal(90, LicenseLimits.Pro.MaxQuarantineDays);
        Assert.Equal(2, free.MaxOrganizeDepth);
        Assert.Equal(int.MaxValue, pro.MaxImagesPerBatch);
    }
}
