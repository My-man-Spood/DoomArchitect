# PK3 resource loading

**Status:** Done  
**Area:** Resources

- [x] PK3 (zip-based) resource loading - pulled forward ahead of property
      editing UI because a real test map depends on `gzdoom.pk3` for
      textures/flats/sprites/patches it doesn't embed itself. Researched
      UDB's real `DataReader`/`WADReader`/`PK3StructuredReader`/`PK3Reader`
      hierarchy (`Source/Core/Data/*.cs`) before designing this, then built
      the shared lookup surface this entry originally envisioned:
      `Core.IO.IResourceContainer` (`FindLump`/`FindNamespaceLumps`), with
      `WadFile` and the new `Pk3File` both implementing it, and
      `WadResourceSet` widened into `ResourceSet` over `IResourceContainer`
      instead of `WadFile` specifically - `TextureSet` never needed to
      change its own logic, just its declared types. `ResourceContainerFactory.Open`
      sniffs a path's magic bytes (`IWAD`/`PWAD` vs. the zip `PK` signature)
      so the resource-list UI (Map Options, Preferences) and saved
      `.dbs`/`settings.cfg` resource paths both just work whether a path is
      a `.wad` or a `.pk3`, no extension-trusting required. PK3 namespace
      folders (`patches/`, `textures/`, `flats/`, `sprites/`, `graphics/`)
      ported literally from UDB's real `PK3StructuredReader` constants -
      only the 5 namespaces `TextureSet` actually consumes; `hires/`/
      `colormaps/`/`voxels/` are real GZDoom conventions too but nothing
      reads them yet from either a WAD or a PK3, so left out until
      something does.

      Deliberate divergences from UDB, flagged rather than silent: built on
      .NET's own built-in `System.IO.Compression.ZipArchive` instead of the
      third-party SharpCompress UDB uses (SharpCompress's whole reason for
      being there is tolerating a PK3 that's secretly a rar/7z file - not a
      real-world need, and this way needed zero new dependencies); within
      one archive, a duplicate path keeps the *first* entry and silently
      drops later ones, matching UDB's own real (arguably non-canonical
      versus real GZDoom's last-wins) behavior on purpose, since UDB is
      this project's north star for Core logic.

      **Deliberately not built**: a `.wad` embedded in a PK3's own root
      (UDB supports this; not needed for `gzdoom.pk3`, which doesn't embed
      one); nested PK3-in-PK3 (UDB doesn't support this either);
      `MixTexturesFlats`/`roottextures`/`rootflats` game-config options
      (still hardcoded to vanilla defaults, same as the texture pipeline
      entry above); saving/writing to a PK3 (resources are only ever
      referenced by path, never rewritten, same as WADs today).
