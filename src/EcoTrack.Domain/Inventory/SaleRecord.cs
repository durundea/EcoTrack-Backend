using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class SaleRecord : Entity
{
    private SaleRecord() { }

    public Guid InventoryItemId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? RejectedByUserId { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public int QuantitySold { get; private set; }
    public decimal RevenueInr { get; private set; }
    public DateTime SoldAtUtc { get; private set; }
    public SaleApprovalStatus ApprovalStatus { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public bool CanBeModified => ApprovalStatus != SaleApprovalStatus.Approved;

    public static SaleRecord CreateDraft(Guid inventoryItemId, Guid requestedByUserId, int quantitySold, decimal revenueInr, DateTime soldAtUtc, DateTime createdAtUtc)
    {
        if (quantitySold <= 0) throw new ArgumentOutOfRangeException(nameof(quantitySold), "Quantity sold must be greater than zero.");
        if (revenueInr < 0) throw new ArgumentOutOfRangeException(nameof(revenueInr), "Revenue must be non-negative.");

        return new SaleRecord
        {
            Id = Guid.NewGuid(),
            InventoryItemId = inventoryItemId,
            RequestedByUserId = requestedByUserId,
            QuantitySold = quantitySold,
            RevenueInr = revenueInr,
            SoldAtUtc = soldAtUtc,
            ApprovalStatus = SaleApprovalStatus.Draft,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public void SubmitForApproval(Guid actorUserId, UserRole actorRole, DateTime submittedAtUtc)
    {
        if (ApprovalStatus != SaleApprovalStatus.Draft)
            throw new InvalidOperationException("Only draft sales can be submitted for approval.");
        if (actorRole == UserRole.Collector && actorUserId != RequestedByUserId)
            throw new InvalidOperationException("Collectors can submit only their own sales.");
        ApprovalStatus = SaleApprovalStatus.PendingApproval;
        UpdatedAtUtc = submittedAtUtc;
    }

    public void Approve(Guid approverUserId, UserRole approverRole, DateTime approvedAtUtc)
    {
        if (approverRole != UserRole.Admin)
            throw new InvalidOperationException("Only admins can approve sales.");
        if (ApprovalStatus != SaleApprovalStatus.PendingApproval)
            throw new InvalidOperationException("Only pending sales can be approved.");
        ApprovalStatus = SaleApprovalStatus.Approved;
        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = approvedAtUtc;
        UpdatedAtUtc = approvedAtUtc;
    }

    public void Reject(Guid rejectorUserId, UserRole rejectorRole, string reason, DateTime rejectedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Rejection reason is required.", nameof(reason));
        if (rejectorRole != UserRole.Admin)
            throw new InvalidOperationException("Only admins can reject sales.");
        if (ApprovalStatus != SaleApprovalStatus.PendingApproval)
            throw new InvalidOperationException("Only pending sales can be rejected.");
        ApprovalStatus = SaleApprovalStatus.Rejected;
        RejectedByUserId = rejectorUserId;
        RejectionReason = reason;
        RejectedAtUtc = rejectedAtUtc;
        UpdatedAtUtc = rejectedAtUtc;
    }

    public void EnsureCanBeModified()
    {
        if (!CanBeModified)
            throw new InvalidOperationException("Approved sales cannot be modified.");
    }

    public void UpdateDraft(int quantitySold, DateTime soldAtUtc, DateTime updatedAtUtc)
    {
        EnsureCanBeModified();
        if (quantitySold <= 0) throw new ArgumentOutOfRangeException(nameof(quantitySold), "Quantity sold must be greater than zero.");
        QuantitySold = quantitySold;
        SoldAtUtc = soldAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }
}
