// Contracts keeps its own source-neutral copy of MediaKind (as it does for ContentRating and
// PublicationStatus) so it needn't depend on Domain. Both namespaces are in scope throughout this
// project, so pin the bare name to the domain enum — the one that matches what's persisted — and
// let the few places that talk to a source qualify the Contracts one explicitly.
global using MediaKind = MangaFusion.Domain.Library.MediaKind;
