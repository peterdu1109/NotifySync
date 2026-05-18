<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.6.0.0-blue" alt="Version">
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

> 📖 **Changelog & version history:** see [GitHub Releases](https://github.com/peterdu1109/NotifySync/releases) for what's new in each version.

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
*   **Netflix-Style Design** — A sleek glass-morphism dropdown. The red counter clears as soon as you open the bell, but NEW / UPD badges stay visible for 72 hours.
*   **Hero Section** — The latest addition is displayed large with its backdrop image. One click jumps straight to the content.
*   **Smart Grouping** — Episodes grouped by series (with season-aware summaries like `S1-S5 — 120 episodes`), tracks grouped by album.
*   **Smart Upgrade Detection** — When you replace a media file, the notification comes back with a blue **UPD** badge that tells you *what* changed: **Quality**, **Codec**, **Audio**, or any combination. Subtitle additions and metadata refreshes stay silent.
*   **Category Filters & Dismiss** — Quick filters (All / Movies / Series / Music / custom). Dismiss with a click or swipe-left on mobile, per-item or per-group.
*   **Collection Monitoring** — Track Jellyfin collections (BoxSets) for new additions.
*   **Deletion History** — Admin log of recently deleted media with configurable retention.
*   **Synced Across Devices** — Read / unread status saved server-side, in sync across all your devices.
*   **Clear List (Safe)** — Clearing notifications never marks anything as "Played" in Jellyfin.
*   **Bilingual & Responsive** — French / English following your Jellyfin language. Works on desktop, tablet, and mobile (*not supported on TV*).

### 🚀 Performance
*   **Real-Time Updates** — Notifications appear instantly via Jellyfin's WebSockets — no page refresh.
*   **HTTP Caching** — `ETag` / `If-None-Match` skips transfers when nothing changed.
*   **Lazy Image Loading** — Thumbnails load as you scroll, keeping the interface snappy.

### 🛡️ Security & Privacy
*   **Respects Jellyfin Permissions** — Each user only sees content from libraries they have access to (tags, ratings, folders all enforced).
*   **Safe Against Attacks** — All content sanitized before display; all SQL parameterized.
*   **User Isolation** — No user can access another user's notifications or data.

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
| **Unauthorized content visible** | Plugin respects Jellyfin permissions — check user restrictions in the dashboard. |
| **429 Error** | Wait 30 seconds between "Regenerate history" clicks (anti-spam). |
| **Incompatible** | Ensure you are running Jellyfin **10.11.X**. |

## 👥 Authors & Credits

NotifySync is maintained by:

- **[ElieMFR](https://github.com/ElieMFR)** — Original author & project lead
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-maintainer

Contributions, bug reports, and feature requests are welcome on [GitHub](https://github.com/peterdu1109/NotifySync).

Released under the [MIT License](./LICENSE).

---

<a name="français"></a>
# Français

NotifySync transforme l'interface Jellyfin en y ajoutant une cloche de notifications native, inspirée des grandes plateformes de streaming. Vos utilisateurs voient instantanément les derniers ajouts — Films, Séries, Musique — sans jamais quitter leur page, via un menu déroulant en verre dépoli qui s'intègre naturellement dans Jellyfin.

> 📖 **Changelog & historique des versions :** voir les [Releases GitHub](https://github.com/peterdu1109/NotifySync/releases) pour les nouveautés de chaque version.

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
*   **Design Netflix-Style** — Un menu déroulant élégant en verre dépoli. Le compteur rouge disparaît dès l'ouverture, mais les badges NOUVEAU / MAJ restent visibles 72 heures.
*   **Section Hero** — Le dernier ajout s'affiche en grand avec son image de fond. Un clic mène directement au contenu.
*   **Regroupement Intelligent** — Épisodes groupés par série (avec synthèse par saison du type `S1-S5 — 120 épisodes`), pistes groupées par album.
*   **Détection Intelligente des Mises à Jour** — Quand vous remplacez un fichier média, la notification revient avec un badge bleu **MAJ** qui indique *ce qui* a changé : **Qualité**, **Codec**, **Audio**, ou n'importe quelle combinaison. Les ajouts de sous-titres et rafraîchissements de métadonnées restent silencieux.
*   **Filtres & Suppression** — Filtres rapides (Tout / Films / Séries / Musique / personnalisé). Supprimez d'un clic ou par **swipe gauche** sur mobile, à l'unité ou par groupe.
*   **Surveillance des Collections** — Suivez vos collections Jellyfin (BoxSets) pour les nouveaux ajouts.
*   **Historique des Suppressions** — Journal admin des médias récemment supprimés, avec rétention paramétrable.
*   **Synchronisé entre Appareils** — État lu / non-lu sauvegardé côté serveur, en sync sur tous vos appareils.
*   **Vider la Liste (Sans Risque)** — Effacer les notifications ne marque rien comme "Vu" dans Jellyfin.
*   **Bilingue & Responsive** — Français / Anglais selon la langue de votre Jellyfin. Fonctionne sur bureau, tablette et mobile (*non supporté sur TV*).

### 🚀 Performance
*   **Mises à Jour en Temps Réel** — Les notifications apparaissent instantanément via les WebSockets de Jellyfin — aucun rafraîchissement nécessaire.
*   **Cache HTTP** — `ETag` / `If-None-Match` évite les transferts quand rien n'a changé.
*   **Chargement Progressif des Images** — Les miniatures se chargent au fil du défilement pour garder l'interface fluide.

### 🛡️ Sécurité & Confidentialité
*   **Respecte les Permissions Jellyfin** — Chaque utilisateur ne voit que le contenu des bibliothèques auxquelles il a accès (tags, classifications, dossiers tous respectés).
*   **Protection Contre les Attaques** — Tout le contenu est nettoyé avant affichage ; toutes les requêtes SQL sont paramétrées.
*   **Isolation des Utilisateurs** — Aucun utilisateur ne peut accéder aux notifications d'un autre.

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
| **Contenu non autorisé visible** | Le plugin respecte les permissions Jellyfin — vérifiez les restrictions utilisateur. |
| **Erreur 429** | Attendez 30 secondes entre chaque clic sur "Régénérer l'historique" (anti-spam). |
| **Incompatible** | Vérifiez que vous utilisez Jellyfin **10.11.X**. |

## 👥 Auteurs & Crédits

NotifySync est maintenu par :

- **[ElieMFR](https://github.com/ElieMFR)** — Auteur original & lead du projet
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-mainteneur

Contributions, rapports de bugs et demandes de fonctionnalités sont les bienvenus sur [GitHub](https://github.com/peterdu1109/NotifySync).

Distribué sous [Licence MIT](./LICENSE).
