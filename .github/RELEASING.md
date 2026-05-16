# Releasing NotifySync

Two channels: **stable** (default for end-users) and **beta** (opt-in for testers).

## Channels

| Channel | Git branch | Tags | GitHub release | Repository file | Audience |
|---|---|---|---|---|---|
| **Stable** | `main` | `vX.Y.Z.W` | Latest | `repository.json` | Everyone |
| **Beta** | `beta` | `vX.Y.Z.W-betaN` / `-rcN` | Pre-release ✅ | `repository-beta.json` | Opt-in testers |

Users opt into beta by adding a second repository URL in Jellyfin:
```
https://raw.githubusercontent.com/peterdu1109/NotifySync/main/repository-beta.json
```

## Beta workflow (recommended for non-trivial changes)

1. **Develop on the `beta` branch.**

   ```bash
   rtk git checkout beta
   rtk git pull
   # ...code, commit...
   rtk git push
   ```

2. **Cut a beta release.** Bump version with a beta suffix in `csproj`, `meta.json`, `client.js`, `ConfigurationPage.html`, `README.md`. Use `vX.Y.Z.W-beta1`, `-beta2`, then `-rc1` when feature-complete.

3. **Build + tag + push:**

   ```bash
   rtk dotnet build --configuration Release
   # zip + checksum as usual
   rtk git tag v5.5.12.0-beta1
   rtk git push origin v5.5.12.0-beta1
   ```

4. **Create GitHub release with `--prerelease` flag:**

   ```bash
   rtk gh release create v5.5.12.0-beta1 NotifySync.zip \
     --title "🧪 NotifySync v5.5.12.0-beta1" \
     --prerelease \
     --notes-file notes.md
   ```

   The `--prerelease` flag matters: it keeps the stable "Latest" pointer untouched.

5. **Add the beta entry to `repository-beta.json` only** (not `repository.json`). Commit on `main` so the beta feed picks it up immediately.

6. **Wait for testers.** Iterate with `-beta2`, `-beta3`, etc. on the `beta` branch as needed.

7. **Promote to stable** when ready:

   - Merge `beta` → `main` (fast-forward if possible, otherwise normal merge).
   - Cut the stable release: drop the `-betaN` suffix from versions everywhere, build a fresh ZIP, tag `v5.5.12.0`, create release **without** `--prerelease`.
   - Add the entry to `repository.json`.

## Direct-to-stable workflow (small, low-risk fixes)

For trivial bumps (CSS tweak, one-line bug fix) the beta channel is overkill. Continue with the standard:

1. Work on `main`
2. Bump version, build, zip
3. Tag `vX.Y.Z.W`, push
4. Create GitHub release (no `--prerelease`)
5. Update `repository.json`

Keep this path for the cases where you're confident in the change.

## Don'ts

- ❌ Don't add `-beta` entries to `repository.json` (stable feed) — that pushes them to every user.
- ❌ Don't tag a stable version on the `beta` branch — promote via merge first, then tag on `main`.
- ❌ Don't forget the `--prerelease` flag on `gh release create` for betas — without it, the stable "Latest" pointer shifts and Jellyfin's catalog will surface the beta.
- ❌ Don't ship a beta with a placeholder ZIP — every beta still needs to be installable (testers depend on it).

## What goes in beta vs stable

Beta is for:
- New features that need real-world testing (e.g., Phase B classification)
- Refactors that touch the DB schema or event handlers
- UI changes you want feedback on before everyone sees them
- Anything you'd label *"this could regress something subtle"*

Stable accepts:
- Bug fixes after beta validation
- Tiny CSS/text fixes
- Security patches (skip beta if the fix is urgent)
- Documentation updates (README, etc.) — these don't need a release at all most of the time
