# ZDoom Wiki raw research dump

Local-only staging material for writing hover doc comments on real ACS
functions and action specials (`BcsBuiltinFunctions`, and the
`special`-declared functions parsed out of `zcommon.bcs`). **Not
shipped, not committed** - see `/.gitignore`.

## What's here

- `raw/*.wikitext` - the raw wikitext of 511 ZDoom Wiki pages (action
  specials + ACS functions), fetched directly from the wiki's own
  MediaWiki API (`https://zdoom.org/w/api.php`), one file per page,
  named after the page title.
- `manifest/categories.json` - which wiki category each page title came
  from.
- `manifest/all_titles.json` - the deduped title list the fetch was
  driven from.

Gathered from these categories: `Action specials by name`, the ten
`ACS * functions`/`ACS String operations` topic categories, `ACS
specials`, plus ~20 pages (`PlayerHealth`, `BlueCount`, `GetCVar`,
etc.) that turned out to be tagged `Skulltag features` instead of an
ACS category, found by direct title lookup once the category pass
left them as gaps.

## Why this can never be committed or shipped

The ZDoom Wiki's content license is the **GNU Free Documentation
License 1.2** (confirmed via the wiki's own `siprop=rightsinfo` API),
not something permissive. Reproducing its text - verbatim or lightly
reworded - would carry real obligations (preserving the license text,
original authorship, a "History" section, etc.) that a casual
"Sources" credit doesn't satisfy.

The way around that: this raw dump is a *fact-finding* source, never
copied into the actual product. Hover doc comments must be written as
short, original sentences in our own words, from the facts learned
here (what a function/parameter does) - not excerpted or paraphrased
wiki prose. A "Sources: ZDoom Wiki" credit in an About page is then
just a courtesy, not a license requirement, because nothing of the
wiki's own copyrighted expression ships.

## Using it

When writing a function's doc comment, read its file here, note the
facts (what it does, what each parameter means, any gotchas), then
write a fresh one-or-two-sentence description from those facts - the
same way you'd write documentation after reading a manual, not by
editing a copy of it.
