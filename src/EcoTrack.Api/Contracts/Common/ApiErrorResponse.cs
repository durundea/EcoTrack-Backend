namespace EcoTrack.Api.Contracts.Common;

public sealed record ApiErrorResponse(int Status, string Message);
