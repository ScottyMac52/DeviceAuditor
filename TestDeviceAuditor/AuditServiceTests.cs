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
        _sutMock = new Mock<AuditService>(_dbMock.Object, _repairMock.Object)
        {
            CallBase = true
        };
        _sut = _sutMock.Object;
    }

    [Fact]
    public void Run_WithActiveOnly_Works()
    {
        var options = new Options { Vendors = "044F", ActiveOnly = true };

        _dbMock.Setup(d => d.Load()).Returns(true);
        _dbMock.Setup(d => d.GetName(It.IsAny<string>(), It.IsAny<string>()))
               .Returns("Mock Device");

        _sut.Run(options);

        _dbMock.Verify(d => d.Load(), Times.Once);
    }

    [Fact]
    public void Run_WithGhostsEnabled_CallsGhostScan()
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

        _sutMock.Verify(x => x.GetGhostsFromRegistry(It.IsAny<string>(), It.IsAny<IReadOnlySet<string?>>()), Times.Once);
    }

    [Fact]
    public void ExtractPid_Works()
    {
        AuditService.ExtractPid("VID_044F&PID_B10A").Should().Be("B10A");
    }
}