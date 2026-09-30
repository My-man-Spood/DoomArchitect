namespace DoomArchitect.Core.Configuration;

/// <summary>
/// One named Test Map source-port profile for a given
/// <see cref="GameConfigurationKind"/> - a game configuration can have
/// several (e.g. "GZDoom" and "GZDoom (software)"), with one marked active
/// in <see cref="AppSettings"/>. <see cref="CustomParameters"/> is only used
/// when <see cref="UseCustomParameters"/> is true; otherwise Test Map falls
/// back to the game configuration's own <see cref="IGameConfiguration.TestParameters"/>
/// template.
/// </summary>
public sealed record TestEngine(string Name, string ExecutablePath, bool UseCustomParameters, string CustomParameters);
