namespace EcoTrack.Application.Recycling.Contracts;

public record CreateConversionRequest(
    string ProductName,
    decimal Quantity,
    string Unit);
