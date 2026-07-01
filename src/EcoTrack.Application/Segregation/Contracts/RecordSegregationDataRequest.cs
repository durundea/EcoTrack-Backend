namespace EcoTrack.Application.Segregation.Contracts;

public sealed record RecordSegregationDataRequest(
    decimal PlasticKg,
    decimal OrganicKg,
    decimal MetalKg,
    decimal PaperKg,
    decimal EWasteKg);
