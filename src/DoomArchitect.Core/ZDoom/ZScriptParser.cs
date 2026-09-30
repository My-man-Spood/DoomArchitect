using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Textures;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses ZScript source into a set of <see cref="ActorStructure"/>s -
/// ported from UDB's real <c>ZScriptParser</c>. Same two-phase shape as the
/// original: <see cref="Parse"/> only records each `class`/`mixin class`
/// declaration's header plus where its body starts
/// (<see cref="ZScriptClassStructure"/>, since ZScript allows inheriting a
/// class before it's been declared yet); <see cref="CompleteParsing"/>
/// (UDB's own real name is the reserved word `Finalize` - renamed here,
/// same behavior, to avoid C#'s special treatment of that exact method
/// name) walks every recorded class, parses its actual body
/// (<c>ZScriptActorStructure</c>, Phase 2's next file), then wires up
/// inheritance/mixins/extensions once every class in the scan is known.
/// Real adaptations: no `DataLocation` cross-archive check for `extend
/// class` (a single <see cref="ZScriptParser"/> instance only ever parses
/// one resource's own `#include` closure in this project's phased design,
/// so there's nothing else to compare against); no `uservars` tracking
/// (see <see cref="ActorStructure"/>'s own remark); the game-config
/// inheritance step reads <see cref="IGameConfiguration.GetThingTypes"/>
/// instead of an ambient singleton, via <see cref="GameConfiguration"/>.
/// </summary>
public sealed class ZScriptParser : ZDTextParser
{
    /// <summary>
    /// One recorded `class`/`mixin class` declaration - parsing its actual
    /// body is deferred to <see cref="Process"/> (called from
    /// <see cref="CompleteParsing"/>), since ZScript allows a class to
    /// inherit one not yet declared at the point it's read.
    /// </summary>
    public sealed class ZScriptClassStructure
    {
        public string ClassName { get; internal set; } = string.Empty;
        public string? ReplacementName { get; internal set; }
        public string? ParentName { get; internal set; }
        public ZScriptActorStructure? Actor { get; internal set; }
        internal DecorateCategoryInfo? Region;
        public bool IsMixin { get; internal set; }
        public bool IsExtension { get; internal set; }
        public List<ZScriptClassStructure> Extensions { get; } = new();
        public bool IsFinal { get; internal set; }
        public List<string> PermittedInheritedClassNames { get; internal set; }

        public ZScriptParser Parser { get; }
        private readonly Stream _stream;
        internal long Position;
        private readonly BinaryReader _reader;
        private readonly string _sourceName;

        internal ZScriptClassStructure(
            ZScriptParser parser, string className, DecorateCategoryInfo? region,
            string? replacesName = null, string? parentName = null,
            bool isMixin = false, bool isExtension = false, bool isFinal = false,
            List<string>? permittedInheritedClassNames = null)
        {
            Parser = parser;
            _stream = parser.DataStream!;
            Position = parser.DataStream!.Position;
            _reader = parser.DataReader!;
            _sourceName = parser.SourceName;

            ClassName = className;
            ReplacementName = replacesName;
            ParentName = parentName;
            Region = region;
            IsMixin = isMixin;
            IsExtension = isExtension;
            IsFinal = isFinal;
            PermittedInheritedClassNames = permittedInheritedClassNames != null ? new List<string>(permittedInheritedClassNames) : new List<string>();
        }

        internal void RestoreStreamData()
        {
            Parser.RestoreParseTarget(_stream, _reader, _sourceName);
            _stream.Position = Position;
            Parser.PrevStreamPosition = -1;
        }

        internal bool Process()
        {
            RestoreStreamData();

            var isActor = false;
            ZScriptClassStructure? current = this;
            while (current != null)
            {
                if (current.ClassName.Equals("actor", StringComparison.OrdinalIgnoreCase))
                {
                    isActor = true;
                    break;
                }

                if (current.ParentName != null)
                {
                    var parentName = current.ParentName.ToLowerInvariant();
                    if (parentName == current.ClassName.ToLowerInvariant())
                    {
                        Parser.ReportError($"Fatal: Class \"{current.ClassName}\" is trying to inherit from itself.");
                        return false;
                    }

                    var childName = current.ClassName;
                    Parser._allClasses.TryGetValue(parentName, out current);
                    if (current == null)
                    {
                        Parser.ReportError($"Fatal: Class \"{childName}\" is trying to inherit from \"{parentName}\" which does not exist.");
                        return false;
                    }

                    if (current.IsFinal)
                    {
                        Parser.ReportError($"Fatal: Class \"{childName}\" is trying to inherit from \"{parentName}\" which is final");
                        return false;
                    }

                    if (current.PermittedInheritedClassNames.Count > 0 && !current.PermittedInheritedClassNames.Contains(childName.ToLowerInvariant()))
                    {
                        Parser.ReportError($"Fatal: Class \"{childName}\" is not allowed to inherit from \"{parentName}\"");
                        return false;
                    }
                }
                else
                {
                    current = null;
                }
            }

            if (isActor || IsMixin || IsExtension)
            {
                Actor = new ZScriptActorStructure(Parser, Region, ClassName, ReplacementName, ParentName);
                if (Parser.HasError)
                {
                    Actor = null;
                    return false;
                }

                if (!IsExtension)
                {
                    var key = Actor.ClassName.ToLowerInvariant();
                    Parser._archivedActors[key] = Actor;
                    Parser._realArchivedActors[key] = Actor;
                    if (Actor.CheckActorSupported(Parser.GameConfiguration?.DecorateGames ?? "")) Parser._actors[key] = Actor;

                    if (Actor.ReplacesClass != null)
                    {
                        var replaceKey = Actor.ReplacesClass.ToLowerInvariant();
                        if (Parser.GetArchivedActorByName(Actor.ReplacesClass, false) != null)
                            Parser._archivedActors[replaceKey] = Actor;
                        else
                            Parser.LogWarning($"Unable to find \"{Actor.ReplacesClass}\" class to replace, while parsing \"{Actor.ClassName}\"");

                        if (Actor.CheckActorSupported(Parser.GameConfiguration?.DecorateGames ?? "") && Parser.GetActorByName(Actor.ReplacesClass) != null)
                            Parser._actors[replaceKey] = Actor;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>Resolves a literal `#include` filename to that file's raw bytes, or null if it can't be found.</summary>
    public Func<string, byte[]?>? OnInclude;

    public IGameConfiguration? GameConfiguration { get; set; }

    public bool NoWarnings;
    public List<string> Warnings { get; } = new();
    private void LogWarning(string message) { if (!NoWarnings) Warnings.Add(message); }

    private Dictionary<string, ActorStructure> _actors = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ActorStructure> _archivedActors = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ActorStructure> _realArchivedActors = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ZScriptClassStructure> _allClasses = new();
    private List<ZScriptClassStructure> _allClassesList = new();
    private Dictionary<string, ZScriptClassStructure> _mixinClasses = new();
    private List<ZScriptClassStructure> _mixinClassesList = new();
    private readonly HashSet<string> _parsedLumps = new(StringComparer.OrdinalIgnoreCase);

    internal ZScriptTokenizer? Tokenizer;

    public IEnumerable<ActorStructure> Actors => _actors.Values;
    public ICollection<ActorStructure> AllActors => _archivedActors.Values;

    /// <summary>Same as <see cref="Actors"/>, keyed by lowercase class name - for the Phase 4 merge into <see cref="Configuration.ThingTypeInfo"/>.</summary>
    public IReadOnlyDictionary<string, ActorStructure> ActorsByClass => _actors;

    /// <summary>Same as <see cref="AllActors"/>, keyed by lowercase class name.</summary>
    public IReadOnlyDictionary<string, ActorStructure> AllActorsByClass => _archivedActors;

    /// <summary>
    /// Every class name recorded by <see cref="Parse"/> (lowercase),
    /// regardless of whether it's an actor - available right after
    /// <see cref="Parse"/>, no <see cref="CompleteParsing"/> needed. Exists
    /// for callers that only need to know "is a class named X declared
    /// here at all" (e.g. <see cref="RequiredArchiveDetector"/>'s content
    /// fingerprint check) without risking a fingerprint failure over an
    /// unrelated class elsewhere in the same file failing full inheritance
    /// resolution.
    /// </summary>
    public IReadOnlyCollection<string> DeclaredClassNames => _allClasses.Keys;

    public ZScriptParser()
    {
        ClearActors();
    }

    private void ClearActors()
    {
        _actors = new Dictionary<string, ActorStructure>(StringComparer.OrdinalIgnoreCase);
        _archivedActors = new Dictionary<string, ActorStructure>(StringComparer.OrdinalIgnoreCase);
        _realArchivedActors = new Dictionary<string, ActorStructure>(StringComparer.OrdinalIgnoreCase);
        _allClasses = new Dictionary<string, ZScriptClassStructure>();
        _allClassesList = new List<ZScriptClassStructure>();
        _mixinClasses = new Dictionary<string, ZScriptClassStructure>();
        _mixinClassesList = new List<ZScriptClassStructure>();
    }

    private bool ParseInclude(string filename)
    {
        var savedStream = DataStream!;
        var savedReader = DataReader!;
        var savedSource = SourceName;
        var savedTokenizer = Tokenizer;

        if (string.IsNullOrEmpty(filename))
        {
            ReportError("Expected file name to include");
            return false;
        }

        if (Path.IsPathRooted(filename))
        {
            ReportError("Absolute include paths are not supported by ZDoom");
            return false;
        }

        // GZDoom 4.8+ supports relative includes - resolve against the including file's own path.
        if (filename.StartsWith("../") || filename.StartsWith("./"))
        {
            var pathTokens = savedSource.Split('\\', '/').ToList();
            pathTokens.RemoveAt(pathTokens.Count - 1); // drop the including file's own name
            pathTokens.AddRange(filename.Split('\\', '/'));
            pathTokens.RemoveAll(t => t == ".");

            for (var i = 0; i < pathTokens.Count; i++)
            {
                if (pathTokens[i] != "..") continue;
                if (i == 0)
                {
                    ReportError("Relative path escaping archive");
                    return false;
                }

                pathTokens.RemoveAt(i);
                pathTokens.RemoveAt(i - 1);
                i -= 2;
            }

            filename = string.Join("/", pathTokens);
        }

        if (filename.Contains('\\'))
        {
            ReportError("Only forward slashes are supported by ZDoom");
            return false;
        }

        if (!_parsedLumps.Add(filename))
        {
            ReportError($"Already parsed \"{filename}\". Check your include directives");
            return false;
        }

        var included = OnInclude?.Invoke(filename);
        if (included != null && !Parse(included, filename)) return false;

        RestoreParseTarget(savedStream, savedReader, savedSource);
        Tokenizer = savedTokenizer;
        return true;
    }

    /// <summary>Reads a raw token list up to (not including) an un-nested `;`/`,`, or a matching close-paren if <paramref name="betweenParen"/>.</summary>
    internal List<ZScriptToken>? ParseExpression(bool betweenParen = false)
    {
        var tokens = new List<ZScriptToken>();
        var nesting = 0;

        while (true)
        {
            var checkpoint = DataStream!.Position;
            var token = Tokenizer!.ReadToken();
            if (token == null)
            {
                ReportError("Expected a token");
                return null;
            }

            if (!string.IsNullOrEmpty(token.WarningMessage)) LogWarning(token.WarningMessage);

            if ((token.Type == ZScriptTokenType.Semicolon || token.Type == ZScriptTokenType.Comma) && nesting == 0 && !betweenParen)
            {
                DataStream.Position = checkpoint;
                return tokens;
            }

            if (token.Type == ZScriptTokenType.OpenParen)
            {
                nesting++;
            }
            else if (token.Type == ZScriptTokenType.CloseParen)
            {
                nesting--;
                if (nesting < 0)
                {
                    DataStream.Position = checkpoint;
                    return tokens;
                }
            }

            tokens.Add(token);
        }
    }

    /// <summary>Skips a `{ ... }` block (braces only - no `;`-terminated single-statement form).</summary>
    internal bool SkipBlock()
    {
        var token = Tokenizer!.ExpectToken(ZScriptTokenType.OpenCurly);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected {{, got {(object?)token ?? "<null>"}");
            return false;
        }

        var nesting = 1;
        while (nesting > 0)
        {
            token = Tokenizer.ReadToken(true);
            if (token == null)
            {
                ReportError("Expected a token");
                return false;
            }

            if (token.Type != ZScriptTokenType.Invalid) continue;

            if (token.Value == "{") nesting++;
            else if (token.Value == "}")
            {
                nesting--;
                if (nesting < 0)
                {
                    ReportError("Closing parenthesis without an opening one");
                    return false;
                }
            }
        }

        // A stray trailing ";" after a class body is technically invalid but tolerated - real gzdoom.pk3 has it.
        var checkpoint = DataStream!.Position;
        var tail = Tokenizer.ReadToken();
        if (tail == null || tail.Type != ZScriptTokenType.Semicolon) DataStream.Position = checkpoint;

        return true;
    }

    /// <summary>Skips a class body - either `{ ... }` or a bare `;` (a forward declaration).</summary>
    internal bool SkipClassBlock()
    {
        var token = Tokenizer!.ExpectToken(ZScriptTokenType.OpenCurly, ZScriptTokenType.Semicolon);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected {{ or ;, got {(object?)token ?? "<null>"}");
            return false;
        }

        var isSemicolon = token.Type == ZScriptTokenType.Semicolon;
        var nesting = 1;
        while (nesting > 0)
        {
            token = Tokenizer.ReadToken(true);
            if (token == null)
            {
                if (isSemicolon)
                {
                    nesting--;
                    break;
                }

                ReportError("Expected a token");
                return false;
            }

            if (token.Type != ZScriptTokenType.Invalid) continue;

            if (token.Value == "{") nesting++;
            else if (token.Value == "}")
            {
                nesting--;
                if (nesting < 0)
                {
                    ReportError("Closing parenthesis without an opening one");
                    return false;
                }
            }
        }

        var checkpoint = DataStream!.Position;
        var tail = Tokenizer.ReadToken();
        if (tail == null || tail.Type != ZScriptTokenType.Semicolon) DataStream.Position = checkpoint;

        return true;
    }

    /// <summary>A `{ ... }` block, or (if <paramref name="allowSingle"/>) a single `expr;` statement - returns the raw token list either way.</summary>
    internal List<ZScriptToken>? ParseBlock(bool allowSingle, ZScriptToken? skipRead = null)
    {
        var tokens = new List<ZScriptToken>();
        var checkpoint = DataStream!.Position;
        var token = skipRead ?? Tokenizer!.ReadToken();
        if (token == null)
        {
            ReportError("Expected a code block, got <null>");
            return null;
        }

        if (token.Type != ZScriptTokenType.OpenCurly)
        {
            if (!allowSingle)
            {
                ReportError($"Expected {{, got {token}");
                return null;
            }

            DataStream.Position = checkpoint;
            var expression = ParseExpression();
            if (expression == null) return null;

            token = Tokenizer!.ReadToken();
            if (token == null || token.Type != ZScriptTokenType.Semicolon)
            {
                ReportError($"Expected ;, got {(object?)token ?? "<null>"}");
                return null;
            }

            expression.Add(token);
            return expression;
        }

        var nesting = 1;
        while (nesting > 0)
        {
            token = Tokenizer!.ReadToken();
            if (token == null)
            {
                ReportError("Expected a token");
                return null;
            }

            if (token.Type == ZScriptTokenType.OpenCurly)
            {
                nesting++;
            }
            else if (token.Type == ZScriptTokenType.CloseCurly)
            {
                nesting--;
                if (nesting < 0)
                {
                    ReportError("Closing parenthesis without an opening one");
                    return null;
                }
            }

            tokens.Add(token);
        }

        var tail = Tokenizer!.ReadToken();
        var tailCheckpoint = DataStream!.Position;
        if (tail == null || tail.Type != ZScriptTokenType.Semicolon) DataStream.Position = tailCheckpoint;
        else tokens.Add(tail);

        return tokens;
    }

    internal bool ParseConst()
    {
        Tokenizer!.SkipWhitespace();
        var token = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected const name, got {(object?)token ?? "<null>"}");
            return false;
        }

        Tokenizer.SkipWhitespace();
        token = Tokenizer.ExpectToken(ZScriptTokenType.OpAssign);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected =, got {(object?)token ?? "<null>"}");
            return false;
        }

        Tokenizer.SkipWhitespace();
        if (ParseExpression() == null) return false;

        Tokenizer.SkipWhitespace();
        token = Tokenizer.ExpectToken(ZScriptTokenType.Semicolon);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected ;, got {(object?)token ?? "<null>"}");
            return false;
        }

        return true;
    }

    internal bool ParseEnum()
    {
        Tokenizer!.SkipWhitespace();
        var token = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected enum name, got {(object?)token ?? "<null>"}");
            return false;
        }

        Tokenizer.SkipWhitespace();
        token = Tokenizer.ReadToken();
        if (token == null)
        {
            ReportError("Expected a code block or integer type for enum, got <null>");
            return false;
        }

        if (token.Type == ZScriptTokenType.Colon)
        {
            Tokenizer.SkipWhitespace();
            token = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token is not { IsValid: true })
            {
                ReportError($"Expected an integer type, got {(object?)token ?? "<null>"}");
                return false;
            }

            Tokenizer.SkipWhitespace();
            token = Tokenizer.ReadToken();
        }

        return ParseBlock(false, token) != null;
    }

    internal bool ParseInteger(out int value)
    {
        value = 1;

        var token = Tokenizer!.ExpectToken(ZScriptTokenType.Integer, ZScriptTokenType.OpAdd, ZScriptTokenType.OpSubtract);
        if (token is not { IsValid: true })
        {
            ReportError($"Expected integer, got {(object?)token ?? "<null>"}");
            return false;
        }

        if (token.Type == ZScriptTokenType.OpSubtract) value = -1;

        if (token.Type != ZScriptTokenType.Integer)
        {
            token = Tokenizer.ExpectToken(ZScriptTokenType.Integer);
            if (token is not { IsValid: true })
            {
                ReportError($"Expected integer, got {(object?)token ?? "<null>"}");
                return false;
            }

            value *= token.ValueInt;
            return true;
        }

        value = token.ValueInt;
        return true;
    }

    internal string? ParseDottedIdentifier()
    {
        var name = "";
        while (true)
        {
            var token = Tokenizer!.ExpectToken(ZScriptTokenType.Identifier);
            if (token is not { IsValid: true })
            {
                ReportError($"Expected identifier, got {(object?)token ?? "<null>"}");
                return null;
            }

            if (name.Length > 0) name += '.';
            name += token.Value.ToLowerInvariant();

            var checkpoint = DataStream!.Position;
            token = Tokenizer.ReadToken();
            if (token!.Type != ZScriptTokenType.Dot)
            {
                DataStream.Position = checkpoint;
                break;
            }
        }

        return name;
    }

    /// <summary>Parses `class Name [replaces X] [: Parent] [modifiers...] { ... }` (or `struct`/`extend class`/`mixin class`) far enough to record a <see cref="ZScriptClassStructure"/> - the body itself is skipped and reparsed later by <see cref="ZScriptClassStructure.Process"/>.</summary>
    internal bool ParseClassOrStruct(bool isStruct, bool extend, bool mixin, DecorateCategoryInfo? region)
    {
        Tokenizer!.SkipWhitespace();
        var className = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (className is not { IsValid: true })
        {
            ReportError($"Expected class name, got {(object?)className ?? "<null>"}");
            return false;
        }

        ZScriptToken? replaceName = null, parentName = null, nativeTok = null, scopeTok = null, finalTok = null;
        string[] classScopeModifiers = { "clearscope", "ui", "play" };
        string[] otherModifiers = { "abstract" };
        var permittedInheritedClassNames = new List<string>();

        while (true)
        {
            Tokenizer.SkipWhitespace();
            var token = Tokenizer.ReadToken();
            if (token == null)
            {
                ReportError("Expected a token");
                return false;
            }

            if (token.Type == ZScriptTokenType.Identifier)
            {
                var value = token.Value.ToLowerInvariant();
                if (value == "replaces")
                {
                    if (nativeTok != null) { ReportError("Cannot have replacement after native"); return false; }
                    if (replaceName != null) { ReportError("Cannot have two replacements per class"); return false; }

                    Tokenizer.SkipWhitespace();
                    replaceName = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (replaceName is not { IsValid: true }) { ReportError($"Expected replacement class name, got {(object?)replaceName ?? "<null>"}"); return false; }
                }
                else if (value == "native")
                {
                    if (nativeTok != null) { ReportError("Cannot have two native keywords"); return false; }
                    nativeTok = token;
                }
                else if (value == "final")
                {
                    if (finalTok != null) { ReportError("Cannot have two final keywords"); return false; }
                    finalTok = token;
                }
                else if (value == "sealed")
                {
                    var sealedNames = ParseSealed();
                    if (sealedNames == null) return false;
                    permittedInheritedClassNames = sealedNames;
                }
                else if (Array.IndexOf(classScopeModifiers, value) >= 0)
                {
                    if (scopeTok != null) { ReportError("Cannot have two scope qualifiers"); return false; }
                    scopeTok = token;
                }
                else if (!isStruct && Array.IndexOf(otherModifiers, value) >= 0)
                {
                    // recorded by UDB for nothing this project needs either
                }
                else if (value == "version")
                {
                    if (!SkipParenthesizedSingle(ZScriptTokenType.String)) return false;
                }
                else if (value == "unsafe")
                {
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.OpenParen) is not { IsValid: true }) { ReportError("Expected ("); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.Identifier) is not { IsValid: true }) { ReportError("Expected identifier"); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true }) { ReportError("Expected )"); return false; }
                }
                else if (value == "deprecated")
                {
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.OpenParen) is not { IsValid: true }) { ReportError("Expected ("); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.String) is not { IsValid: true }) { ReportError("Expected string"); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.Comma) is not { IsValid: true }) { ReportError("Expected ,"); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.String) is not { IsValid: true }) { ReportError("Expected string"); return false; }
                    Tokenizer.SkipWhitespace();
                    if (Tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true }) { ReportError("Expected )"); return false; }
                }
                else
                {
                    ReportError($"Unexpected token {token}");
                }
            }
            else if (token.Type == ZScriptTokenType.Colon)
            {
                if (parentName != null) { ReportError("Cannot have two parent classes"); return false; }
                if (replaceName != null || nativeTok != null) { ReportError("Cannot have parent class after replacement class or native keyword"); return false; }

                Tokenizer.SkipWhitespace();
                parentName = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                if (parentName is not { IsValid: true }) { ReportError($"Expected replacement class name, got {(object?)parentName ?? "<null>"}"); return false; }
            }
            else if (token.Type is ZScriptTokenType.Semicolon or ZScriptTokenType.OpenCurly)
            {
                DataStream!.Position--;
                break;
            }
        }

        Tokenizer.SkipWhitespace();
        var bodyPosition = DataStream!.Position;
        if (!SkipClassBlock()) return false;

        if (!isStruct && !extend && !mixin)
        {
            var cls = new ZScriptClassStructure(this, className.Value, region, replaceName?.Value, parentName?.Value, false, false, finalTok != null, permittedInheritedClassNames) { Position = bodyPosition };
            var key = cls.ClassName.ToLowerInvariant();
            if (_allClasses.ContainsKey(key))
            {
                ReportError($"Class {cls.ClassName} is double-defined");
                return false;
            }

            _allClasses.Add(key, cls);
            _allClassesList.Add(cls);
        }
        else if (!isStruct && extend)
        {
            var key = className.Value.ToLowerInvariant();
            if (!_allClasses.ContainsKey(key))
            {
                ReportError($"Trying to extend class {className.Value} before it was defined");
                return false;
            }

            var cls = new ZScriptClassStructure(this, className.Value, region, isExtension: true) { Position = bodyPosition };
            _allClasses[key].Extensions.Add(cls);
        }
        else if (mixin)
        {
            var cls = new ZScriptClassStructure(this, className.Value, region, isMixin: true) { Position = bodyPosition };
            var key = cls.ClassName.ToLowerInvariant();
            if (_mixinClasses.ContainsKey(key))
            {
                ReportError($"Mixin class {cls.ClassName} is double-defined");
                return false;
            }

            _mixinClasses.Add(key, cls);
            _mixinClassesList.Add(cls);
        }

        return true;
    }

    private bool SkipParenthesizedSingle(ZScriptTokenType inner)
    {
        Tokenizer!.SkipWhitespace();
        if (Tokenizer.ExpectToken(ZScriptTokenType.OpenParen) is not { IsValid: true }) { ReportError("Expected ("); return false; }
        Tokenizer.SkipWhitespace();
        if (Tokenizer.ExpectToken(inner) is not { IsValid: true }) { ReportError($"Expected {inner}"); return false; }
        Tokenizer.SkipWhitespace();
        if (Tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true }) { ReportError("Expected )"); return false; }
        return true;
    }

    /// <summary>Parses `sealed(Name, Name, ...)` after the `sealed` class modifier.</summary>
    private List<string>? ParseSealed()
    {
        var names = new List<string>();

        Tokenizer!.SkipWhitespace();
        var token = Tokenizer.ExpectToken(ZScriptTokenType.OpenParen);
        if (token is not { IsValid: true }) { ReportError($"Expected (, got {(object?)token ?? "<null>"}"); return null; }

        while (true)
        {
            Tokenizer.SkipWhitespace();
            token = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token is not { IsValid: true }) { ReportError($"Expected class name, got {(object?)token ?? "<null>"}"); return null; }

            names.Add(token.Value.ToLowerInvariant());

            Tokenizer.SkipWhitespace();
            token = Tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.CloseParen);
            if (token is not { IsValid: true }) { ReportError($"Expected , or ), got {(object?)token ?? "<null>"}"); return null; }

            if (token.Type == ZScriptTokenType.CloseParen) break;
        }

        return names;
    }

    /// <summary>Parses <paramref name="data"/> (one ZScript file's raw bytes) - top-level declarations only (class/struct/const/enum headers, `#include`/`#region`). `#include`s are parsed after the rest of the file, matching GZDoom's real order.</summary>
    public bool Parse(byte[] data, string sourceName)
    {
        if (!base.Parse(new MemoryStream(data), sourceName)) return false;

        var includes = new List<string>();
        PrevStreamPosition = -1;
        Tokenizer = new ZScriptTokenizer(DataReader!);

        var regions = new List<DecorateCategoryInfo>();

        while (true)
        {
            var token = Tokenizer.ExpectToken(
                ZScriptTokenType.Identifier, ZScriptTokenType.Whitespace, ZScriptTokenType.Newline,
                ZScriptTokenType.BlockComment, ZScriptTokenType.LineComment, ZScriptTokenType.Preprocessor);

            if (token == null) break; // EOF

            if (!token.IsValid)
            {
                ReportError($"Expected preprocessor statement, const, enum or class declaraction, got {token}");
                return false;
            }

            if (token.Type == ZScriptTokenType.LineComment && token.Value.Trim().Equals("$gzdb_skip", StringComparison.OrdinalIgnoreCase)) break;

            switch (token.Type)
            {
                case ZScriptTokenType.Whitespace:
                case ZScriptTokenType.Newline:
                case ZScriptTokenType.BlockComment:
                    break;

                case ZScriptTokenType.LineComment:
                {
                    var comment = token.Value.TrimStart();
                    if (comment.Length > 0 && comment[0] == '$' && regions.Count > 0)
                        ZScriptActorStructure.ParseGZDBComment(regions[^1].Properties, comment);
                    break;
                }

                case ZScriptTokenType.Preprocessor:
                {
                    Tokenizer.SkipWhitespace();
                    var directive = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (directive is not { IsValid: true })
                    {
                        ReportError($"Expected preprocessor directive, got {(object?)directive ?? "<null>"}");
                        return false;
                    }

                    switch (directive.Value.ToLowerInvariant())
                    {
                        case "include":
                            Tokenizer.SkipWhitespace();
                            var includeName = Tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.String, ZScriptTokenType.Name);
                            if (includeName is not { IsValid: true })
                            {
                                ReportError($"Cannot include: expected a string value, got {(object?)includeName ?? "<null>"}");
                                return false;
                            }
                            includes.Add(includeName.Value); // GZDoom parses includes after the rest of the file - deferred, not immediate
                            break;

                        case "region":
                        {
                            var regionName = "";
                            while (true)
                            {
                                token = Tokenizer.ReadToken();
                                if (token == null || token.Type == ZScriptTokenType.Newline) break;
                                regionName += token.Value;
                            }

                            var parts = regionName.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                            var region = new DecorateCategoryInfo();
                            if (regions.Count > 0)
                            {
                                region.Category.AddRange(regions[^1].Category);
                                foreach (var (k, v) in regions[^1].Properties) region.Properties[k] = v;
                            }
                            region.Category.AddRange(parts);
                            regions.Add(region);
                            break;
                        }

                        case "endregion":
                            if (regions.Count > 0) regions.RemoveAt(regions.Count - 1);
                            else LogWarning("Superfluous #endregion found without corresponding #region");
                            break;

                        default:
                            ReportError($"Unknown preprocessor directive: {directive.Value}");
                            return false;
                    }
                    break;
                }

                case ZScriptTokenType.Identifier:
                    switch (token.Value.ToLowerInvariant())
                    {
                        case "extend":
                        {
                            Tokenizer.SkipWhitespace();
                            var kind = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                            if (kind is not { IsValid: true } || (kind.Value.ToLowerInvariant() != "class" && kind.Value.ToLowerInvariant() != "struct"))
                            {
                                ReportError($"Expected class or struct, got {(object?)kind ?? "<null>"}");
                                return false;
                            }
                            if (!ParseClassOrStruct(kind.Value.ToLowerInvariant() == "struct", true, false, regions.Count > 0 ? regions[^1] : null)) return false;
                            break;
                        }

                        case "mixin":
                        {
                            Tokenizer.SkipWhitespace();
                            var kind = Tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                            if (kind is not { IsValid: true } || kind.Value.ToLowerInvariant() != "class")
                            {
                                ReportError($"Expected class, got {(object?)kind ?? "<null>"}");
                                return false;
                            }
                            if (!ParseClassOrStruct(false, false, true, regions.Count > 0 ? regions[^1] : null)) return false;
                            break;
                        }

                        case "class":
                            if (!ParseClassOrStruct(false, false, false, regions.Count > 0 ? regions[^1] : null)) return false;
                            break;

                        case "struct":
                            if (!ParseClassOrStruct(true, false, false, null)) return false;
                            break;

                        case "const":
                            if (!ParseConst()) return false;
                            break;

                        case "enum":
                            if (!ParseEnum()) return false;
                            break;

                        case "version":
                            Tokenizer.SkipWhitespace();
                            if (Tokenizer.ExpectToken(ZScriptTokenType.String) is not { IsValid: true })
                            {
                                ReportError("Expected version string");
                                return false;
                            }
                            break;

                        default:
                            ReportError($"Expected preprocessor statement, const, enum or class declaraction, got {token}");
                            return false;
                    }
                    break;
            }
        }

        foreach (var include in includes)
        {
            if (!ParseInclude(include)) return false;
        }

        return true;
    }

    /// <summary>
    /// UDB's own real name for this is the reserved word `Finalize` - see
    /// the class doc comment for why it's renamed here. Walks every class
    /// recorded by <see cref="Parse"/>, actually parses each one's body,
    /// then wires up inheritance/mixins/extensions once every class in this
    /// scan is known. Call once, after every file (including all its
    /// `#include`s) has been through <see cref="Parse"/>.
    /// </summary>
    public bool CompleteParsing()
    {
        ClearError();

        foreach (var cls in _allClassesList)
        {
            if (!cls.Process()) return false;
            foreach (var extension in cls.Extensions)
                if (!extension.Process()) return false;
        }

        foreach (var cls in _mixinClassesList)
        {
            if (!cls.Process()) return false;
        }

        foreach (var cls in _allClassesList)
        {
            var actor = cls.Actor;
            if (actor == null) continue;

            if (cls.ParentName != null && !cls.ParentName.Equals("thinker", StringComparison.OrdinalIgnoreCase))
            {
                actor.BaseClass = GetArchivedActorByName(cls.ParentName, true);
                InheritFromStaticGameConfiguration(actor, cls.ParentName);
            }

            foreach (var mixinClassName in actor.Mixins) ApplyMixin(actor, mixinClassName);

            foreach (var extension in cls.Extensions)
            {
                var extensionActor = extension.Actor;
                if (extensionActor == null) continue;

                foreach (var mixinClassName in extensionActor.Mixins) ApplyMixin(extensionActor, mixinClassName);

                if (extensionActor.States.ContainsKey("spawn")) actor.States["spawn"] = extensionActor.GetState("spawn")!;
                if (extensionActor.Properties.ContainsKey("height")) actor.Properties["height"] = new List<string>(extensionActor.Properties["height"]);
                if (extensionActor.Properties.ContainsKey("radius")) actor.Properties["radius"] = new List<string>(extensionActor.Properties["radius"]);
                if (extensionActor.Flags.ContainsKey("spawnceiling")) actor.Flags["spawnceiling"] = true;
                if (extensionActor.Flags.ContainsKey("solid")) actor.Flags["solid"] = true;
            }
        }

        return true;
    }

    private void InheritFromStaticGameConfiguration(ActorStructure actor, string parentName)
    {
        if (actor.BaseClass != null)
        {
            for (var i = 0; i < 5; i++) actor.GetArgumentInfo(i); // preserve base-class args the same way UDB's own null-fill does (handled by GetArgumentInfo's own fallback)
        }

        if (GameConfiguration == null) return;

        var parentCheck = parentName.ToLowerInvariant();
        var match = GameConfiguration.GetThingTypes().FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.ClassName) && t.ClassName.Equals(parentCheck, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            if (actor.BaseClass == null) LogWarning($"Unable to find \"{parentName}\" class to inherit from, while parsing \"{actor.ClassName}\"");
            return;
        }

        if (actor.States.Count == 0 && !string.IsNullOrEmpty(match.SpriteName))
        {
            var spriteName = InternalSprites.IsInternalName(match.SpriteName) ? match.SpriteName : match.SpriteName[..Math.Min(5, match.SpriteName.Length)];
            actor.States["spawn"] = new StateStructure(spriteName);
        }

        if (actor.BaseClass == null)
        {
            if (match.Hangs && !actor.Flags.ContainsKey("spawnceiling")) actor.Flags["spawnceiling"] = true;
            if (!actor.Properties.ContainsKey("height")) actor.Properties["height"] = new List<string> { match.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (!actor.Properties.ContainsKey("radius")) actor.Properties["radius"] = new List<string> { match.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }
    }

    private void ApplyMixin(ActorStructure actor, string mixinClassName)
    {
        if (!_mixinClasses.TryGetValue(mixinClassName, out var mixinCls) || mixinCls.Actor == null)
        {
            LogWarning($"Unable to find \"{mixinClassName}\" mixin class while parsing \"{actor.ClassName}\"");
            return;
        }

        var mixinActor = mixinCls.Actor;

        if (actor.States.Count == 0 && mixinActor.States.Count != 0)
        {
            if (!actor.States.ContainsKey("spawn") && mixinActor.States.TryGetValue("spawn", out var spawn))
                actor.States["spawn"] = spawn;
        }

        if (!actor.Properties.ContainsKey("height") && mixinActor.Properties.ContainsKey("height"))
            actor.Properties["height"] = new List<string>(mixinActor.Properties["height"]);

        if (!actor.Properties.ContainsKey("radius") && mixinActor.Properties.ContainsKey("radius"))
            actor.Properties["radius"] = new List<string>(mixinActor.Properties["radius"]);

        if (!actor.Flags.ContainsKey("spawnceiling") && mixinActor.Flags.ContainsKey("spawnceiling")) actor.Flags["spawnceiling"] = true;
        if (!actor.Flags.ContainsKey("solid") && mixinActor.Flags.ContainsKey("solid")) actor.Flags["solid"] = true;
    }

    protected override int GetCurrentLineNumber()
    {
        PrevStreamPosition = Tokenizer?.LastPosition ?? -1;
        return base.GetCurrentLineNumber();
    }

    public ActorStructure? GetActorByName(string name) => _actors.GetValueOrDefault(name.ToLowerInvariant());

    public ActorStructure? GetActorByDoomEdNum(int doomEdNum) => _actors.Values.FirstOrDefault(a => a.DoomEdNum == doomEdNum);

    internal ActorStructure? GetArchivedActorByName(string name, bool unique)
    {
        var dict = unique ? _realArchivedActors : _archivedActors;
        return dict.GetValueOrDefault(name.ToLowerInvariant());
    }
}
