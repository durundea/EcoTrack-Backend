using EcoTrack.Application.Recycling;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Xunit;
using Moq;
using EcoTrack.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.UnitTests.Recycling;

public class RecyclingServiceTests
{
    [Fact]
    public async Task AdvanceStage_FromSegregatedToProcessing_Succeeds()
    {
        // Arrange
        var batchId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            actorUserId,
            DateTime.UtcNow);

        var mockDbContext = CreateMockDbContext(new[] { batch });
        var service = new RecyclingService(mockDbContext);

        // Act
        var result = await service.AdvanceStageAsync(
            batch.Id,
            new AdvanceStageRequest("Processing"),
            actorUserId,
            CancellationToken.None);

        // Assert
        Assert.Equal("Processing", result.Stage);
        Assert.True(result.StageHistory.Count >= 2);
    }

    [Fact]
    public async Task AdvanceStage_InvalidTransition_ThrowsBadRequestException()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            actorUserId,
            DateTime.UtcNow);

        var mockDbContext = CreateMockDbContext(new[] { batch });
        var service = new RecyclingService(mockDbContext);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
            await service.AdvanceStageAsync(
                batch.Id,
                new AdvanceStageRequest("Collected"), // Invalid: can't go backwards
                actorUserId,
                CancellationToken.None));
    }

    private IApplicationDbContext CreateMockDbContext(IEnumerable<RecyclingBatch> batches)
    {
        var batchList = batches.ToList();
        var mockDbSet = new Mock<DbSet<RecyclingBatch>>();

        mockDbSet
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RecyclingBatch, bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<System.Linq.Expressions.Expression<Func<RecyclingBatch, bool>>, CancellationToken>((predicate, ct) =>
            {
                var batch = batchList.FirstOrDefault(predicate.Compile());
                return Task.FromResult(batch);
            });

        mockDbSet
            .Setup(x => x.AsNoTracking())
            .Returns(mockDbSet.Object);

        var mockContext = new Mock<IApplicationDbContext>();
        mockContext.Setup(x => x.RecyclingBatches).Returns(mockDbSet.Object);
        mockContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return mockContext.Object;
    }
}
