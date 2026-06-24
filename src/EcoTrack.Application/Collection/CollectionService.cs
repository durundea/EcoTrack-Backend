using EcoTrack.Application.Collection.Contracts;
using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Collection;

public class CollectionService
{
    private readonly IApplicationDbContext _dbContext;

    public CollectionService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<PickupListItemResponse>> GetPickupsAsync(
        GetPickupsQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

        var query = _dbContext.PickupTasks.AsNoTracking().AsQueryable();
        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.AssignedCollectorUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<PickupStatus>(request.Status, ignoreCase: true, out var status))
            {
                throw new BadRequestException("Invalid status value.");
            }

            query = query.Where(x => x.Status == status);
        }

        if (string.IsNullOrWhiteSpace(request.SortBy) || !request.SortBy.Equals("scheduledAtUtc", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(request.SortBy))
            {
                throw new BadRequestException("SortBy must be scheduledAtUtc.");
            }
        }

        var sortDirection = string.IsNullOrWhiteSpace(request.SortDirection) ? "desc" : request.SortDirection;
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.ScheduledAtUtc)
            : sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderByDescending(x => x.ScheduledAtUtc)
                : throw new BadRequestException("SortDirection must be asc or desc.");

        var totalCount = await query.CountAsync(cancellationToken);
        var pickups = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var collectorNames = await GetCollectorNamesAsync(pickups.Select(x => x.AssignedCollectorUserId), cancellationToken);
        var items = pickups.Select(x => ToListItemResponse(x, collectorNames)).ToList();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResponse<PickupListItemResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<PickupDetailResponse> GetByIdAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var pickup = await FindVisiblePickupAsync(id, actorUserId, actorRole, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> CreateAsync(CreatePickupRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var pickupCode = await GeneratePickupCodeAsync(cancellationToken);
        PickupTask pickup;

        try
        {
            pickup = PickupTask.CreateScheduled(
                request.SiteName,
                request.SiteAddressText,
                request.ScheduledAtUtc,
                request.EstimatedWeightKg,
                request.Notes,
                actorUserId,
                DateTime.UtcNow,
                pickupCode);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        _dbContext.PickupTasks.Add(pickup);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> UpdateAsync(Guid id, UpdatePickupRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var pickup = await FindVisiblePickupAsync(id, actorUserId, actorRole, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        if (string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                pickup.UpdateByAdmin(
                    request.SiteName ?? pickup.SiteName,
                    request.SiteAddressText ?? pickup.SiteAddressText,
                    request.ScheduledAtUtc ?? pickup.ScheduledAtUtc,
                    request.EstimatedWeightKg ?? pickup.EstimatedWeightKg,
                    request.Notes,
                    DateTime.UtcNow);
            }
            catch (InvalidOperationException ex)
            {
                throw new ConflictException(ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BadRequestException(ex.Message);
            }
            catch (ArgumentException ex)
            {
                throw new BadRequestException(ex.Message);
            }
        }
        else
        {
            if (request.SiteName is not null || request.SiteAddressText is not null || request.ScheduledAtUtc.HasValue || request.EstimatedWeightKg.HasValue)
            {
                throw new BadRequestException("Collectors can only update notes.");
            }

            try
            {
                pickup.UpdateNotes(request.Notes, DateTime.UtcNow);
            }
            catch (InvalidOperationException ex)
            {
                throw new ConflictException(ex.Message);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> AssignAsync(Guid id, AssignPickupRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only admins can assign pickups.");
        }

        var pickup = await _dbContext.PickupTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        var collector = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == request.AssignedCollectorUserId, cancellationToken)
            ?? throw new BadRequestException("AssignedCollectorUserId must reference an active collector.");

        if (collector.Role != UserRole.Collector || !collector.IsActive)
        {
            throw new BadRequestException("AssignedCollectorUserId must reference an active collector.");
        }

        try
        {
            pickup.AssignCollector(request.AssignedCollectorUserId, actorUserId, DateTime.UtcNow, request.Note);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> MarkCollectedAsync(Guid id, MarkCollectedRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var pickup = await FindVisiblePickupAsync(id, actorUserId, actorRole, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        try
        {
            pickup.MarkCollected(request.CollectedWeightKg, actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> SendToSegregationAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only admins can send pickups to segregation.");
        }

        var pickup = await _dbContext.PickupTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        try
        {
            pickup.SendToSegregation(DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupDetailResponse> CancelAsync(Guid id, CancelPickupRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only admins can cancel pickups.");
        }

        var pickup = await _dbContext.PickupTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        try
        {
            pickup.Cancel(actorUserId, DateTime.UtcNow, request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await ToDetailResponseAsync(pickup, cancellationToken);
    }

    public async Task<PickupHistoryResponse> GetAssignmentHistoryAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var pickup = await FindVisiblePickupAsync(id, actorUserId, actorRole, cancellationToken)
            ?? throw new NotFoundException("Pickup not found.");

        var events = await _dbContext.PickupAssignmentEvents
            .AsNoTracking()
            .Where(x => x.PickupTaskId == pickup.Id)
            .OrderBy(x => x.ChangedAtUtc)
            .Select(x => new AssignmentEventResponse(x.Id, x.PickupTaskId, x.PreviousCollectorUserId, x.NewCollectorUserId, x.ChangedByUserId, x.ChangedAtUtc, x.Note))
            .ToListAsync(cancellationToken);

        return new PickupHistoryResponse(events);
    }

    private async Task<PickupTask?> FindVisiblePickupAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        var query = _dbContext.PickupTasks.AsQueryable();
        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.AssignedCollectorUserId == actorUserId);
        }

        query = query.Where(x => x.Status != PickupStatus.Cancelled);
        return await query.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    private async Task<PickupDetailResponse> ToDetailResponseAsync(PickupTask pickup, CancellationToken cancellationToken)
    {
        string? displayName = null;
        if (pickup.AssignedCollectorUserId.HasValue)
        {
            displayName = await _dbContext.Users
                .Where(x => x.Id == pickup.AssignedCollectorUserId.Value)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var events = await _dbContext.PickupAssignmentEvents
            .AsNoTracking()
            .Where(x => x.PickupTaskId == pickup.Id)
            .OrderBy(x => x.ChangedAtUtc)
            .Select(x => new AssignmentEventResponse(x.Id, x.PickupTaskId, x.PreviousCollectorUserId, x.NewCollectorUserId, x.ChangedByUserId, x.ChangedAtUtc, x.Note))
            .ToListAsync(cancellationToken);

        return new PickupDetailResponse(
            pickup.Id,
            pickup.PickupCode,
            pickup.SiteName,
            pickup.SiteAddressText,
            pickup.ScheduledAtUtc,
            pickup.EstimatedWeightKg,
            pickup.CollectedWeightKg,
            pickup.Status.ToString(),
            pickup.AssignedCollectorUserId,
            displayName,
            pickup.Notes,
            pickup.CreatedByUserId,
            pickup.CreatedAtUtc,
            pickup.UpdatedAtUtc,
            pickup.CancelledByUserId,
            pickup.CancelledAtUtc,
            pickup.CancelReason,
            events);
    }

    private async Task<Dictionary<Guid, string>> GetCollectorNamesAsync(IEnumerable<Guid?> collectorIds, CancellationToken cancellationToken)
    {
        var ids = collectorIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    private static PickupListItemResponse ToListItemResponse(PickupTask pickup, IReadOnlyDictionary<Guid, string> collectorNames)
    {
        collectorNames.TryGetValue(pickup.AssignedCollectorUserId ?? Guid.Empty, out var displayName);

        return new PickupListItemResponse(
            pickup.Id,
            pickup.PickupCode,
            pickup.SiteName,
            pickup.SiteAddressText,
            pickup.ScheduledAtUtc,
            pickup.EstimatedWeightKg,
            pickup.CollectedWeightKg,
            pickup.Status.ToString(),
            pickup.AssignedCollectorUserId,
            displayName,
            pickup.Notes);
    }

    private async Task<string> GeneratePickupCodeAsync(CancellationToken cancellationToken)
    {
        var count = await _dbContext.PickupTasks.CountAsync(cancellationToken);
        return $"P-{1001 + count}";
    }
}