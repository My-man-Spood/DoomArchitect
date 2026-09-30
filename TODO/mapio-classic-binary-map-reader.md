# Classic binary-format map reader

**Status:** Done  
**Area:** Map I/O

- [x] **Classic binary-format map reader** (`ClassicMapReader`) - fixed-size
      binary records (`VERTEXES`=4, `SECTORS`=26, `SIDEDEFS`=30,
      `LINEDEFS`=14 bytes/record), a close port of UDB's own
      `DoomMapSetIO`. Verified against the actual UDB source rather than
      general Doom-format community knowledge, which caught a real
      gotcha: sidedef texture fields are ordered
      upper-then-**lower**-then-**middle**, not the commonly-assumed
      upper-then-middle-then-lower (covered by a dedicated test). Same
      "warn and drop" recovery as the UDMF reader (dangling vertex
      refs, zero-length linedefs, out-of-range sidedef/sector refs), and
      the same `CustomFields` bucket for what isn't a typed property
      (linedef flags/special/tag, sector special/tag) - `THINGS` is
      skipped entirely, since there's no Things model and, unlike UDMF,
      no writer for this format to round-trip through anyway.
      Hexen/ZDoom-format maps are rejected with a clear
      `NotSupportedException` (that format has entirely different record
      layouts, not ported) - detected the same way UDB itself
      distinguishes formats: a `BEHAVIOR` lump alongside the map, not by
      guessing from record sizes (confirmed UDB does NOT sniff
      Doom-vs-Hexen from `LINEDEFS` byte width - that would have been a
      reasonable-sounding but wrong assumption to make without checking).
      `WadFile.FindClassicMapNames()` + `MapFileLoader.LoadClassicMap`
      mirror their UDMF counterparts; `OpenMapMenu` now tries UDMF first,
      then falls back to classic format - opening an original id
      Software WAD works end to end
