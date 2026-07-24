<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.7.17.0-blue" alt="Version">
  <img src="https://img.shields.io/badge/.NET-9.0-purple" alt=".NET Framework">
  <img src="https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet" alt="Jellyfin">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
  <img src="https://img.shields.io/github/stars/peterdu1109/NotifySync?style=flat&color=yellow&cacheSeconds=3600" alt="GitHub stars">
  <img src="https://img.shields.io/github/downloads/peterdu1109/NotifySync/total?color=brightgreen&cacheSeconds=3600" alt="Downloads">
  <img src="https://img.shields.io/github/release-date/peterdu1109/NotifySync?color=orange&cacheSeconds=3600" alt="Last release">
</p>

<p align="center">
  <b>The modern notification center Jellyfin has been waiting for.</b><br>
  <i>Le centre de notifications moderne que Jellyfin attendait.</i>
</p>

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
- [🎨 Recommended Theme](#-recommended-theme)
- [👥 Authors & Credits](#-authors--credits)

## 🚀 Quick Start

1. Add the repository in Jellyfin: `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
2. Install **NotifySync** from the Catalog → restart Jellyfin.
3. Install the **File Transformation** plugin to inject the bell automatically (see [Enable the Bell](#-enable-the-bell)).

That's it. The bell appears in the top-right header and starts populating as new content is added.

## ✨ Key Features

*   **Streaming-style bell** — A sleek glass dropdown with the latest addition as a large hero spotlight. The red counter clears when you open the bell; each item keeps a NEW / UPD badge until you dismiss it or it's played.
*   **Real-time** — Notifications appear instantly when content is added. No page refresh, ever.
*   **Organized by recency** — The list is split into **Today**, **This week**, and **Earlier** sections, newest first.
*   **Smart grouping** — Episodes grouped by series and labeled with the exact range that landed (`Season 4 • Ep. 1-12`, or `Ep. 1, 8` for a partial batch); tracks grouped by album.
*   **Upgrade detection** — Replace a media file and the notification comes back with a blue **UPD** badge telling you *what* changed: **Quality**, **Codec**, **Audio**, or a combination. Subtitle additions and metadata refreshes stay silent.
*   **Favorites filter** — Favorite a series in Jellyfin and filter the bell down to just the content you care about.
*   **Filters & dismiss** — Quick category pills, dismiss per item or per group, swipe-left on mobile. Clearing the list never marks anything as "Played".
*   **Collections** — Monitored Jellyfin collections (BoxSets) notify you on new additions.
*   **Deletion history** — Admins get a log of recently deleted media, with configurable retention.
*   **Synced across devices** — Read and cleared states live on the server: phone, desktop, and TV browser always agree.
*   **Respects permissions** — Every user only ever sees content from libraries they can access, with strict per-user isolation.
*   **Bilingual & responsive** — French / English following your Jellyfin language, on desktop, tablet, and mobile (*not supported on TV apps*).

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

## 🧪 Jellyfin 12.0 (Preview)

A preview build of NotifySync runs on **Jellyfin 12.0** (from the `jellyfin-12` branch). The bell, real-time updates and admin page are fully functional — but the install differs from the stable 10.11 build.

> [!IMPORTANT]
> - Requires Jellyfin **12.0 rc3** or newer.
> - This is a **preview** — not for production, and expect rough edges.

### Steps
1.  In Jellyfin: **Dashboard → Plugins → Repositories** → add:
    ```
    https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/jellyfin-12/repository-12.json
    ```
2.  Go to the **Catalog**, install **NotifySync**, and restart Jellyfin.
3.  **Enable the bell — manual injection is required.** The File Transformation plugin has no Jellyfin 12 build yet, so add the script tag before `</body>` in the web client's `index.html`. On the official server build it lives at `/jellyfin/jellyfin-web/index.html`:
    ```
    <script src="/NotifySync/client.js"></script>
    ```

> [!WARNING]
> The manual injection must be **re-applied after every Jellyfin update** (Jellyfin overwrites `index.html`). Automatic injection returns once File Transformation ships a Jellyfin 12 build.

## ⚙️ Configuration

Go to **Dashboard > Plugins > NotifySync**.

| Setting | Default | Description |
|---------|---------|-------------|
| **Quotas** | `10` | Maximum number of **entries** per category (1–50). A whole series counts as one entry regardless of episode count (up to 500 episodes stored per series). |
| **Monitored Libraries** | *(none)* | Check the folders you want to appear in notifications. |
| **Monitored Collections** | *(none)* | Select Jellyfin collections (BoxSets) to track for new additions. Scanned every 15 minutes. |
| **Category Mapping** | *(empty)* | Rename your libraries for display in the bell (e.g. *"My Movies"* → *"Movies"*). |
| **Manual Library IDs** | *(empty)* | Add library IDs or names manually for advanced setups (Channels, XFusion). |
| **Deleted Items Tracking** | `enabled` | Enable/disable the deletion history log. **Required for full UPD detection** on Sonarr/Radarr replacements (delete+re-import scenario). Replacements are correlated within a 7-day window. |
| **Deletion Retention** | `30 days` | Number of days to keep deleted item records (7–365). The lower bound is 7 days so the detection window always has data — retention only controls how long the deletions stay visible in the admin log. |
| **Regenerate History** | — | Force a full rescan after changing libraries or quotas. |
| **Scan Collections Now** | — | Force an immediate scan of monitored Collections instead of waiting for the automatic interval. |

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
| **A renamed file shows a false UPD badge** | UPD detection is **filename-based**. A plain rename of the same file is now suppressed automatically (identical file size). A false UPD can only slip through if the rename also changes the file's content/size, or for items added before v5.7.9 (no stored size) — just dismiss it. |
| **Unauthorized content visible** | Plugin respects Jellyfin permissions — check user restrictions in the dashboard. |
| **429 Error** | Wait 30 seconds between "Regenerate history" clicks (anti-spam). |
| **Incompatible** | Ensure you are running Jellyfin **10.11.X**. |

## 🎨 Recommended Theme

NotifySync integrates beautifully with the [**ElegantFin Cinema Edition**](https://github.com/peterdu1109/ElegantFin) theme — a streaming-inspired CSS theme for Jellyfin with a full suite of cinematic modules, including dedicated styling for the NotifySync notification bell.

Both projects work flawlessly together on **PC (browser)** and **mobile (Jellyfin app + browser)**.

> ⭐ Install once, get a complete cinematic Jellyfin experience with real-time notifications.
>
> ⚠️ **TV note**: NotifySync currently doesn't render on TV apps (Samsung Tizen, Android TV, LG webOS). The notification bell is a web client feature and TV apps use native rendering.

## 👥 Authors & Credits

NotifySync is maintained by:

- **[ElieMFR](https://github.com/ElieMFR)** — Original author & project lead
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-maintainer

Thanks to **[@muisje](https://github.com/muisje)** and **[@Wernouxe](https://github.com/Wernouxe)** for their feature requests and bug reports that shaped this plugin.

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
- [🎨 Thème recommandé](#-thème-recommandé)
- [👥 Auteurs & Crédits](#-auteurs--crédits)

## 🚀 Démarrage Rapide

1. Ajoutez le dépôt dans Jellyfin : `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
2. Installez **NotifySync** depuis le Catalogue → redémarrez Jellyfin.
3. Installez le plugin **File Transformation** pour injecter la cloche automatiquement (voir [Activer la Cloche](#-activer-la-cloche)).

C'est tout. La cloche apparaît en haut à droite de l'interface et se remplit au fur et à mesure des nouveaux ajouts.

## ✨ Fonctionnalités

*   **Cloche style streaming** — Un menu déroulant élégant en verre dépoli, avec le dernier ajout en grand en vedette. Le compteur rouge disparaît à l'ouverture ; chaque élément garde un badge NOUVEAU / MAJ jusqu'à ce que vous le retiriez ou qu'il soit marqué vu.
*   **Temps réel** — Les notifications apparaissent instantanément quand un contenu est ajouté. Aucun rafraîchissement, jamais.
*   **Organisé par fraîcheur** — La liste est découpée en sections **Aujourd'hui**, **Cette semaine** et **Plus ancien**, du plus récent au plus ancien.
*   **Regroupement intelligent** — Épisodes groupés par série avec la plage exacte qui est arrivée (`Saison 4 • Ép. 1-12`, ou `Ép. 1, 8` pour un lot partiel) ; pistes groupées par album.
*   **Détection des mises à jour** — Remplacez un fichier média et la notification revient avec un badge bleu **MAJ** qui indique *ce qui* a changé : **Qualité**, **Codec**, **Audio**, ou une combinaison. Les ajouts de sous-titres et rafraîchissements de métadonnées restent silencieux.
*   **Filtre Favoris** — Mettez une série en favori dans Jellyfin et filtrez la cloche sur les seuls contenus qui vous intéressent.
*   **Filtres & suppression** — Pills de catégories, suppression à l'unité ou par groupe, swipe gauche sur mobile. Vider la liste ne marque jamais rien comme « Vu ».
*   **Collections** — Les collections Jellyfin surveillées (BoxSets) vous notifient des nouveaux ajouts.
*   **Historique des suppressions** — Les admins disposent d'un journal des médias récemment supprimés, avec rétention paramétrable.
*   **Synchronisé entre appareils** — États lu / vidé stockés côté serveur : téléphone, bureau et navigateur TV toujours d'accord.
*   **Respecte les permissions** — Chaque utilisateur ne voit que le contenu des bibliothèques auxquelles il a accès, avec une isolation stricte par utilisateur.
*   **Bilingue & responsive** — Français / Anglais selon la langue de votre Jellyfin, sur bureau, tablette et mobile (*non supporté sur les apps TV*).

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

## 🧪 Jellyfin 12.0 (Aperçu)

Une version aperçu de NotifySync tourne sur **Jellyfin 12.0** (branche `jellyfin-12`). La cloche, le temps réel et la page d'admin sont pleinement fonctionnels — mais l'installation diffère de la version stable 10.11.

> [!IMPORTANT]
> - Nécessite Jellyfin **12.0 rc3** ou plus récent.
> - C'est un **aperçu** — pas pour la production, quelques aspérités à prévoir.

### Étapes
1.  Dans Jellyfin : **Tableau de bord → Extensions → Dépôts** → ajoutez :
    ```
    https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/jellyfin-12/repository-12.json
    ```
2.  Allez dans le **Catalogue**, installez **NotifySync**, puis redémarrez Jellyfin.
3.  **Activer la cloche — l'injection manuelle est requise.** Le plugin File Transformation n'a pas encore de build Jellyfin 12 : ajoutez la balise script avant `</body>` dans le `index.html` du client web. Sur le serveur officiel il se trouve à `/jellyfin/jellyfin-web/index.html` :
    ```
    <script src="/NotifySync/client.js"></script>
    ```

> [!WARNING]
> L'injection manuelle doit être **réappliquée après chaque mise à jour de Jellyfin** (Jellyfin écrase `index.html`). L'injection automatique reviendra dès que File Transformation publiera un build Jellyfin 12.

## ⚙️ Configuration

Allez dans **Tableau de bord > Extensions > NotifySync**.

| Paramètre | Défaut | Description |
|-----------|--------|-------------|
| **Quotas** | `10` | Nombre maximum d'**entrées** par catégorie (1–50). Une série entière compte pour une seule entrée, quel que soit le nombre d'épisodes (jusqu'à 500 épisodes stockés par série). |
| **Bibliothèques Surveillées** | *(aucune)* | Cochez les dossiers que vous souhaitez voir apparaître dans la cloche. |
| **Collections Surveillées** | *(aucune)* | Sélectionnez les collections Jellyfin (BoxSets) à surveiller pour les nouveaux ajouts. Scannées toutes les 15 minutes. |
| **Mappage des Catégories** | *(vide)* | Renommez vos bibliothèques pour l'affichage (ex. *"Mes Films"* → *"Films"*). |
| **IDs Manuels** | *(vide)* | Ajoutez des IDs ou noms de bibliothèques manuellement pour les configurations avancées (Channels, XFusion). |
| **Suivi des Suppressions** | `activé` | Activer/désactiver le journal des suppressions. **Requis pour la détection MAJ complète** sur les remplacements Sonarr/Radarr (scénario delete+ré-import). Les remplacements sont corrélés dans une fenêtre de 7 jours. |
| **Rétention des Suppressions** | `30 jours` | Nombre de jours de conservation des éléments supprimés (7–365). La borne basse est de 7 jours pour que la fenêtre de détection ait toujours des données — la rétention ne fait que prolonger l'affichage dans le journal admin. |
| **Régénérer l'Historique** | — | Force un scan complet après modification des bibliothèques ou quotas. |
| **Scanner les Collections** | — | Force un scan immédiat des Collections surveillées au lieu d'attendre l'intervalle automatique. |

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
| **Un fichier renommé affiche un faux badge MAJ** | La détection MAJ est **basée sur le nom de fichier**. Un simple renommage du même fichier est maintenant supprimé automatiquement (taille identique). Un faux MAJ ne peut subsister que si le renommage change aussi le contenu/la taille, ou pour les éléments ajoutés avant la v5.7.9 (taille non stockée) — retirez la notification. |
| **Contenu non autorisé visible** | Le plugin respecte les permissions Jellyfin — vérifiez les restrictions utilisateur. |
| **Erreur 429** | Attendez 30 secondes entre chaque clic sur "Régénérer l'historique" (anti-spam). |
| **Incompatible** | Vérifiez que vous utilisez Jellyfin **10.11.X**. |

## 🎨 Thème recommandé

NotifySync s'intègre parfaitement avec le thème [**ElegantFin Cinema Edition**](https://github.com/peterdu1109/ElegantFin) — un thème CSS inspiré des plateformes de streaming pour Jellyfin avec une suite complète de modules cinématographiques, dont un styling dédié pour la cloche de notifications NotifySync.

Les deux projets fonctionnent ensemble sur **PC (navigateur)** et **mobile (app Jellyfin + navigateur)**.

> ⭐ Installation unique, expérience Jellyfin cinéma complète avec notifications temps réel.
>
> ⚠️ **Note TV** : NotifySync ne s'affiche actuellement pas sur les apps TV (Samsung Tizen, Android TV, LG webOS). La cloche est une fonctionnalité du client web et les apps TV utilisent un rendu natif.

## 👥 Auteurs & Crédits

NotifySync est maintenu par :

- **[ElieMFR](https://github.com/ElieMFR)** — Auteur original & lead du projet
- **[Peterdu1109](https://github.com/peterdu1109)** — Co-mainteneur

Merci à **[@muisje](https://github.com/muisje)** et **[@Wernouxe](https://github.com/Wernouxe)** pour leurs demandes de fonctionnalités et rapports de bugs qui ont façonné ce plugin.

Contributions, rapports de bugs et demandes de fonctionnalités sont les bienvenus sur [GitHub](https://github.com/peterdu1109/NotifySync).

Distribué sous [Licence MIT](./LICENSE).
