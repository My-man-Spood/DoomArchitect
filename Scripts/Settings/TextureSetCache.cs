using System;
using System.Collections.Generic;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Textures;

namespace DoomArchitect.Settings;

/// <summary>
/// Process-wide cache of already-decoded <see cref="TextureSet"/>s, keyed by
/// an <paramref name="identity"/> list the caller builds - one token per
/// container in the <see cref="ResourceSet"/> being loaded, compared by
/// value (<see cref="object.Equals(object)"/>) in order.
///
/// This is the actual fix for "opening a second map from the same mod
/// shouldn't re-decode the same textures": <see cref="TextureSet.Load"/>
/// itself is cheap (it only parses PLAYPAL/PNAMES/TEXTURE1/TEXTURE2), but
/// every wall/flat/sprite it resolves after that is decoded lazily and
/// cached *per instance* (its own private wall/flat/sprite dictionaries) -
/// so a fresh <see cref="TextureSet"/> always starts cold, even when it's
/// built from the exact same bytes a different already-open tab already
/// decoded moments ago. <see cref="ResourceContainerCache"/> only dedupes
/// the raw containers (avoiding a redundant disk read); it does nothing
/// about this, since it still hands every caller a brand-new
/// <see cref="TextureSet"/> on every map open.
///
/// Why the identity list isn't just "the containers themselves", the way
/// <see cref="ResourceContainerCache"/> is keyed by path: a map's own
/// backing WAD is always read fresh (<c>OpenMapMenu</c> never caches it -
/// a save must always be reflected), so it's a brand-new object on every
/// single open even when the file on disk hasn't changed at all - keying
/// on that object's reference would almost never hit. But it can't just be
/// ignored either: a map's own WAD can embed its own PLAYPAL/PNAMES/
/// TEXTURE1/TEXTURE2/graphics, layered at the *highest* priority, so two
/// maps from the same mod can legitimately have different embedded
/// textures - dropping it from the identity risks silently serving one
/// map's textures to another, not just an unnecessary redecode. The
/// caller instead passes a cheap value token for that one entry - its
/// path plus last-write-time and length - which stays equal across
/// repeated opens of the same unmodified file but correctly changes the
/// moment that file is edited or saved, while every other, already-shared
/// container (via <see cref="ResourceContainerCache"/>) is passed as
/// itself and compared by reference, same as before.
///
/// Like <see cref="ResourceContainerCache"/>, entries are never evicted -
/// see its own remarks on why that's an accepted, deliberate limitation
/// right now rather than an oversight.
/// </summary>
public static class TextureSetCache
{
    private static readonly Dictionary<IReadOnlyList<object>, TextureSet> Cache =
        new(new IdentityListComparer());

    public static TextureSet Load(ResourceSet resources, IReadOnlyList<object> identity)
    {
        if (Cache.TryGetValue(identity, out var existing)) return existing;

        var textureSet = TextureSet.Load(resources);
        Cache[identity] = textureSet;
        return textureSet;
    }

    private sealed class IdentityListComparer : IEqualityComparer<IReadOnlyList<object>>
    {
        public bool Equals(IReadOnlyList<object> a, IReadOnlyList<object> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!Equals(a[i], b[i])) return false;
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<object> list)
        {
            var hash = new HashCode();
            foreach (var token in list) hash.Add(token);
            return hash.ToHashCode();
        }
    }
}
