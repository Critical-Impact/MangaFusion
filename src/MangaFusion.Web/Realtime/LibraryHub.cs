using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MangaFusion.Web.Realtime;

/// <summary>Broadcasts library/download events. The shared library means everyone gets updates.</summary>
[Authorize]
public sealed class LibraryHub : Hub;
