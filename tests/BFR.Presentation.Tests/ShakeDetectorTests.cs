namespace BFR.Presentation.Tests;

using BFR.Presentation;

using System.Linq;

using Xunit;

/// <summary>Test cho nhận diện lắc chuột (logic thuần, không cần hook Win32).</summary>
public class ShakeDetectorTests
{
    /// <summary>Sinh chuỗi mẫu: <paramref name="swings"/> nhịp, mỗi nhịp đi <paramref name="amp"/> px, 4 mẫu/nhịp.</summary>
    private static bool Run(ShakeDetector d, int swings, int amp, int stepMs, int startX = 500, long startT = 0)
    {
        int x = startX; long t = startT;
        for (int i = 0; i < swings; i++)
        {
            int dir = i % 2 == 0 ? 1 : -1;
            for (int k = 0; k < 4; k++)
            {
                x += dir * (amp / 4); t += stepMs;
                if (d.Feed(x, t)) return true;
            }
        }
        return false;
    }

    [Fact]
    public void FastBackAndForth_Triggers() => Assert.True(Run(new ShakeDetector(), 6, 120, 20));

    [Fact]
    public void SingleSweep_DoesNotTrigger() => Assert.False(Run(new ShakeDetector(), 1, 600, 20));

    [Fact]
    public void OneReversal_DoesNotTrigger() => Assert.False(Run(new ShakeDetector(), 2, 300, 20));

    [Fact]
    public void SlowReversals_DoNotTrigger() => Assert.False(Run(new ShakeDetector(), 6, 120, 150));

    [Fact]
    public void TinyJitter_DoesNotTrigger() => Assert.False(Run(new ShakeDetector(), 12, 16, 10));

    [Fact]
    public void Cooldown_SuppressesImmediateRetrigger()
    {
        var d = new ShakeDetector();
        Assert.True(Run(d, 6, 120, 20));
        Assert.False(Run(d, 6, 120, 20, startT: 200));
    }

    [Fact]
    public void AfterCooldown_TriggersAgain()
    {
        var d = new ShakeDetector();
        Assert.True(Run(d, 6, 120, 20));
        Assert.True(Run(d, 6, 120, 20, startT: 5000));
    }

    [Fact]
    public void Reset_ForgetsPreviousMovement()
    {
        var d = new ShakeDetector();
        Assert.False(Run(d, 2, 120, 20));
        d.Reset();
        Assert.False(Run(d, 2, 120, 20, startT: 100));
    }
}
