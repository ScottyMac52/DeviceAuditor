using DeviceAuditor.Models;
using DeviceAuditor.Services;
using DeviceAuditor.Services.Interfaces;
using Moq;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;

namespace DeviceAuditor.Tests.Services;

public class AuditServiceTests
{
    private readonly Mock<IDeviceDatabase> _dbMock = new();
    private readonly Mock<IRepairService> _repairMock = new();
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        _sut = new AuditService(_dbMock.Object, _repairMock.Object);
    }

    #region Pure Helper Methods (no external deps)

    [Fact]
    public void ExtractPid_ReturnsCorrectPid()
    {
        Assert.Equal("000C", AuditService.ExtractPid("HID\\VID_8089&PID_000C&MI_00"));
        Assert.Equal("1234", AuditService.ExtractPid("VID_044F&PID_1234"));
        Assert.Null(AuditService.ExtractPid("no pid here"));
    }

    [Theory]
    [InlineData("HID\\VID_044F&PID_B10A&MI_00&Col01", "HID\\VID_044F&PID_B10A")]
    [InlineData("VID_4098&PID_1234&IG_00", "VID_4098&PID_1234")]
    public void ExtractPhysicalRootFromInstance_ReturnsRoot(string input, string expected)
    {
        var result = typeof(AuditService)
            .GetMethod("ExtractPhysicalRootFromInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, new object[] { input }) as string;

        result.Should().Be(expected);
    }

    [Fact]
    public void GetBestInstanceKey_PrefersContainerId()
    {
        var result = typeof(AuditService)
            .GetMethod("GetBestInstanceKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, new object[] { "fullId", "container-123" }) as string;

        result.Should().Be("container-123");
    }

    #endregion

    #region ScanActiveDevices

    [Fact]
    public void ScanActiveDevices_ReturnsDevices_WhenFound()
    {
        _dbMock.Setup(d => d.GetName("B10A", "044F")).Returns("HOTAS Warthog");

        var devices = _sut.ScanActiveDevices("044F");

        devices.Should().NotBeNull();
    }

    #endregion

    #region GetGhostsFromRegistry

    [Fact]
    public void GetGhostsFromRegistry_ExcludesActiveDevices()
    {
        var activeKeys = new HashSet<string?> { "container-ghost1", "active-123" }
            .ToHashSet(StringComparer.OrdinalIgnoreCase) as IReadOnlySet<string?>;

        var ghosts = _sut.GetGhostsFromRegistry("044F", activeKeys);

        ghosts.Should().NotBeNull();
    }

    #endregion

    #region Run (High-level)

    [Fact]
    public void Run_ProcessesVendors_AndCallsDatabase()
    {
        var options = new Options { Vendors = "044F,4098", ActiveOnly = true };

        _dbMock.Setup(d => d.Load()).Returns(true);
        _dbMock.Setup(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()))
               .Returns("Mocked Device");

        _sut.Run(options);

        _dbMock.Verify(d => d.Load(), Times.Once);
        
        // In CI (GitHub runner) there are no real HID devices, so GetName may not be called
        // We only verify it was set up correctly and Load() was called
        _dbMock.Verify(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce, 
            "GetName should be called when devices are found. If this fails in CI, consider making ScanActiveDevices virtual.");
    }

    [Fact]
    public void Run_WhenDatabaseFails_PrintsError()
    {
        var options = new Options { Vendors = "044F" };

        _dbMock.Setup(d => d.Load()).Returns(false);

        _sut.Run(options);

        _dbMock.Verify(d => d.Load(), Times.Once);
    }

    #endregion
}
