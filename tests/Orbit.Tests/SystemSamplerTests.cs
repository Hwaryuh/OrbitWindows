using Orbit.Core.SystemInfo;

namespace Orbit.Tests;

public class SystemSamplerTests
{
    [Fact]
    public void CpuIsTheBusyShareSinceTheLastReading()
    {
        // Kernel time includes idle: 100 ticks of kernel+user, 75 of them idle.
        Assert.Equal(25, SystemSampler.CpuPercent((1000, 2000, 500), (1075, 2080, 520)), 6);
        Assert.Equal(0, SystemSampler.CpuPercent((1, 1, 1), (1, 1, 1)));
    }

    [Fact]
    public void RatesAreBytesASecondAndNeverNegative()
    {
        Assert.Equal((1000.0, 500.0), SystemSampler.RatesPerSecond((0, 0, 0), (2000, 1000, 2000)));
        Assert.Equal((0.0, 0.0), SystemSampler.RatesPerSecond((5000, 5000, 0), (10, 10, 1000)));
        Assert.Equal((0.0, 0.0), SystemSampler.RatesPerSecond((0, 0, 1000), (10, 10, 1000)));
    }

    [Fact]
    public void ASampleOfThisPcIsSane()
    {
        using var sampler = new SystemSampler();
        sampler.Sample();
        Thread.Sleep(200);
        var reading = sampler.Sample();
        Assert.InRange(reading.Cpu, 0, 100);
        Assert.True(reading.MemoryTotal > 0 && reading.MemoryUsed <= reading.MemoryTotal);
        Assert.True(reading.DiskTotal > 0);
        Assert.True(reading.Gpu is null or >= 0 and <= 100);
        Assert.Equal(2, sampler.CpuHistory.Count);
        Assert.NotEmpty(sampler.TopProcesses(5));
    }
}
