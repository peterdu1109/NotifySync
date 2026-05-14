# Release Notes Template

> Use this template for every GitHub release. Goal: an end-user can understand the value of the release in **30 seconds** without reading any code references.

## Philosophy

- **TL;DR first** — one sentence, plain language. If the reader stops here, they should still know what changed.
- **Action items second** — what they need to do (update, restart, reconfigure).
- **Details third** — concrete examples, tables, comparisons. No code symbols.
- **Technical details last** — collapsible `<details>` section for the curious. Internal class names, schema changes, regex patterns go here.

## Format (copy-paste this)

```markdown
## 🇬🇧 English

### 🎉 TL;DR

[One sentence. What the user sees / experiences differently after this release.]

### 👉 What You Need to Do

1. **Plugins → Catalog → Update NotifySync**
2. **Restart Jellyfin**

[Add any extra step only if truly required — reconfiguration, manual action, etc.]

### ✨ What's New / 🐛 What's Fixed

- **[Feature/Fix Title]** — Plain-language description. Concrete example if possible.
- **[Feature/Fix Title]** — ...

### 💬 Background (optional)

[Why this release exists — user feedback, observed bug, etc. Keep it short.]

---

<details>
<summary>🔧 Technical details for the curious</summary>

[All the dev jargon: file names, function names, schema changes,
regex patterns, performance numbers, edge cases.]

</details>

---

## 🇫🇷 Français

### 🎉 TL;DR

[Une phrase. Ce que l'utilisateur voit / vit différemment après cette release.]

### 👉 Ce Que Tu Dois Faire

1. **Extensions → Catalogue → Mettre à jour NotifySync**
2. **Redémarrer Jellyfin**

### ✨ Quoi de Neuf / 🐛 Corrections

- **[Titre Feature/Fix]** — Description en langage humain. Exemple concret si possible.
- **[Titre Feature/Fix]** — ...

### 💬 Contexte (optionnel)

[Pourquoi cette release existe — retour utilisateur, bug observé, etc. Garder court.]

---

<details>
<summary>🔧 Détails techniques pour les curieux</summary>

[Tout le jargon dev : noms de fichiers, fonctions, changements de schéma,
patterns regex, chiffres de perf, cas limites.]

</details>
```

## DOs

- ✅ Use **tables** for feature comparisons (before/after)
- ✅ Use **real filenames** as examples (`Movie.WEB-DL.1080p.mkv → Movie.BluRay.2160p.mkv`)
- ✅ Use **bold** for badge labels and UI elements (**UPD**, **Quality**, **MAJ • Codec**)
- ✅ Lead with the **user benefit**, not the implementation
- ✅ End with **"That's it."** when no reconfiguration is needed — reassures the user

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

## Title Format

Always: `🔔 NotifySync vX.Y.Z.W` (matches the established pattern from earlier releases).

## Repository Auto-Refresh Reminder

Jellyfin caches the `repository.json` feed for a few minutes. When testing the release in Jellyfin:
1. **Tableau de bord → Extensions → Catalogue → F5**
2. If still stale: **Extensions → Dépôts** → remove and re-add the NotifySync repository

Mention this in the release notes only if there's a known issue requiring it — otherwise it's just noise.
