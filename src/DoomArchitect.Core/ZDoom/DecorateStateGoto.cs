namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses a DECORATE `goto Target` / `goto Class::Target+Offset` directive -
/// ported verbatim from UDB's real <c>DecorateStateGoto</c> (a hand-rolled
/// character scanner over the whole line, since DECORATE bizarrely allows
/// quotes here; no dependencies beyond <see cref="ZDTextParser.ReadLine"/>
/// and the owning <see cref="ActorStructure"/>).
/// </summary>
internal sealed class DecorateStateGoto : StateGoto
{
    public DecorateStateGoto(ActorStructure actor, ZDTextParser parser)
    {
        var firstTarget = "";
        var secondTarget = "";
        var commentReached = false;
        var offsetReached = false;
        var offsetStr = "";
        var i = 0;

        var line = parser.ReadLine() ?? "";

        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;

        while (i < line.Length && line[i] != ':')
        {
            if (line[i] == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'))
            {
                commentReached = true;
                break;
            }

            if (line[i] == ' ' || line[i] == '\t') break;

            if (line[i] == '+')
            {
                i++;
                offsetReached = true;
                break;
            }

            if (line[i] != '"') firstTarget += line[i];
            i++;
        }

        if (!commentReached && !offsetReached)
        {
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;

            while (i < line.Length)
            {
                if (line[i] == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'))
                {
                    commentReached = true;
                    break;
                }

                if (line[i] == ' ' || line[i] == '\t') break;

                if (line[i] == '+')
                {
                    i++;
                    offsetReached = true;
                    break;
                }

                if (line[i] != '"' && line[i] != ':') secondTarget += line[i];
                i++;
            }
        }

        if (!offsetReached)
        {
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;

            if (i < line.Length && line[i] == '+')
            {
                i++;
                offsetReached = true;
            }
        }

        if (offsetReached)
        {
            while (i < line.Length)
            {
                if (line[i] == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'))
                {
                    // commentReached from here on is never read again, matching UDB's own dead assignment
                    break;
                }

                if (line[i] == ' ' || line[i] == '\t') break;

                if (line[i] != '"' && line[i] != ':') offsetStr += line[i];
                i++;
            }
        }

        // A first target, optionally a second target, optionally a sprite offset.
        if (string.IsNullOrEmpty(secondTarget))
        {
            ClassName = actor.ClassName;
            StateName = firstTarget.ToLowerInvariant().Trim();
        }
        else
        {
            ClassName = firstTarget.ToLowerInvariant().Trim();
            StateName = secondTarget.ToLowerInvariant().Trim();
        }

        if (offsetStr.Length > 0 && int.TryParse(offsetStr, out var offset)) SpriteOffset = offset;

        if (ClassName == "super" && actor.BaseClass != null) ClassName = actor.BaseClass.ClassName;
    }
}
