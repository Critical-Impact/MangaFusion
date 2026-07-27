using Microsoft.Extensions.Configuration;
using PDFtoImage;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Rasterizes PDF pages to JPEG files so a PDF can be imported through the same page-file
/// pipeline as a CBZ/folder source. Backed by PDFium (via the PDFtoImage package) — its own docs say
/// it isn't thread-safe and wraps every call in a global lock, so concurrent callers can't actually
/// corrupt anything, but without an explicit limit here, committing several PDF-backed series at once
/// (each its own Hangfire job) would still queue an unbounded number of worker threads blocked on that
/// one native lock — burning memory and DB connections for nothing, and potentially starving other
/// background jobs of workers. <see cref="ExtractPagesAsync"/> gates on a semaphore instead, sized from
/// <c>Import:MaxConcurrentPdfConversions</c> (default 1, matching PDFium's own "one at a time"
/// guidance — raise it only if you've verified your deployment tolerates more).</summary>
public sealed class PdfPageExtractor
{
    private readonly SemaphoreSlim _gate;
    private readonly RenderOptions _renderOptions;

    public PdfPageExtractor(IConfiguration config)
    {
        var max = config.GetValue<int?>("Import:MaxConcurrentPdfConversions") ?? 1;
        _gate = new SemaphoreSlim(Math.Max(1, max));

        // Render each page at a bounded width (aspect-preserved) rather than PDFium's native/DPI size.
        // An unbounded render of a large-media-box page allocates a bitmap big enough to fail outright
        // ("Unable to allocate pixels for the bitmap"), especially under aggressive GC memory settings;
        // capping the longest rendered edge keeps every page's bitmap allocation predictable while
        // staying at a comfortable on-screen reading resolution. Tunable via Import:PdfMaxRenderWidth.
        var maxWidth = Math.Max(400, config.GetValue<int?>("Import:PdfMaxRenderWidth") ?? 1800);
        _renderOptions = new RenderOptions(Width: maxWidth, WithAspectRatio: true);
    }

    /// <summary>Page count only — cheap (reads the PDF's page tree, doesn't rasterize) — not gated by
    /// the concurrency limit below; PDFium's own internal lock covers this briefly regardless.</summary>
    public int CountPages(string pdfPath)
    {
        using var stream = File.OpenRead(pdfPath);
        return Conversion.GetPageCount(stream);
    }

    /// <summary>Rasterizes every page of <paramref name="pdfPath"/> to a zero-padded JPEG in
    /// <paramref name="destDir"/>, in page order. Returns the written file paths, in order. PDFium
    /// rendering is CPU-bound and blocking, so this runs on a thread-pool thread.
    /// <paramref name="pageProgress"/>, if given, is reported (1-based pages done) after each page —
    /// this is the slow part of a PDF import, easily minutes for a full volume, so callers doing
    /// anything interactive should report it onward to the user. Waits for a free concurrency-limit
    /// slot before starting if another conversion is already running (see the class doc comment).</summary>
    public async Task<List<string>> ExtractPagesAsync(
        string pdfPath, string destDir, IProgress<int>? pageProgress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(destDir);

                using var stream = File.OpenRead(pdfPath);
                // leaveOpen: true — GetPageCount defaults to closing the stream when done, which would
                // leave nothing for the SaveJpeg calls below to read from.
                var pageCount = Conversion.GetPageCount(stream, leaveOpen: true);
                var pad = Math.Max(3, pageCount.ToString().Length);

                var results = new List<string>(pageCount);
                for (var i = 0; i < pageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(destDir, $"{(i + 1).ToString().PadLeft(pad, '0')}.jpg");
                    Conversion.SaveJpeg(dest, stream, i, leaveOpen: true, options: _renderOptions);
                    results.Add(dest);
                    pageProgress?.Report(i + 1);
                }

                return results;
            }, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
