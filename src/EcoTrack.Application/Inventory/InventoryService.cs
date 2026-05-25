using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Inventory;

public class InventoryService
{
    private readonly IApplicationDbContext _dbContext;

    public InventoryService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<InventoryItemResponse>> GetItemsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.InventoryItems
            .Select(item => new InventoryItemResponse(
                item.Id,
                item.Name,
                item.Category.ToString(),
                item.QuantityKg,
                item.Unit,
                item.StandardPriceInr))
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryItemResponse> CreateItemAsync(
        CreateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var category = Enum.Parse<InventoryCategory>(request.Category, ignoreCase: true);
        var item = InventoryItem.Create(
            request.Name,
            category,
            request.QuantityKg,
            request.Unit,
            request.StandardPriceInr,
            DateTime.UtcNow);

        _dbContext.InventoryItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(item);
    }

    public async Task<InventoryItemResponse> UpdatePriceAsync(
        Guid id,
        UpdateInventoryPriceRequest request,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.InventoryItems
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Inventory item {id} was not found.");

        item.UpdateStandardPrice(
            request.StandardPriceInr,
            Enum.Parse<UserRole>(actorRole, ignoreCase: true),
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(item);
    }

    private static InventoryItemResponse ToResponse(InventoryItem item) =>
        new(item.Id, item.Name, item.Category.ToString(), item.QuantityKg, item.Unit, item.StandardPriceInr);
}
