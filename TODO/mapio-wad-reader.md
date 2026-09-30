# WAD reader

**Status:** Done  
**Area:** Map I/O

- [x] `Core.IO`: WAD reader (`WadFile`) - the classic container format
      (12-byte header + flat lump directory, a map's lumps identified
      purely by position relative to its marker lump, e.g. `MAP01`), plus
      `MapFileLoader.LoadUdmfMap(wadPath, mapName)` wiring it straight
      into the UDMF reader. **UDMF-format maps only for now** - deliberately
      scoped down from "any WAD" after discussing size with the user.
      `WadFile.ReadMapTextMap` throws a clear `NotSupportedException`
      (not a confusing parse failure) when a map's marker isn't
      immediately followed by `TEXTMAP`, i.e. when it's a classic
      binary-format map
