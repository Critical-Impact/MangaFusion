namespace MangaFusion.Contracts.Models;

/// <summary>A page of results from a source, with enough info to drive pagination.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Limit,
    int Offset);
