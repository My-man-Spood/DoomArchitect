namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// A DECORATE/ZScript `$argN`-declared Thing-editor argument: just whether
/// it's used and its title. UDB's own real <c>ArgumentInfo</c> (the one
/// actually used for its actor arguments too) is much richer - an argument
/// type/enum list, default value, and rendering hints (a helper
/// circle/rectangle drawn in the 3D/2D view with range-gradient colors) for
/// its property-argument-editing UI. This project has no Thing-argument-
/// editing UI at all yet (confirmed: the equivalent linedef/sector action
/// arguments are parsed and tested but not wired into any editor either),
/// so building that richness now would have nothing to attach to - tracked
/// in TODO/TODO.md as a deferred sub-piece of the ZScript/DECORATE actor
/// discovery port, not silently dropped.
/// </summary>
public sealed record ActorArgumentInfo(bool Used, string Title);
