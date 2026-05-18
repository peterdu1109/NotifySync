<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.5.12.6--beta7-orange" alt="Version">
  <img src="https://img.shields.io/badge/.NET-9.0-purple" alt=".NET Framework">
  <img src="https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet" alt="Jellyfin">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
  <img src="https://img.shields.io/github/stars/peterdu1109/NotifySync?style=flat&color=yellow" alt="GitHub stars">
  <img src="https://img.shields.io/github/downloads/peterdu1109/NotifySync/total?color=brightgreen" alt="Downloads">
  <img src="https://img.shields.io/github/release-date/peterdu1109/NotifySync?color=orange" alt="Last release">
</p>

<p align="center">
  <b>The modern notification center Jellyfin has been waiting for.</b><br>
  <i>Le centre de notifications moderne que Jellyfin attendait.</i>
</p>

<!-- SCREENSHOT_PLACEHOLDER: drop a bell-preview.gif here to showcase the dropdown in action -->

---

<p align="center">
  <b>🌐 Language / Langue</b><br>
  <a href="#english">English</a> | <a href="#français">Français</a>
</p>

---

<a name="english"></a>
# English

NotifySync transforms the Jellyfin interface by adding a native notification bell, inspired by major streaming platforms. Your users instantly see the latest additions — Movies, Series, Music — without ever leaving their current page, through a sleek glass-morphism dropdown that feels like it was always part of Jellyfin.

## 📑 Table of Contents

- [🚀 Quick Start](#-quick-start)
- [✨ Key Features](#-key-features)
- [📦 Installation](#-installation)
- [⚙️ Configuration](#️-configuration)
- [❓ Troubleshooting](#-troubleshooting)
- [👥 Authors & Credits](#-authors--credits)

## 🚀 Quick Start

1. Add the repository in Jellyfin: `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
2. Install **NotifySync** from the Catalog → restart Jellyfin.
3. Install the **File Transformation** plugin to inject the bell automatically (see [Enable the Bell](#-enable-the-bell)).

That's it. The bell appears in the top-right header and starts populating as new content is added.

## ✨ Key Features

### 🎨 User Experience
*   **Netflix-Style Design** — A sleek dropdown with a frosted glass look. The red counter disappears as soon as you open the bell, but NEW/UPD badges stay visible for 72 hours so you can always spot recent additions.
*   **Hero Section** — The latest addition is displayed large with its backdrop image. One click takes you straight to the content. You can also dismiss it directly.
*   **Smart Grouping** — Episodes of the same series are grouped together (e.g. *"3 new episodes"*). Music tracks are grouped by album.
*   **Group Dismiss** — Click ✕ on a grouped notification to dismiss all its items (whole series at once).
*   **Category Filters** — Quick filters (All / Movies / Series / Music / custom) to focus on what matters to you.
*   **Dismiss Notifications** — Remove any notification with a click, or **swipe left** on mobile. The footer button adapts to your filter: clear everything, or just the selected category.
*   **Smart Upgrade Detection** — When you replace a media file, the notification comes back to the top with a blue **UPD** badge instead of **NEW**, and tells you *what* changed: **Quality** (resolution or source tier), **Codec** (re-encode), **Audio** (new audio track added), or any combination (e.g. **Q+C+A** when all three change at once). Subtitle additions, metadata refreshes, and file moves with the same release name stay silent — no badge noise.
*   **Bell Pulse** — When new content arrives while the bell is closed, it pulses to grab attention (throttled to once every 30 seconds).
*   **Collection Monitoring** — Track your Jellyfin collections (BoxSets): new additions to a monitored collection trigger a notification.
*   **Deletion History** — Administrators can see a log of recently deleted media in the configuration page, with configurable retention.
*   **Synced Across Devices** — Read/unread status is saved on the server and stays in sync across all your browsers and devices.
*   **Clear List (Safe)** — Clear all notifications without affecting your Jellyfin watch history — nothing gets marked as "Played".
*   **Bilingual Interface** — The bell and config page follow your Jellyfin language (French / English).
*   **Relative Timestamps** — Dates show as *"2 hours ago"*, *"yesterday"*, *"3 days ago"*.
*   **Theme Song Filtering** — Openings, endings, and NCOP/NCED are automatically excluded.
*   **Responsive** — Works on desktop, tablet, and mobile (via the official Jellyfin app). *Note: Not supported on TV.*

### 🚀 Performance
*   **Real-Time Updates** — Notifications appear instantly thanks to Jellyfin's built-in WebSockets. No page refresh needed.
*   **HTTP Cache + ETag** — The plugin uses `If-None-Match` to skip transfers when nothing changed, saving bandwidth and CPU.
*   **Lazy Image Loading** — Thumbnails load as you scroll, keeping the interface snappy even with many notifications.
*   **Lightweight Storage** — All data is stored in a fast SQLite database (WAL mode) optimized for concurrent access.
*   **Optimized for .NET 9** — Built for the latest Jellyfin runtime for maximum performance.

### 🛡️ Security & Privacy
*   **Respects Jellyfin Permissions** — Each user only sees content from the libraries they have access to. All restrictions (tags, ratings, folders) are enforced.
*   **User Isolation** — No user can access another user's notifications or data. Admins can manage all users.
*   **Safe Against Attacks** — All content is sanitized before display, and all database queries are protected against injection.
*   **Anti-Spam** — 30-second cooldown on manual history regeneration to protect server resources.
*   **Crash-Safe** — Data is written safely to prevent corruption even if the server stops unexpectedly.

## 📦 Installation

> [!IMPORTANT]
> **Prerequisites:**
> * Jellyfin **10.11.X**

### Steps
1.  Open your Jellyfin dashboard > **Plugins** > **Repositories**.
2.  Add a new repository:
    ```
    https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json
    ```
3.  Go to the **Catalog**, find **NotifySync** and click **Install**.
4.  Restart your Jellyfin server.

> [!NOTE]
> **🧪 Want to test upcoming features?** Add the beta channel alongside the stable one:
> ```
> https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository-beta.json
> ```
> Beta releases are pre-release versions — install at your own risk. The stable channel above remains your safe default.

### 🔔 Enable the Bell

> [!TIP]
> **Method 1: File Transformation (Highly Recommended) ✅**
> Install the File Transformation plugin for automatic injection — no file editing required, and **survives Jellyfin updates**:
> 1.  Add repository: `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
> 2.  Install **File Transformation**.
> 3.  Restart Jellyfin → `Ctrl+F5`.

#### Method 2: Manual Injection

> [!WARNING]
> This method requires re-applying the patch **after every Jellyfin update**, because Jellyfin overwrites `index.html`. Method 1 is strongly preferred.

If you prefer not to install another plugin, manually add the script tag to your `index.html`:

| Platform | Command |
|----------|---------|
| **Linux** | `sudo sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /usr/share/jellyfin/web/index.html` |
| **Docker** | `docker exec jellyfin sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /jellyfin/jellyfin-web/index.html` |
| **Windows** | Add `<script src="/NotifySync/client.js"></script>` before `</body>` in `C:\Program Files\Jellyfin\Server\jellyfin-web\index.html` |

## ⚙️ Configuration

Go to **Dashboard > Plugins > NotifySync**.

| Setting | Default | Description |
|---------|---------|-------------|
| **Quotas** | `10` | Maximum number of items to display per category (1–50). |
| **Monitored Libraries** | *(none)* | Check the folders you want to appear in notifications. |
| **Monitored Collections** | *(none)* | Select Jellyfin collections (BoxSets) to track for new additions. |
| **Category Mapping** | *(empty)* | Rename your libraries for display in the bell (e.g. *"My Movies"* → *"Movies"*). |
| **Manual Library IDs** | *(empty)* | Add library IDs or names manually for advanced setups (Channels, XFusion). |
| **Deleted Items Tracking** | `enabled` | Enable/disable the deletion history log. **Required for full UPD detection** on Sonarr/Radarr replacements (delete+re-import scenario). |
| **Deletion Retention** | `30 days` | Number of days to keep deleted item records (1–365). |
| **Regenerate History** | — | Force a full rescan after changing libraries or quotas. |

## ❓ Troubleshooting

| Issue | Solution |
|-------|----------|
| **Bell doesn't appear** | Check **File Transformation** is installed. Restart Jellyfin. Clear browser cache (`Ctrl+Shift+R`). |
| **Bell appeared then disappeared after Jellyfin update** | You're using Method 2 (manual injection) — re-apply the `sed` command. Switch to Method 1 (File Transformation) to avoid this. |
| **Badge count is wrong** | Click "Regenerate history" in config. Clear browser localStorage. |
| **Music not synced** | Use "Regenerate history" to rescan audio tracks. |
| **Content missing** | Ensure the library is checked in "Monitored Libraries". |
| **New TV episode missing** | Make sure the **Series-type library** containing the episode is checked in "Monitored Libraries". |
| **Replaced file shows NEW instead of UPD** | Enable **Deleted Items Tracking** in config — required for the delete+re-import detection path. |
| **UPD sub-label is wrong or missing** | The classifier uses filename keywords (`2160p`, `BluRay`, `HEVC`, `VFF`, etc.). Setups with non-standard naming may land on plain `UPD` with no sub-label. Open a GitHub issue with example filenames to extend the patterns. |
| **Unauthorized content visible** | Plugin respects Jellyfin permissions — check user restrictions in the dashboard. |
| **429 Error** | Wait 30 seconds between "Regenerate history" clicks (anti-spam). |
| **Incompatible** | Ensure you are running Jellyfin **10.11.X**. |

## 👥 Authors & Credits

NotifySync is maintained by:

- **[ElieM](https://github.com/ElieM)** — Original author & project lead
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-maintainer

Contributions, bug reports, and feature requests are welcome on [GitHub](https://github.com/peterdu1109/NotifySync).

Released under the [MIT License](./LICENSE).

---

<a name="français"></a>
# Français

NotifySync transforme l'interface Jellyfin en y ajoutant une cloche de notifications native, inspirée des grandes plateformes de streaming. Vos utilisateurs voient instantanément les derniers ajouts — Films, Séries, Musique — sans jamais quitter leur page, via un menu déroulant en verre dépoli qui s'intègre naturellement dans Jellyfin.

## 📑 Sommaire

- [🚀 Démarrage Rapide](#-démarrage-rapide)
- [✨ Fonctionnalités](#-fonctionnalités)
- [📦 Installation](#-installation-1)
- [⚙️ Configuration](#️-configuration-1)
- [❓ Dépannage](#-dépannage)
- [👥 Auteurs & Crédits](#-auteurs--crédits)

## 🚀 Démarrage Rapide

1. Ajoutez le dépôt dans Jellyfin : `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
2. Installez **NotifySync** depuis le Catalogue → redémarrez Jellyfin.
3. Installez le plugin **File Transformation** pour injecter la cloche automatiquement (voir [Activer la Cloche](#-activer-la-cloche)).

C'est tout. La cloche apparaît en haut à droite de l'interface et se remplit au fur et à mesure des nouveaux ajouts.

## ✨ Fonctionnalités

### 🎨 Expérience Utilisateur
*   **Design Netflix-Style** — Un menu déroulant élégant avec effet de verre dépoli. Le compteur rouge disparaît dès l'ouverture de la cloche, mais les badges NOUVEAU/MAJ restent visibles pendant 72 heures pour repérer facilement les nouveautés.
*   **Section Hero** — Le dernier ajout s'affiche en grand avec son image de fond. Un clic mène directement au contenu. Vous pouvez aussi le supprimer directement.
*   **Regroupement Intelligent** — Les épisodes d'une même série sont groupés (ex. *"3 nouveaux épisodes"*). Les musiques sont groupées par album.
*   **Suppression Groupée** — Cliquez sur ✕ sur une notification groupée pour supprimer tous ses éléments d'un coup (une série entière, par exemple).
*   **Filtres par Catégorie** — Des filtres rapides (Tout / Films / Séries / Musique / personnalisé) pour cibler ce qui vous intéresse.
*   **Supprimer des Notifications** — Retirez une notification d'un clic, ou **glissez vers la gauche** sur mobile. Le bouton en bas s'adapte à votre filtre : vider tout, ou seulement la catégorie sélectionnée.
*   **Détection Intelligente des Mises à Jour** — Quand vous remplacez un fichier média, la notification remonte en haut avec un badge bleu **MAJ** au lieu de **NOUVEAU**, et vous indique *ce qui* a changé : **Qualité** (palier de résolution ou source), **Codec** (ré-encodage), **Audio** (nouvelle piste audio ajoutée), ou n'importe quelle combinaison (par ex. **Q+C+A** quand les trois changent en même temps). Les ajouts de sous-titres, rafraîchissements de métadonnées et déplacements de fichier avec le même nom restent silencieux — pas de bruit de badge.
*   **Pulse de la Cloche** — Quand un nouveau contenu arrive alors que la cloche est fermée, elle pulse pour attirer l'attention (limité à une fois toutes les 30 secondes).
*   **Surveillance des Collections** — Suivez vos collections Jellyfin (BoxSets) : les nouveaux ajouts dans une collection surveillée déclenchent une notification.
*   **Historique des Suppressions** — Les administrateurs peuvent consulter les médias récemment supprimés dans la page de configuration, avec rétention paramétrable.
*   **Synchronisé entre Appareils** — L'état lu/non-lu est sauvegardé sur le serveur et reste synchronisé sur tous vos navigateurs et appareils.
*   **Vider la Liste (Sans Risque)** — Effacez toutes les notifications sans toucher à votre historique Jellyfin — rien n'est marqué comme "Vu".
*   **Interface Bilingue** — La cloche et la page de configuration suivent la langue de votre Jellyfin (Français / Anglais).
*   **Horodatage Relatif** — Les dates s'affichent sous forme *"il y a 2 heures"*, *"hier"*, *"il y a 3 jours"*.
*   **Filtrage des Génériques** — Les openings, endings et NCOP/NCED sont automatiquement exclus.
*   **Responsive** — Fonctionne sur bureau, tablette et mobile (via l'app officielle Jellyfin). *Note : Non supporté sur TV.*

### 🚀 Performance
*   **Mises à Jour en Temps Réel** — Les notifications apparaissent instantanément grâce aux WebSockets intégrés de Jellyfin. Aucun rafraîchissement nécessaire.
*   **Cache HTTP + ETag** — Le plugin utilise `If-None-Match` pour éviter les transferts quand rien n'a changé, économisant bande passante et CPU.
*   **Chargement Progressif des Images** — Les miniatures se chargent au fil du défilement pour garder l'interface fluide.
*   **Stockage Léger** — Toutes les données sont stockées dans une base SQLite rapide (mode WAL) optimisée pour les accès simultanés.
*   **Optimisé pour .NET 9** — Conçu pour la dernière version de Jellyfin, pour des performances maximales.

### 🛡️ Sécurité & Confidentialité
*   **Respecte les Permissions Jellyfin** — Chaque utilisateur ne voit que le contenu des bibliothèques auxquelles il a accès. Toutes les restrictions (tags, classifications, dossiers) sont respectées.
*   **Isolation des Utilisateurs** — Aucun utilisateur ne peut accéder aux notifications d'un autre. Les administrateurs peuvent gérer tous les utilisateurs.
*   **Protection Contre les Attaques** — Tout le contenu est nettoyé avant affichage, et toutes les requêtes sont protégées contre les injections.
*   **Anti-Spam** — Cooldown de 30 secondes sur la régénération manuelle pour protéger les ressources serveur.
*   **Résistant aux Crashs** — Les données sont écrites de manière sécurisée pour éviter toute corruption même en cas d'arrêt inattendu.

## 📦 Installation

> [!IMPORTANT]
> **Pré-requis :**
> * Jellyfin **10.11.X**

### Étapes
1.  Tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez un nouveau dépôt :
    ```
    https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json
    ```
3.  Allez dans le **Catalogue**, trouvez **NotifySync** et cliquez sur **Installer**.
4.  Redémarrez votre serveur Jellyfin.

> [!NOTE]
> **🧪 Envie de tester les fonctionnalités à venir ?** Ajoute le canal beta à côté du canal stable :
> ```
> https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository-beta.json
> ```
> Les versions beta sont des pré-releases — à installer à tes risques. Le canal stable ci-dessus reste ton choix sûr par défaut.

### 🔔 Activer la Cloche

> [!TIP]
> **Méthode 1 : File Transformation (Recommandé) ✅**
> Injection automatique sans modification de fichier, et **survit aux mises à jour de Jellyfin** :
> 1.  Dépôt : `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
> 2.  Installez **File Transformation** et redémarrez Jellyfin → `Ctrl+F5`.

#### Méthode 2 : Injection Manuelle

> [!WARNING]
> Cette méthode nécessite de **ré-appliquer le patch après chaque mise à jour Jellyfin**, car Jellyfin écrase `index.html`. La Méthode 1 est fortement recommandée.

Si vous préférez ne pas installer d'extension tierce :

| Plateforme | Commande |
|------------|----------|
| **Linux** | `sudo sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /usr/share/jellyfin/web/index.html` |
| **Docker** | `docker exec jellyfin sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /jellyfin/jellyfin-web/index.html` |
| **Windows** | Ajoutez `<script src="/NotifySync/client.js"></script>` avant `</body>` dans `index.html` |

## ⚙️ Configuration

Allez dans **Tableau de bord > Extensions > NotifySync**.

| Paramètre | Défaut | Description |
|-----------|--------|-------------|
| **Quotas** | `10` | Nombre maximum d'éléments affichés par catégorie (1–50). |
| **Bibliothèques Surveillées** | *(aucune)* | Cochez les dossiers que vous souhaitez voir apparaître dans la cloche. |
| **Collections Surveillées** | *(aucune)* | Sélectionnez les collections Jellyfin (BoxSets) à surveiller pour les nouveaux ajouts. |
| **Mappage des Catégories** | *(vide)* | Renommez vos bibliothèques pour l'affichage (ex. *"Mes Films"* → *"Films"*). |
| **IDs Manuels** | *(vide)* | Ajoutez des IDs ou noms de bibliothèques manuellement pour les configurations avancées (Channels, XFusion). |
| **Suivi des Suppressions** | `activé` | Activer/désactiver le journal des suppressions. **Requis pour la détection MAJ complète** sur les remplacements Sonarr/Radarr (scénario delete+ré-import). |
| **Rétention des Suppressions** | `30 jours` | Nombre de jours de conservation des éléments supprimés (1–365). |
| **Régénérer l'Historique** | — | Force un scan complet après modification des bibliothèques ou quotas. |

## ❓ Dépannage

| Problème | Solution |
|----------|----------|
| **La cloche n'apparaît pas** | Vérifiez que **File Transformation** est installé. Redémarrez Jellyfin. Videz le cache (`Ctrl+Shift+R`). |
| **La cloche a disparu après mise à jour Jellyfin** | Vous utilisez la Méthode 2 (injection manuelle) — ré-appliquez la commande `sed`. Passez à la Méthode 1 (File Transformation) pour éviter ça. |
| **Le badge est incorrect** | Cliquez sur "Régénérer l'historique". Videz le localStorage du navigateur. |
| **Musique non synchronisée** | Utilisez "Régénérer l'historique" pour rescanner les pistes audio. |
| **Contenu manquant** | Vérifiez que la bibliothèque est cochée dans "Bibliothèques Surveillées". |
| **Nouvel épisode TV manquant** | Vérifiez que la **bibliothèque de type Série** contenant l'épisode est cochée dans "Bibliothèques Surveillées". |
| **Fichier remplacé apparaît en NOUVEAU au lieu de MAJ** | Activez le **Suivi des Suppressions** dans la config — nécessaire pour la détection du scénario delete+ré-import. |
| **Sous-label MAJ incorrect ou absent** | Le classificateur utilise les keywords du filename (`2160p`, `BluRay`, `HEVC`, `VFF`, etc.). Les setups avec un naming non-standard peuvent tomber sur `MAJ` tout court sans sous-label. Ouvrez une issue GitHub avec des exemples de noms de fichiers pour étendre les patterns. |
| **Contenu non autorisé visible** | Le plugin respecte les permissions Jellyfin — vérifiez les restrictions utilisateur. |
| **Erreur 429** | Attendez 30 secondes entre chaque clic sur "Régénérer l'historique" (anti-spam). |
| **Incompatible** | Vérifiez que vous utilisez Jellyfin **10.11.X**. |

## 👥 Auteurs & Crédits

NotifySync est maintenu par :

- **[ElieM](https://github.com/ElieM)** — Auteur original & lead du projet
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-mainteneur

Contributions, rapports de bugs et demandes de fonctionnalités sont les bienvenus sur [GitHub](https://github.com/peterdu1109/NotifySync).

Distribué sous [Licence MIT](./LICENSE).
