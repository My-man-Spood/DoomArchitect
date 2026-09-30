# Test Map (F9)

**Status:** Done  
**Area:** Test Map

- [x] Test Map (F9 + toolbar split button) - launches the current map in a
      configured external source port, matching UDB's real `Launcher`/
      `EngineInfo`/`ConfigurationInfo` architecture (verified against the
      actual source, not guessed): multiple named test engines per game
      configuration (`Core.Configuration.TestEngine`, stored in
      `AppSettings` under a `testengines` block, editable via Preferences'
      new Test Engines tab, `Scripts/View/TestEnginesEditor.cs`) rather than
      one engine per config; a combined skill+monsters picker (every skill
      from the game config's own `skills` block, times with/without
      monsters, sign-encoded like UDB's own `TestSkill_Click`); a real
      digit-run `%L1`/`%L2` scanner (`Core.Configuration.
      TestLaunchCommandBuilder`, unit-tested) rather than an ExMy/MAPxx
      special case, matching UDB's actual `Launcher.ConvertParameters`
      algorithm exactly. UI matches UDB's real toolbar split-button shape
      (`Scripts/View/TestMapToolbar.cs`) rather than a Map-menu item, after
      the first pass put it in the wrong place - a play button that
      launches at the last skill/monsters choice, plus a small separate
      dropdown-arrow button opening a skill popup with the real icon
      grouping (with-monsters/no-monsters, separated, UDB's own
      `Monster2`/`Monster3` icons).
      **Deliberate implementation differences, flagged**: launches via
      Godot's own `OS.CreateProcess(path, string[] arguments)` (a real
      argument array) instead of UDB's single shell-escaped command-line
      string - a genuine improvement this project's process-launch API
      allows for, not a 1:1 port of that specific mechanical detail.
      `TestShortPaths` (a Windows short-path workaround for source ports
      with poor long-path support) is read from the `.cfg` data but not
      applied - a known, deliberate Windows-only gap, not silently missed.
      The resource list here has no distinct "which one is the IWAD"
      concept the way UDB's `DataLocation` does, so the first configured
      resource is treated as the IWAD by convention (documented in
      `TestMapLauncher`), everything after it as additional resources.
      **Update, the gzdoom.pk3 exclusion this was originally blocked on is
      now done:** `RequiredArchive`/`RequiredArchiveEntry`
      (`IGameConfiguration.GetRequiredArchives()`) read the already-bundled
      `requiredarchives` block (`GameConfigurationLoader`, confirmed
      against the real `GZDoom_common.cfg` data - one entry, `gzdoom.pk3`,
      `ExcludeFromTesting = true`, a class-named-`Actor` + `x11r6rgb.txt`
      fingerprint). `ZDoom.RequiredArchiveDetector.Matches` answers "is
      this specific resource that archive" by real content, using the
      actual `ZScriptParser`/`DecorateParser` from the port above (checked
      against `ZScriptParser.DeclaredClassNames` directly - available right
      after `Parse()`, no `CompleteParsing()` needed, so an unrelated
      class's own broken inheritance elsewhere in the same resource can
      never cause a false negative here) - a genuine content fingerprint,
      not the regex approximation originally planned before the full port
      existed. `TestMapLauncher` now drops any `%AP` resource that matches
      an `ExcludeFromTesting` archive (opened fresh via
      `ResourceContainerFactory`, kept rather than dropped if it can't even
      be opened, so it still fails normally downstream instead of silently
      vanishing). `ResourceListEditor` gained the matching "required
      resource missing" warning (a new `GameConfiguration` property,
      re-checked via the same detector on every add/remove/game-config
      change), wired from both `MapOptionsDialog` and `PreferencesDialog`'s
      own default-resources tab. 11 new Core tests (bundled-data read,
      detector fingerprint matching including the "don't let an unrelated
      broken class cause a false negative" case).
