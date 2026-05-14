<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.5.10.0-blue" alt="Version">
  <img src="https://img.shields.io/badge/.NET-9.0-purple" alt=".NET Framework">
  <img src="https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet" alt="Jellyfin">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
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

## ✨ Key Features

### 🎨 User Experience
*   **Netflix-Style Design** — A sleek dropdown with a frosted glass look. The red counter disappears as soon as you open the bell, but NEW/UPD badges stay visible for 72 hours so you can always spot recent additions.
*   **Hero Section** — The latest addition is displayed large with its backdrop image. One click takes you straight to the content. You can also dismiss it directly.
*   **Smart Grouping** — Episodes of the same series are grouped together (e.g. *"3 new episodes"*). Music tracks are grouped by album.
*   **Category Filters** — Quick filters (All / Movies / Series / Music / custom) to focus on what matters to you.
*   **Dismiss Notifications** — Remove any notification with a click, or swipe it left on mobile. The button at the bottom adapts to your filter: clear everything, or just the selected category.
*   **Smart Upgrade Detection** — When you replace a media file, the notification comes back to the top with a blue **UPD** badge instead of **NEW**, and tells you *what* changed: **Quality** (real upgrade), **Codec** (re-encode), **Audio** (dubbing added), or **Minor** (subtitle / metadata refresh). No more guessing whether it's worth re-watching.
*   **Collection Monitoring** — Track your Jellyfin collections (BoxSets): new additions to a monitored collection trigger a notification.
*   **Deletion History** — Administrators can see a log of recently deleted media in the configuration page, with configurable retention.
*   **Synced Across Devices** — Read/unread status is saved on the server and stays in sync across all your browsers and devices.
*   **Bell Animation** — The bell shakes when new content arrives, so you never miss an update.
*   **Clear List (Safe)** — Clear all notifications without affecting your Jellyfin watch history — nothing gets marked as "Played".
*   **Bilingual Interface** — The bell and config page follow your Jellyfin language (French / English).
*   **Relative Timestamps** — Dates show as *"2 hours ago"*, *"yesterday"*, *"3 days ago"*.
*   **Theme Song Filtering** — Openings, endings, and NCOP/NCED are automatically excluded.
*   **Responsive** — Works on desktop, tablet, and mobile (via the official Jellyfin app). *Note: Not supported on TV.*

### 🚀 Performance
*   **Real-Time Updates** — Notifications appear instantly thanks to Jellyfin's built-in WebSockets. No page refresh needed.
*   **Smart Caching** — The plugin only transfers data when something has actually changed, saving bandwidth and keeping things fast.
*   **Lazy Image Loading** — Thumbnails load as you scroll, keeping the interface snappy even with many notifications.
*   **Lightweight Storage** — All data is stored in a fast SQLite database optimized for concurrent access.
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

### 🔔 Enable the Bell

> [!TIP]
> **Method 1: File Transformation (Highly Recommended) ✅**
> Install the File Transformation plugin for automatic injection — no file editing required:
> 1.  Add repository: `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
> 2.  Install **File Transformation**.
> 3.  Restart Jellyfin → `Ctrl+F5`.

#### Method 2: Manual Injection
If you prefer not to install another plugin, manually add the script tag to your `index.html`:

| Platform | Command |
|----------|---------|
| **Linux** | `sudo sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /usr/share/jellyfin/web/index.html` |
| **Docker** | `docker exec jellyfin sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /jellyfin/jellyfin-web/index.html` |
| **Windows** | Add `<script src="/NotifySync/client.js"></script>` before `</body>` in `C:\Program Files\Jellyfin\Server\jellyfin-web\index.html` |

## ⚙️ Configuration

Go to **Dashboard > Plugins > NotifySync**.

| Setting | Description |
|---------|-------------|
| **Quotas** | Maximum number of items to display per category (1–50). |
| **Monitored Libraries** | Check the folders you want to appear in notifications. |
| **Monitored Collections** | Select Jellyfin collections (BoxSets) to track for new additions. |
| **Category Mapping** | Rename your libraries for display in the bell (e.g. *"My Movies"* → *"Movies"*). |
| **Manual Library IDs** | Add library IDs or names manually for advanced setups (Channels, XFusion). |
| **Deleted Items Tracking** | Enable/disable the deletion history log (admin only). |
| **Deletion Retention** | Number of days to keep deleted item records (1–365, default 30). |
| **Regenerate History** | Force a full rescan after changing libraries or quotas. |

## ❓ Troubleshooting

| Issue | Solution |
|-------|----------|
| **Bell doesn't appear** | Check **File Transformation** is installed. Clear browser cache (`Ctrl+Shift+R`). |
| **Badge count is wrong** | Click "Regenerate history" in config. Clear browser localStorage. |
| **Music not synced** | Use "Regenerate history" to rescan audio tracks. |
| **Content missing** | Ensure the library is checked in "Monitored Libraries". |
| **Unauthorized content visible** | Plugin respects Jellyfin permissions — check user restrictions in the dashboard. |
| **429 Error** | Wait 30 seconds between "Regenerate history" clicks (anti-spam). |
| **Incompatible** | Ensure you are running Jellyfin **10.11.X**. |

---

<a name="français"></a>
# Français

NotifySync transforme l'interface Jellyfin en y ajoutant une cloche de notifications native, inspirée des grandes plateformes de streaming. Vos utilisateurs voient instantanément les derniers ajouts — Films, Séries, Musique — sans jamais quitter leur page, via un menu déroulant en verre dépoli qui s'intègre naturellement dans Jellyfin.

## ✨ Fonctionnalités

### 🎨 Expérience Utilisateur
*   **Design Netflix-Style** — Un menu déroulant élégant avec effet de verre dépoli. Le compteur rouge disparaît dès l'ouverture de la cloche, mais les badges NOUVEAU/MAJ restent visibles pendant 72 heures pour repérer facilement les nouveautés.
*   **Section Hero** — Le dernier ajout s'affiche en grand avec son image de fond. Un clic mène directement au contenu. Vous pouvez aussi le supprimer directement.
*   **Regroupement Intelligent** — Les épisodes d'une même série sont groupés (ex. *"3 nouveaux épisodes"*). Les musiques sont groupées par album.
*   **Filtres par Catégorie** — Des filtres rapides (Tout / Films / Séries / Musique / personnalisé) pour cibler ce qui vous intéresse.
*   **Supprimer des Notifications** — Retirez une notification d'un clic, ou glissez-la vers la gauche sur mobile. Le bouton en bas s'adapte à votre filtre : vider tout, ou seulement la catégorie sélectionnée.
*   **Détection Intelligente des Mises à Jour** — Quand vous remplacez un fichier média, la notification remonte en haut avec un badge bleu **MAJ** au lieu de **NOUVEAU**, et vous indique *ce qui* a changé : **Qualité** (vraie amélioration), **Codec** (ré-encodage), **Audio** (doublage ajouté), ou **Mineur** (sous-titre / refresh metadata). Plus besoin de deviner si ça vaut la peine de re-regarder.
*   **Surveillance des Collections** — Suivez vos collections Jellyfin (BoxSets) : les nouveaux ajouts dans une collection surveillée déclenchent une notification.
*   **Historique des Suppressions** — Les administrateurs peuvent consulter les médias récemment supprimés dans la page de configuration, avec rétention paramétrable.
*   **Synchronisé entre Appareils** — L'état lu/non-lu est sauvegardé sur le serveur et reste synchronisé sur tous vos navigateurs et appareils.
*   **Animation de la Cloche** — La cloche s'anime quand un nouveau contenu arrive, pour ne rien manquer.
*   **Vider la Liste (Sans Risque)** — Effacez toutes les notifications sans toucher à votre historique Jellyfin — rien n'est marqué comme "Vu".
*   **Interface Bilingue** — La cloche et la page de configuration suivent la langue de votre Jellyfin (Français / Anglais).
*   **Horodatage Relatif** — Les dates s'affichent sous forme *"il y a 2 heures"*, *"hier"*, *"il y a 3 jours"*.
*   **Filtrage des Génériques** — Les openings, endings et NCOP/NCED sont automatiquement exclus.
*   **Responsive** — Fonctionne sur bureau, tablette et mobile (via l'app officielle Jellyfin). *Note : Non supporté sur TV.*

### 🚀 Performance
*   **Mises à Jour en Temps Réel** — Les notifications apparaissent instantanément grâce aux WebSockets intégrés de Jellyfin. Aucun rafraîchissement nécessaire.
*   **Cache Intelligent** — Le plugin ne transfère des données que lorsqu'il y a un réel changement, économisant la bande passante.
*   **Chargement Progressif des Images** — Les miniatures se chargent au fil du défilement pour garder l'interface fluide.
*   **Stockage Léger** — Toutes les données sont stockées dans une base rapide optimisée pour les accès simultanés.
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

### 🔔 Activer la Cloche

> [!TIP]
> **Méthode 1 : File Transformation (Recommandé) ✅**
> Injection automatique sans modification de fichier :
> 1.  Dépôt : `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
> 2.  Installez **File Transformation** et redémarrez Jellyfin → `Ctrl+F5`.

#### Méthode 2 : Injection Manuelle
Si vous préférez ne pas installer d'extension tierce :

| Plateforme | Commande |
|------------|----------|
| **Linux** | `sudo sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /usr/share/jellyfin/web/index.html` |
| **Docker** | `docker exec jellyfin sed -i 's\|</body>\|    <script src="/NotifySync/client.js"></script>\n</body>\|' /jellyfin/jellyfin-web/index.html` |
| **Windows** | Ajoutez `<script src="/NotifySync/client.js"></script>` avant `</body>` dans `index.html` |

## ⚙️ Configuration

Allez dans **Tableau de bord > Extensions > NotifySync**.

| Paramètre | Description |
|-----------|-------------|
| **Quotas** | Nombre maximum d'éléments affichés par catégorie (1–50). |
| **Bibliothèques Surveillées** | Cochez les dossiers que vous souhaitez voir apparaître dans la cloche. |
| **Collections Surveillées** | Sélectionnez les collections Jellyfin (BoxSets) à surveiller pour les nouveaux ajouts. |
| **Mappage des Catégories** | Renommez vos bibliothèques pour l'affichage (ex. *"Mes Films"* → *"Films"*). |
| **IDs Manuels** | Ajoutez des IDs ou noms de bibliothèques manuellement pour les configurations avancées (Channels, XFusion). |
| **Suivi des Suppressions** | Activer/désactiver le journal des suppressions (admin uniquement). |
| **Rétention des Suppressions** | Nombre de jours de conservation des éléments supprimés (1–365, défaut 30). |
| **Régénérer l'Historique** | Force un scan complet après modification des bibliothèques ou quotas. |

## ❓ Dépannage

| Problème | Solution |
|----------|----------|
| **La cloche n'apparaît pas** | Vérifiez que **File Transformation** est installé. Videz le cache (`Ctrl+Shift+R`). |
| **Le badge est incorrect** | Cliquez sur "Régénérer l'historique". Videz le localStorage du navigateur. |
| **Musique non synchronisée** | Utilisez "Régénérer l'historique" pour rescanner les pistes audio. |
| **Contenu manquant** | Vérifiez que la bibliothèque est cochée dans "Bibliothèques Surveillées". |
| **Contenu non autorisé visible** | Le plugin respecte les permissions Jellyfin — vérifiez les restrictions utilisateur. |
| **Erreur 429** | Attendez 30 secondes entre chaque clic sur "Régénérer l'historique" (anti-spam). |
| **Incompatible** | Vérifiez que vous utilisez Jellyfin **10.11.X**. |
