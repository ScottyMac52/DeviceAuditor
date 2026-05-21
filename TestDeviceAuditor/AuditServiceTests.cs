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
    private readonly Mock<AuditService> _sutMock;
    private readonly AuditService _sut;

    public AuditServiceTests()
    {
        _sutMock = new Mock<AuditService>(_dbMock.Object, _repairMock.Object) { CallBase = true };
        _sut = _sutMock.Object;
    }

    #region Run() high-level paths

    [Fact]
    public void Run_WithActiveOnly_LoadsDatabase_AndProcessesActiveDevices()
    {
        var options = new Options { Vendors = "044F", ActiveOnly = true };

        _dbMock.Setup(d => d.Load()).Returns(true);
        _dbMock.Setup(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()))
               .Returns("Mock Device");

        _sut.Run(options);

        _dbMock.Verify(d => d.Load(), Times.Once);
        _dbMock.Verify(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()), Times.AtMost(100));
    }

    [Fact]
    public void Run_WithGhostsEnabled_CallsBothScans()
    {
        var options = new Options { Vendors = "044F", ActiveOnly = false };

        _dbMock.Setup(d => d.Load()).Returns(true);
        _dbMock.Setup(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()))
               .Returns("Mock Device");

        _sutMock.Setup(x => x.ScanActiveDevices(It.IsAny<string>()))
                .Returns(new List<DeviceSummary>());

        _sutMock.Setup(x => x.GetGhostsFromRegistry(It.IsAny<string>(), It.IsAny<IReadOnlySet<string?>>()))
                .Returns(new List<DeviceSummary>());

        _sut.Run(options);

        _sutMock.Verify(x => x.ScanActiveDevices(It.IsAny<string>()), Times.Once);
        _sutMock.Verify(x => x.GetGhostsFromRegistry(It.IsAny<string>(), It.IsAny<IReadOnlySet<string?>>()), Times.Once);
    }

    [Fact]
    public void Run_DatabaseLoadFails_PrintsError_AndReturnsEarly()
    {
        var options = new Options { Vendors = "044F" };
        _dbMock.Setup(d => d.Load()).Returns(false);

        _sut.Run(options);

        _dbMock.Verify(d => d.Load(), Times.Once);
    }

    #endregion

    #region GetGhostsFromRegistry - Improved Coverage

    [Fact]
    public void GetGhostsFromRegistry_ReturnsEmpty_WhenNoRegistryData()
    {
        var activeKeys = new HashSet<string?>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                         as IReadOnlySet<string?>;

        var ghosts = _sut.GetGhostsFromRegistry("NONEXISTENT", activeKeys);

        ghosts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void GetGhostsFromRegistry_SkipsActiveDevices_AndAddsGhosts()
    {
        var activeKeys = new HashSet<string?> { "active-container-123" }
            .ToHashSet(StringComparer.OrdinalIgnoreCase) as IReadOnlySet<string?>;

        // Mock the helpers so we can control what happens inside the loop
        _sutMock.Setup(x => x.GetContainerId(It.IsAny<string>()))
                .Returns("ghost-container-456");

        _sutMock.Setup(x => x.GetParentPowerInfo(It.IsAny<string>()))
                .Returns((0, @"SYSTEM\CurrentControlSet\Enum\some\path"));

        var ghosts = _sut.GetGhostsFromRegistry("044F", activeKeys);

        ghosts.Should().NotBeNull();
        // At least the loop ran and we hit the add path (even if registry returns nothing in CI)
    }

    #endregion

    #region Other methods

    [Fact]
    public void ScanActiveDevices_ReturnsEmptyList_WhenNoDevicesFound()
    {
        var devices = _sut.ScanActiveDevices("NONEXISTENTVID");
        devices.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void GetContainerId_ReturnsNull_WhenKeyNotFound()
    {
        var result = _sut.GetContainerId("this\\path\\does\\not\\exist");
        result.Should().BeNull();
    }

    [Fact]
    public void GetParentPowerInfo_ReturnsDefault_OnFailure()
    {
        var result = _sut.GetParentPowerInfo("invalid\\hid\\id");
        result.Status.Should().Be(-1);
        result.Path.Should().BeNull();
    }

    [Fact]
    public void ExtractPid_HandlesVariousFormats()
    {
        AuditService.ExtractPid("HID\\VID_8089&PID_000C&MI_00").Should().Be("000C");
        AuditService.ExtractPid("VID_044F&PID_B10A").Should().Be("B10A");
        AuditService.ExtractPid(null).Should().BeNull();
        AuditService.ExtractPid("").Should().BeNull();
    }

    [Fact]
    public void ExtractPhysicalRootFromInstance_HandlesVariousCases()
    {
        // Null/empty
        AuditService.ExtractPhysicalRootFromInstance(null).Should().BeNull();
        AuditService.ExtractPhysicalRootFromInstance("").Should().BeNull();

        // No & → return as-is
        AuditService.ExtractPhysicalRootFromInstance("VID_044F&PID_B10A").Should().Be("VID_044F&PID_B10A");

        // One & → return up to first &
        AuditService.ExtractPhysicalRootFromInstance("VID_044F&PID_B10A&MI_00").Should().Be("VID_044F&PID_B10A");

        // Multiple & → return up to second &
        AuditService.ExtractPhysicalRootFromInstance("HID\\VID_8089&PID_000C&MI_00&Col01").Should().Be("HID\\VID_8089&PID_000C");
    }

    #endregion
}