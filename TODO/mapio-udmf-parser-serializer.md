# UDMF text format parser/serializer

**Status:** Done  
**Area:** Map I/O

- [x] `Core.IO`: UDMF text format parser/serializer - a close port of
      UDB's own `UniversalParser`/`UniversalStreamReader`/
      `UniversalStreamWriter` (~2200 lines read across two research
      passes into the actual source, not just its behavior guessed at),
      since a from-scratch reimplementation risked missing real
      edge-case behavior UDB already gets right:
      - `UdmfTreeParser`: the tokenizer, same grammar/number-classification
        (hex, the exact non-general "contains '.' or 'e-'" float
        heuristic, int-escalating-to-long) /string-escape/keyword rules
        as UDB's `InputStructure`, reorganized into named methods around
        a small cursor instead of one ~500-line switch loop. One
        deliberate fix (discussed with the user): UDB's own `\DDD` string
        escape has a real bug (only advances 1 of 3 digits, so the
        trailing 2 leak into the string) - fixed here rather than
        reproduced, since nothing depends on the bug
      - `UdmfReader`/`UdmfWriter`: exact field defaults (sector
        `lightlevel` = 160 not 255, linedef `sidefront`/`sideback` = -1
        sentinel, etc.), exact "log a warning and drop" recovery for
        malformed references (dangling vertex, zero-length linedef,
        out-of-range sidedef index, sidedef-with-invalid-sector) rather
        than aborting the whole load, and UDB's own asymmetric
        field-omission rules on write (sector always writes its five
        core fields even at defaults; sidedef omits offsets-when-zero
        and textures-when-"-"; linedef always writes sidefront/sideback,
        `-1` when absent)
      - Went further than pure UDMF-format porting: `Vertex`/`Sector`/
        `Linedef`/`Sidedef` each gained a `CustomFields` bag (boxed
        `object`, no dependency from `Core.Map` back onto `Core.IO`)
        holding any UDMF field recognized by the format but not yet a
        typed property here (linedef `special`/`arg0..arg4`, sector
        `id`/slopes, sidedef flags, vertex `zceiling`/`zfloor`, and any
        genuinely arbitrary third-party field). Combined with whole-block
        preservation for block types we don't recognize at all (`thing`
        included), a load-then-save round-trip loses almost nothing, even
        though most of it isn't editable yet
      - 96 Core tests total (51 new for this pass) covering the
        tokenizer, the reader's defaults/drop-rules/custom-field capture,
        the writer's formatting/omission rules, and full round-trips
