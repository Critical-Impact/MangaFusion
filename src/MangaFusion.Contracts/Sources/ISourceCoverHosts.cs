namespace MangaFusion.Contracts.Sources;

/// <summary>Optional capability: a source declares the hosts its cover images are served from, so the
/// cover proxy (an SSRF guard that otherwise only allows a fixed set of first-party CDNs) will relay
/// them. Scraper sources serve covers from their own domain, which isn't known ahead of time.</summary>
public interface ISourceCoverHosts
{
    /// <summary>Hostnames (no scheme) the proxy may fetch cover images from for this source.</summary>
    IReadOnlyList<string> CoverHosts { get; }
}
