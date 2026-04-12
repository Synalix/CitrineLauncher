# Citrine Launcher Bug Log

Scope: confirmed defects still present in the current codebase. Items are ordered by severity. Each entry includes a short cause, impact, and a minimal fix direction so Claude can work through them one by one.

## Medium

### 1. Re-authing an existing Microsoft account now triggers a second interactive login
Files: [SettingsPanel.axaml.cs](SettingsPanel.axaml.cs#L128), [Handlers/MicrosoftAuth.cs](Handlers/MicrosoftAuth.cs#L13)

Cause: the add flow first authenticates a temporary account to discover the username, then calls `AuthenticateAsync(existing.Id)` again when it finds a matching Microsoft account. `AuthenticateAsync` is interactive, so the existing-account path opens the browser twice and discards the first session.

Impact: re-authing a saved Microsoft account becomes unnecessarily slow and fragile, and cancelling the second prompt wastes the first auth round trip.

Minimal fix: move the session cache entry from the temp account id to the persisted account id, or expose a cache-rekey helper instead of authenticating twice.

### 2. Skins panel can act on the wrong Microsoft account when silent auth returns a different session
Files: [SkinsPanel.axaml.cs](SkinsPanel.axaml.cs#L150), [SkinsPanel.axaml.cs](SkinsPanel.axaml.cs#L274), [SkinsPanel.axaml.cs](SkinsPanel.axaml.cs#L342), [SkinsPanel.axaml.cs](SkinsPanel.axaml.cs#L382)

Cause: `LoadMicrosoftAccountAsync`, skin upload, cape toggle, and cape selection all trust `GetSessionAsync(account.Id)` without verifying that the returned session still belongs to the selected account.

Impact: the skins view can load or mutate profile and cape data for the wrong Microsoft account.

Minimal fix: validate `session.Username` against the selected account before using the session; if it differs, clear the cache and re-auth or abort with an error.

### 3. Changing `MinecraftPath` does not migrate the existing launcher data
Files: [SettingsPanel.axaml.cs](SettingsPanel.axaml.cs#L190), [MainWindow.axaml.cs](MainWindow.axaml.cs#L164), [Handlers/InstanceManager.cs](Handlers/InstanceManager.cs#L13)

Cause: the folder edit flow only updates `Settings.MinecraftPath`; `MainWindow` immediately reloads instances from the new root, but nothing copies the existing `instances/`, `versions/`, or skin cache data over.

Impact: changing the folder makes the existing library appear to disappear unless the user manually moves files, which defeats the "relocate everything" behavior.

Minimal fix: move or copy the existing root contents to the new path before reloading, or explicitly warn that the setting only retargets future data.

### 4. Fabric profile JSON parsing assumes an `id` field and can crash on malformed API responses
Files: [Handlers/InstanceManager.cs](Handlers/InstanceManager.cs#L168)

Cause: `InstallFabricAsync` calls `GetProperty("id")` on the profile JSON without checking that the field exists.

Impact: a malformed or unexpected Fabric response aborts the Fabric install flow with a cryptic exception.

Minimal fix: use `TryGetProperty` and throw a context-rich error when the response is missing required fields.

## Low

### 5. `MainWindow` restores account selection with a case-sensitive username comparison
Files: [MainWindow.axaml.cs](MainWindow.axaml.cs#L142)

Cause: the refresh logic uses `a.Username == savedUsername` while the rest of the launcher compares usernames case-insensitively.

Impact: the wrong account can be restored after a refresh if casing differs.

Minimal fix: switch to `string.Equals(a.Username, savedUsername, StringComparison.OrdinalIgnoreCase)`.

### 6. Modrinth temp downloads are never cleaned up after import
Files: [Handlers/ModrinthClient.cs](Handlers/ModrinthClient.cs#L141), [NewInstanceDialog.axaml.cs](NewInstanceDialog.axaml.cs#L189)

Cause: `DownloadToTempAsync` writes a temp `.mrpack` file, and the new-instance flow imports from it but never deletes it afterward.

Impact: repeated Modrinth browsing and imports leave orphaned temp files behind.

Minimal fix: delete the temp file after `ModpackImporter.ImportFromZipAsync` completes, or wrap the download in a disposable temp-file helper.

### 7. Deleting a Microsoft account leaves its auth cache entry in memory
Files: [SettingsPanel.axaml.cs](SettingsPanel.axaml.cs#L273), [Handlers/MicrosoftAuth.cs](Handlers/MicrosoftAuth.cs#L54)

Cause: `DeleteMenuItem_Click` removes the account from settings, but it never calls `MicrosoftAuth.ClearCache(selectedAccount.Id)` for Microsoft accounts.

Impact: stale session data stays in the in-memory cache until the app exits, which is unnecessary and makes the cache state harder to reason about.

Minimal fix: clear the cache when deleting a Microsoft account.
