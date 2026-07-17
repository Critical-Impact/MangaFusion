namespace MangaFusion.Contracts.Models;

/// <summary>Describes one credential input a source needs, driving the admin config form.
/// <see cref="Secret"/> fields are rendered masked and never returned to the client once set.</summary>
public sealed record CredentialField(string Name, string Label, bool Secret);
