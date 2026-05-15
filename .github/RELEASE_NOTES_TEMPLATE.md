# Release Notes Template

> Use this template for every GitHub release. Goal: an end-user can understand the value of the release in **30 seconds** without reading any code references.

## Philosophy

- **Summary first** — one sentence, plain language. If the reader stops here, they should still know what changed.
- **Details next** — concrete examples, tables, comparisons. No code symbols.
- **Technical details last** — collapsible `<details>` section for the curious. Internal class names, schema changes, regex patterns go here.

The standard update procedure (Plugins → Catalog → Update → Restart Jellyfin) is the same for every release and lives in the README. Don't repeat it in release notes unless this release has a non-standard migration step.

## ⛔ Hard Rules — above the `<details>` tag

Anything visible to a non-developer **must not contain**:

| Forbidden | Why | Replace with |
|---|---|---|
| Any token with a dot-and-CapitalCase (`PluginManifest.Id`, `ApiClient.getItems`) | Sounds like API docs | A description of the user-visible effect |
| Any C# or JS keyword (`true`, `false`, `null`, `void`, `using`, `const`, `await`) | Code symbols | Plain sentence |
| Any backtick code span like `client.js`, `meta.json`, `OnItemUpdated`, `IsMainConfigPage` | Internal names | Describe what the file/function does, in plain words |
| `[JsonPropertyName(...)]`, `[ApiController]`, attribute brackets | Dev annotations | Skip — not relevant to users |
| Version references like *"from 5.5.X.Y"*, *"introduced in 5.5.X.Y"*, *"reverted Fix #N"* | Internal history | Describe the current state, not the path |
| Code blocks (` ``` `) | Implementation detail | Move to the `<details>` block |
| Field/parameter names: `pathChanged`, `sizeRatio`, `UpgradeKind`, `EnableInMainMenu` | API surface | User-facing label: *"upgrade type"*, *"sidebar entry"* |
| Database / SQL terms: `ALTER TABLE`, `nullable column`, `migration` | DBA-speak | *"Existing data keeps working"* |
| Regex syntax: `[a-z0-9]`, `\b`, `(?:...)` | Compiler-speak | *"Detects filename keywords like BluRay, HEVC, VFF"* |

The `<details>` block has no rules. Put all the jargon there.

## ✅ Pre-publish checklist

Before clicking "Publish release", verify ALL of these about the user-facing section (everything above the first `<details>` tag):

1. [ ] **No backticks**, except around UI labels the user actually sees (`UPD`, `MAJ • Quality`).
2. [ ] **No filenames** ending in `.cs`, `.js`, `.html`, `.json`.
3. [ ] **No CamelCase identifiers** that aren't user-facing product names.
4. [ ] **No reference to previous internal versions** like "fix from 5.5.X.Y".
5. [ ] **A non-technical reader can rephrase the In Short section** without losing meaning.

If you can't tick all 5, rewrite the section.

## Format (copy-paste this)

```markdown
## 🇬🇧 English

### 📝 In Short

[One sentence. What the user sees / experiences differently after this release.]

### ✨ What's New / 🐛 What's Fixed

- **[Feature/Fix Title]** — Plain-language description. Concrete example if possible.
- **[Feature/Fix Title]** — ...

---

<details>
<summary>🔧 Technical details for the curious</summary>

[All the dev jargon: file names, function names, schema changes,
regex patterns, performance numbers, edge cases.]

</details>

---

## 🇫🇷 Français

### 📝 En bref

[Une phrase. Ce que l'utilisateur voit / vit différemment après cette release.]

### ✨ Quoi de Neuf / 🐛 Corrections

- **[Titre Feature/Fix]** — Description en langage humain. Exemple concret si possible.
- **[Titre Feature/Fix]** — ...

---

<details>
<summary>🔧 Détails techniques pour les curieux</summary>

[Tout le jargon dev : noms de fichiers, fonctions, changements de schéma,
patterns regex, chiffres de perf, cas limites.]

</details>
```

## DOs

- ✅ Use **tables** for feature comparisons (before/after)
- ✅ Use **real filenames as content examples** (`Movie.WEB-DL.1080p.mkv → Movie.BluRay.2160p.mkv`) — these are user-recognizable, not code
- ✅ Use **bold** for badge labels and UI elements (**UPD**, **Quality**, **MAJ • Codec**)
- ✅ Lead with the **user benefit**, not the implementation
- ✅ End with **"That's it."** when no reconfiguration is needed — reassures the user
- ✅ When the release is part of a chain of iterations and on its own is incomplete, say so plainly: *"This release alone wasn't enough; the full result lands in 5.5.X.Y."* (this is fine because it's about user behavior, not internal mechanics)

## DON'Ts

- ❌ No internal class names in the user-facing section (`OnItemUpdated`, `ProcessBuffer`, etc. → only in `<details>`)
- ❌ No file paths or line numbers in the user-facing section
- ❌ No `pathChanged`, `ALTER TABLE`, `regex` etc. above the fold
- ❌ No phrase like *"reverted Fix #N from version X.Y.Z"* — translate to user impact (*"file replacements now appear correctly again"*)
- ❌ No emoji overload — 1-2 per section header, none in the body
- ❌ Don't describe the bug in dev terms — describe the symptom

## Example Translation (dev → user)

| Dev wording (bad) | User wording (good) |
|---|---|
| Reverted `IsItemInEnabledLibrary` pre-filter in `OnItemAdded` | File replacements appear correctly again |
| Added column `UpgradeKind TEXT NULL` with `ALTER TABLE` migration | New sub-label on the UPD badge tells you what changed |
| `pathChanged \|\| (sizeChanged && dateChanged)` triggers `IsUpgrade = true` | The bell now detects when a file was replaced |
| Phase A heuristic using path-based regex with word boundaries | Detection based on filename keywords like `BluRay`, `HEVC`, `VFF` |
| Removed `installAdminSidebarIcon()` DOM-injection fallback from client.js | One icon instead of two in the admin sidebar entry |
| Set `MenuIcon = "notifications"` on `PluginPageInfo` so Jellyfin renders the bell | NotifySync's bell icon shows up natively next to its sidebar entry |
| `meta.json` was using `"id"` but `PluginManifest` is annotated `[JsonPropertyName("guid")]` | Removes a recurring error in your Jellyfin logs |
| Refactored `ClassifyUpgrade` to add `DetectCodec(path)` returning `"av1"`/`"hevc"`/`"x264"` | Codec swaps between AV1 / x265 / x264 are now correctly labelled |

## Title Format

Always: `🔔 NotifySync vX.Y.Z.W` (matches the established pattern from earlier releases).

## Repository Auto-Refresh Reminder

Jellyfin caches the `repository.json` feed for a few minutes. When testing the release in Jellyfin:
1. **Tableau de bord → Extensions → Catalogue → F5**
2. If still stale: **Extensions → Dépôts** → remove and re-add the NotifySync repository

Mention this in the release notes only if there's a known issue requiring it — otherwise it's just noise.

## Timestamps

The `timestamp` field in `repository.json` must be **strictly in the past** in UTC. Always:

```bash
date -u    # check actual UTC time first
```

Don't trust the local-zone date you might see in editor banners or system reminders — France crossed midnight before UTC did, so taking "today" as the local date can produce a UTC timestamp in the future, which Jellyfin can silently filter out of the catalog.
