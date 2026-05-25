using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Inventory;

public class SaleRecordTests
{
    [Fact]
    public void SubmitForApproval_WhenStatusIsDraft_ChangesStatusToPendingApproval()
    {
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 2, 120m, DateTime.UtcNow);

        sale.SubmitForApproval(Guid.NewGuid(), UserRole.Collector);

        sale.ApprovalStatus.Should().Be(SaleApprovalStatus.PendingApproval);
    }

    [Fact]
    public void Approve_WhenStatusIsPendingApproval_StoresApproverAndLocksRecord()
    {
        var approverId = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 2, 120m, DateTime.UtcNow);
        sale.SubmitForApproval(Guid.NewGuid(), UserRole.Collector);

        sale.Approve(approverId, UserRole.Admin, DateTime.UtcNow);

        sale.ApprovalStatus.Should().Be(SaleApprovalStatus.Approved);
        sale.ApprovedByUserId.Should().Be(approverId);
        sale.CanBeModified.Should().BeFalse();
    }

    [Fact]
    public void CreateDraft_WithZeroQuantity_ThrowsInvalidOperationException()
    {
        var action = () => SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 0, 120m, DateTime.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SubmitForApproval_WhenAlreadyPending_ThrowsInvalidOperationException()
    {
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 1, 60m, DateTime.UtcNow);
        sale.SubmitForApproval(Guid.NewGuid(), UserRole.Admin);
        var action = () => sale.SubmitForApproval(Guid.NewGuid(), UserRole.Admin);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Approve_WhenRoleIsCollector_ThrowsInvalidOperationException()
    {
        var sale = SaleRecord.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), 1, 60m, DateTime.UtcNow);
        sale.SubmitForApproval(Guid.NewGuid(), UserRole.Admin);
        var action = () => sale.Approve(Guid.NewGuid(), UserRole.Collector, DateTime.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }
}
