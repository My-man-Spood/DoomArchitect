using System.Globalization;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// One parsed `actor Name : Parent replaces Other 5000 { ... }` block -
/// ported from UDB's real <c>DecorateActorStructure</c>. Real adaptations:
/// no editor-number range validation against a map format (this project is
/// UDMF-only, with no format-specific bounds concept to check against - a
/// bogus number just flows through as data, same as any other malformed
/// input this scan tolerates rather than crashes on); the "inherit from a
/// static game-config actor" resolution reads
/// <see cref="IGameConfiguration.GetThingTypes"/> instead of UDB's ambient
/// <c>General.Map.Config</c>; and `var user_*` fields are parsed just
/// enough to skip past them correctly (no `uservars` dictionary - see
/// <see cref="ActorStructure"/>'s own remark on why).
/// </summary>
public sealed class DecorateActorStructure : ActorStructure
{
    public DecorateActorStructure(DecorateParser parser, DecorateCategoryInfo? categoryInfo, IGameConfiguration? gameConfiguration)
    {
        CategoryInfo = categoryInfo;

        parser.SkipWhitespace(true);
        ClassName = parser.StripTokenQuotes(parser.ReadToken(ActorClassSpecialTokens) ?? "");

        if (string.IsNullOrEmpty(ClassName))
        {
            parser.ReportError("Expected actor class name");
            return;
        }

        if (parser.GetArchivedActorByName(ClassName) != null)
        {
            parser.ReportError($"Actor \"{ClassName}\" is double-defined");
            return;
        }

        if (!ParseHeader(parser)) return;
        if (!ParseBody(parser)) return;

        ParseCustomArguments();
        InheritFromStaticGameConfiguration(gameConfiguration);
    }

    /// <summary>Everything before the opening `{` - inheritance, replaces, the editor number.</summary>
    private bool ParseHeader(DecorateParser parser)
    {
        while (parser.SkipWhitespace(true))
        {
            var token = parser.ReadToken();
            if (string.IsNullOrEmpty(token))
            {
                parser.ReportError("Unexpected end of structure");
                return false;
            }

            token = token.ToLowerInvariant();
            switch (token)
            {
                case ":":
                    parser.SkipWhitespace(true);
                    InheritsClass = parser.StripTokenQuotes(parser.ReadToken());
                    if (string.IsNullOrEmpty(InheritsClass))
                    {
                        parser.ReportError("Expected class name to inherit from");
                        return false;
                    }
                    BaseClass = parser.GetArchivedActorByName(InheritsClass);
                    break;

                case "replaces":
                    parser.SkipWhitespace(true);
                    ReplacesClass = parser.StripTokenQuotes(parser.ReadToken());
                    if (string.IsNullOrEmpty(ReplacesClass))
                    {
                        parser.ReportError("Expected class name to replace");
                        return false;
                    }
                    break;

                case "native":
                    break;

                case "{":
                    return true;

                case "-":
                    // A negative doomednum, tokenized as "-" then the number - means "no editor number", so just consume and discard it.
                    parser.ReadToken();
                    break;

                default:
                    if (token.StartsWith("$"))
                    {
                        Properties[token] = new List<string> { parser.SkipWhitespace(false) ? parser.ReadLine() ?? "" : "" };
                        continue;
                    }

                    if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var doomEdNum))
                    {
                        parser.ReportError($"Expected editor number or start of actor scope while parsing \"{ClassName}\"");
                        return false;
                    }

                    DoomEdNum = doomEdNum;
                    break;
            }
        }

        parser.ReportError("Unexpected end of structure");
        return false;
    }

    /// <summary>Everything inside the actor's `{ ... }` scope.</summary>
    private bool ParseBody(DecorateParser parser)
    {
        var previousToken = "";

        while (parser.SkipWhitespace(true))
        {
            var token = parser.ReadToken().ToLowerInvariant();

            switch (token)
            {
                case "+":
                case "-":
                {
                    var flagValue = token == "+";
                    parser.SkipWhitespace(true);
                    var flagName = parser.ReadToken();
                    if (string.IsNullOrEmpty(flagName))
                    {
                        parser.ReportError("Expected flag name");
                        return false;
                    }
                    Flags[flagName.ToLowerInvariant()] = flagValue;
                    break;
                }

                case "action":
                case "native":
                    while (parser.SkipWhitespace(true))
                    {
                        var t = parser.ReadToken();
                        if (string.IsNullOrEmpty(t) || t == ";") break;
                    }
                    break;

                case "skip_super":
                    SkipSuper = true;
                    break;

                case "states":
                    if (!SkipCastTypes(parser)) return false;
                    if (!parser.NextTokenIs("{")) return false;
                    if (!ParseStatesBlock(parser)) return false;
                    break;

                case "var":
                    if (!SkipUserVariableDeclaration(parser)) return false;
                    break;

                case "}":
                    return true;

                case "monster":
                    Flags["shootable"] = true;
                    Flags["countkill"] = true;
                    Flags["solid"] = true;
                    Flags["canpushwalls"] = true;
                    Flags["canusewalls"] = true;
                    Flags["activatemcross"] = true;
                    Flags["canpass"] = true;
                    Flags["ismonster"] = true;
                    break;

                case "projectile":
                    Flags["noblockmap"] = true;
                    Flags["nogravity"] = true;
                    Flags["dropoff"] = true;
                    Flags["missile"] = true;
                    Flags["activateimpact"] = true;
                    Flags["activatepcross"] = true;
                    Flags["noteleport"] = true;
                    break;

                case "clearflags":
                    Flags.Clear();
                    break;

                case "game":
                {
                    var games = new List<string>();
                    while (parser.SkipWhitespace(false))
                    {
                        var v = parser.ReadToken();
                        if (string.IsNullOrEmpty(v))
                        {
                            parser.ReportError("Expected \"Game\" property value");
                            return false;
                        }
                        if (v == "\n") break;
                        if (v == "}") return true;
                        if (v != ",") games.Add(v.ToLowerInvariant());
                    }
                    Properties[token] = games;
                    break;
                }

                default:
                    if (token.StartsWith("$"))
                    {
                        Properties[token] = new List<string> { parser.SkipWhitespace(false) ? parser.ReadLine() ?? "" : "" };
                    }
                    else if (!ParsePropertyValues(parser, token))
                    {
                        return true; // hit an unexpected "}" mid-value-list - matches UDB's own tolerant bail-out
                    }
                    break;
            }

            previousToken = token;
        }

        parser.ReportError("Unexpected end of structure");
        return false;
    }

    /// <summary>A plain `name value1 value2 ...` property line - the common case for anything not otherwise special-cased above. Returns false if it hit "}" (caller should treat the actor as done, not an error).</summary>
    private bool ParsePropertyValues(DecorateParser parser, string token)
    {
        var values = new List<string>();
        while (parser.SkipWhitespace(false))
        {
            var v = parser.ReadToken();
            if (string.IsNullOrEmpty(v))
            {
                parser.ReportError("Unexpected end of structure");
                return true;
            }
            if (v == "\n") break;
            if (v == "}") return false;
            if (v != ",") values.Add(v);
        }

        if (token == "scale")
        {
            Properties["xscale"] = values;
            Properties["yscale"] = values;
        }
        else
        {
            Properties[token] = values;
        }

        return true;
    }

    private bool ParseStatesBlock(DecorateParser parser)
    {
        var previousToken = "";
        while (parser.SkipWhitespace(true))
        {
            var stateToken = parser.ReadToken();
            if (string.IsNullOrEmpty(stateToken))
            {
                parser.ReportError("Unexpected end of structure");
                return false;
            }

            if (stateToken == "}") return true;

            if (stateToken == ":")
            {
                if (string.IsNullOrEmpty(previousToken))
                {
                    parser.ReportError("Expected actor state name");
                    return false;
                }

                var state = new DecorateStateStructure(this, parser);
                if (parser.HasError) return false;
                States[previousToken.ToLowerInvariant()] = state;
            }
            else
            {
                previousToken = stateToken;
            }
        }

        parser.ReportError("Unexpected end of structure");
        return false;
    }

    /// <summary>Reads a `var TYPE user_name;` or `var TYPE user_name[N];` declaration far enough to correctly skip past it - see the class doc comment on why nothing is retained from it.</summary>
    private bool SkipUserVariableDeclaration(DecorateParser parser)
    {
        parser.SkipWhitespace(true);
        parser.ReadToken(); // type

        parser.SkipWhitespace(true);
        var name = parser.ReadToken();
        if (string.IsNullOrEmpty(name))
        {
            parser.ReportError("Expected User Variable name");
            return false;
        }

        parser.SkipWhitespace(true);
        var next = parser.ReadToken();
        if (next == "[")
        {
            var arrayLength = -1;
            if (!parser.ReadSignedInt(ref arrayLength))
            {
                parser.ReportError("Expected User Array length");
                return false;
            }
            if (!parser.NextTokenIs("]") || !parser.NextTokenIs(";")) return false;
        }
        else if (next != ";")
        {
            parser.ReportError($"Expected \";\", but got \"{next}\"");
            return false;
        }

        return true;
    }

    /// <summary>Skips a state-block cast type list if one exists, e.g. `States(Actor, Item)` - https://zdoom.org/wiki/Converting_DECORATE_code_to_ZScript#Cast_types.</summary>
    private static bool SkipCastTypes(DecorateParser parser)
    {
        string[] allowedCasts = { "actor", "overlay", "weapon", "item" };

        if (!parser.NextTokenIs("(", false)) return true; // no cast type at all

        while (parser.SkipWhitespace(true))
        {
            var token = parser.ReadToken().ToLowerInvariant();
            if (!allowedCasts.Contains(token))
            {
                parser.ReportError($"Unexpected cast type \"{token}\", expected one of: {string.Join(", ", allowedCasts)}");
                return false;
            }

            parser.SkipWhitespace(true);
            token = parser.ReadToken();

            if (token == ")") return true;
            if (token == ",") continue;

            parser.ReportError($"Expected \",\", or \")\", got {token}");
            return false;
        }

        return false;
    }

    /// <summary>
    /// When this actor extends a real static engine actor (rather than
    /// another parsed one) - inherit its sprite/flags/radius/height/args
    /// for whatever this actor doesn't itself declare, matching UDB's own
    /// real <c>DecorateActorStructure</c> fallback exactly.
    /// </summary>
    private void InheritFromStaticGameConfiguration(IGameConfiguration? gameConfiguration)
    {
        if (gameConfiguration == null) return;
        if (InheritsClass.Equals("actor", StringComparison.OrdinalIgnoreCase) || DoomEdNum <= -1) return;

        var inheritClassCheck = InheritsClass.ToLowerInvariant();
        var match = gameConfiguration.GetThingTypes().FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.ClassName) && t.ClassName.Equals(inheritClassCheck, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            if (BaseClass == null)
            {
                // parser.LogWarning would go here once a real diagnostics
                // surface exists to show it (tracked in TODO/TODO.md) - silently
                // skipping this inheritance step is the correct behavior
                // either way, just without the notification for now.
            }
            return;
        }

        // Allow the internal: prefix here too - a DECORATE actor can
        // legitimately inherit MapSpot/light/other editor-only markers.
        if (States.Count == 0 && !string.IsNullOrEmpty(match.SpriteName))
        {
            var spriteName = InternalSprites.IsInternalName(match.SpriteName) ? match.SpriteName : match.SpriteName[..Math.Min(5, match.SpriteName.Length)];
            States["spawn"] = new StateStructure(spriteName);
        }

        if (BaseClass == null)
        {
            if (match.Hangs && !Flags.ContainsKey("spawnceiling")) Flags["spawnceiling"] = true;
            if (!Properties.ContainsKey("height")) Properties["height"] = new List<string> { match.Height.ToString(CultureInfo.InvariantCulture) };
            if (!Properties.ContainsKey("radius")) Properties["radius"] = new List<string> { match.Radius.ToString(CultureInfo.InvariantCulture) };
        }
    }
}
