using EcoTrack.Domain.Auth;

namespace EcoTrack.Domain.Inventory;

public class SaleRecord
{
    private SaleRecord() { }

    public Guid Id { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public int QuantitySold { get; private set; }
    public decimal RevenueInr { get; private set; }
    public DateTime SoldAtUtc { get; private set; }
    public SaleApprovalStatus ApprovalStatus { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public bool CanBeModified => ApprovalStatus != SaleApprovalStatus.Approved;

    public static SaleRecord CreateDraft(Guid inventoryItemId, Guid requestedByUserId, int quantitySold, decimal revenueInr, DateTime soldAtUtc)
    {
        if (quantitySold <= 0) throw new InvalidOperationException("Quantity sold must be greater than zero.");
        if (revenueInr < 0) throw new InvalidOperationException("Revenue must be non-negative.");

        return new SaleRecord
        {
            Id = Guid.NewGuid(),
            InventoryItemId = inventoryItemId,
            RequestedByUserId = requestedByUserId,
            QuantitySold = quantitySold,
            RevenueInr = revenueInr,
            SoldAtUtc = soldAtUtc,
            ApprovalStatus = SaleApprovalStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    public void SubmitForApproval(Guid actorUserId, UserRole actorRole)
    {
        if (ApprovalStatus != SaleApprovalStatus.Draft)
            throw new InvalidOperationException("Only draft sales can be submitted for approval.");
        ApprovalStatus = SaleApprovalStatus.PendingApproval;
        UpdatedAtUtc = DateTime.UtcNow;
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

    public void EnsureCanBeModified()
    {
        if (!CanBeModified)
            throw new InvalidOperationException("Approved sales cannot be modified.");
    }
}
