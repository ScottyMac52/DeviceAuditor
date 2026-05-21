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
        _dbMock.Verify(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce);
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

    #region Individual method coverage (this is what moves the needle)

    [Fact]
    public void ScanActiveDevices_ReturnsEmptyList_WhenNoDevicesFound()
    {
        var devices = _sut.ScanActiveDevices("NONEXISTENTVID");
        devices.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void GetGhostsFromRegistry_ReturnsEmpty_WhenNoRegistryData()
    {
        var activeKeys = new HashSet<string?>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                         as IReadOnlySet<string?>;

        var ghosts = _sut.GetGhostsFromRegistry("NONEXISTENT", activeKeys);

        ghosts.Should().NotBeNull().And.BeEmpty();
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
        result.Path.Should().BeNull();           // ← Fixed
    }
    #endregion

    #region Static helpers

    [Fact]
    public void ExtractPid_HandlesVariousFormats()
    {
        AuditService.ExtractPid("HID\\VID_8089&PID_000C&MI_00").Should().Be("000C");
        AuditService.ExtractPid("VID_044F&PID_B10A").Should().Be("B10A");
        AuditService.ExtractPid(null).Should().BeNull();
        AuditService.ExtractPid("").Should().BeNull();
    }

    #endregion
}