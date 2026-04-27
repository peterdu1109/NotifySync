# Primer — NotifySync

Brief de prise en main pour un agent Claude Code. À lire avant toute modif.

## Projet

**NotifySync** — plugin Jellyfin qui ajoute une cloche de notifications
native dans l'UI pour signaler les derniers ajouts (Films, Séries, Musique)
sans quitter la page courante.

- Version courante : `5.5.9.1` (voir `NotifySync.csproj`)
- Auteurs : Peterdu1109, ElieM
- Cible Jellyfin : `10.11.0`

## Stack

- **Backend** : C# / .NET 9 (`<TargetFramework>net9.0</TargetFramework>`)
- **API plugin** : Jellyfin.Controller / Jellyfin.Model / Jellyfin.Data
- **Stockage** : SQLite via `Microsoft.Data.Sqlite`
- **UI** : `client.js` + `ConfigurationPage.html` (embarqués comme ressources)
- **Analyseurs** : StyleCop, SerilogAnalyzer, MultithreadingAnalyzer
  (`TreatWarningsAsErrors=true`, ruleset = `jellyfin.ruleset`)

## Fichiers clés

### Bootstrap & DI
- `Plugin.cs` — entry point du plugin Jellyfin
- `NotifySyncEntryPoint.cs` — hook de démarrage
- `NotifySyncServiceRegistrator.cs` — enregistrement des services
- `PluginConfiguration.cs`, `PluginJsonContext.cs`

### Domaine
- `NotificationItem.cs`, `DeletedItemRecord.cs`
- `CategoryMapping.cs`, `CategoryQuotaService.cs`
- `IdHelper.cs`

### Cœur métier
- `NotificationManager.cs` (~55 ko) — orchestration
- `NotificationDatabase.cs` (~44 ko) — couche SQLite
- `NotifyController.cs` (~29 ko) — API HTTP
- `CollectionScanTask.cs`, `HistoryScanTask.cs` — tâches planifiées
- `NotifySyncTransformation.cs`, `FileTransformationPayload.cs` — injection UI

### UI / Config
- `client.js` (~45 ko) — script client injecté
- `ConfigurationPage.html` — page d'admin

### Distribution
- `repository.json` — manifeste du repo de plugins
- `meta.json` — métadonnées du plugin
- `NotifySync.csproj` — build & versioning

## Conventions

- Indent / line endings : voir `.editorconfig`
- Style C# : `jellyfin.ruleset` (StyleCop) — warnings = erreurs
- Versioning : bump `AssemblyVersion`, `FileVersion`, `Version` dans
  `NotifySync.csproj` puis ajouter une entrée à `repository.json`
- Messages de commit : courts, en anglais, format observé dans le log
  (« 5.5.9.1 — Perf: filter OnItemUpdated by type », « Fix theme music: … »)

## Workflow agent

- `primer.md` (ce fichier) — contexte de démarrage, à mettre à jour si la
  stack ou les fichiers clés changent
- `memory.sh` — capture rapide de notes datées vers `obsidian/memory.md`
  (usage : `./memory.sh "ce que je veux retenir"`)
- `hindsight/` — rétrospectives post-tâche, une entrée par fichier
  `YYYY-MM-DD-titre.md`
- `obsidian/` — coffre Obsidian à ouvrir comme vault pour notes longues
- `.claude/` — config locale Claude Code (gitignorée, ne pas commit)

## Points d'attention

- `TreatWarningsAsErrors=true` — un warning bloque le build
- `client.js` est embarqué comme ressource : un changement nécessite un
  rebuild ; checksum référencé dans `repository.json` à mettre à jour
- Logique de chemin Jellyfin sensible : vérifier `Emby.Page.show` et le
  format `#!/details?id=...` (cf. commits récents)
