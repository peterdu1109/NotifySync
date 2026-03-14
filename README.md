<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.4.2.0-blue" alt="Version">
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

## 🖼️ Preview / Aperçu

<div align="center">
  <table>
    <tr>
      <td width="300">
        <video src="https://github.com/user-attachments/assets/147190ed-3d3c-4974-8ca1-979c753c8ec2" controls></video>
      </td>
    </tr>
  </table>
</div>

---

<a name="english"></a>
# English

NotifySync transforms the Jellyfin interface by adding a native notification bell, inspired by major streaming platforms. Your users instantly see the latest additions — Movies, Series, Music — without ever leaving their current page, through a sleek glass-morphism dropdown that feels like it was always part of Jellyfin.

## ✨ Key Features

### 🎨 User Experience
*   **Netflix-Style Design** — Seamless glass-morphism dropdown with backdrop blur. The red badge disappears the moment you open the bell, just by acknowledging the updates.
*   **Hero Section** — The most recent addition is showcased with a large backdrop image, title, and relative timestamp. One click navigates directly to the item.
*   **Smart Grouping** — Episodes of the same series are automatically grouped (e.g. *"3 new episodes"*). Music tracks are grouped by album (e.g. *"5 new tracks"*).
*   **Category Filters** — Filter pills (All / Movies / Series / Music / custom) let you instantly focus on what matters.
*   **Individual Dismiss** — Dismiss any notification with a single click — smooth slide-out animation, persisted per-user on the server.
*   **Server-Side Read/Unread State** — Read status is stored on the server and synced across all browsers and devices. No more localStorage dependency.
*   **Bell Pulse Animation** — The bell shakes when new content arrives via WebSocket. Debounced to avoid visual spam.
*   **Clear List (Safe)** — One button to clear all notifications without ever marking media as "Played" in your Jellyfin history.
*   **Automatic i18n** — The interface (bell + config page) follows the user's Jellyfin language setting (French / English), with browser language fallback.
*   **Relative Timestamps** — All dates are displayed as *"2 hours ago"*, *"yesterday"*, *"3 days ago"* using the browser's locale.
*   **Theme Song Filtering** — Automatically excludes openings, endings, NCOP/NCED, and theme songs from notifications.
*   **Responsive** — Adapts to desktop, tablet, and mobile (via official Jellyfin app). *Note: Not supported on TV.*

### 🚀 Performance
*   **Real-Time WebSockets** — Instant updates via native Jellyfin WebSockets. No polling, no delays.
*   **ETag / 304 Caching** — Zero bandwidth when nothing has changed. The client sends its ETag, the server returns 304 Not Modified.
*   **Per-User RAM Cache** — Serialized responses are cached in memory per user. Subsequent requests skip serialization entirely.
*   **Lazy Image Loading** — Thumbnails are loaded on-demand via IntersectionObserver as you scroll through the list.
*   **SQLite WAL** — Notification data is persisted in a WAL-mode SQLite database for fast concurrent reads.
*   **Event Buffering** — Library events (ItemAdded, ItemUpdated) are debounced and batch-processed to avoid DB contention.
*   **Optimized for .NET 9** — TieredPGO, AOT-compatible JSON serialization, aggressive inlining on hot paths.

### 🛡️ Security & Privacy
*   **Strict Permissions** — Each user only sees content from libraries they have access to. Tags, ratings, and folder restrictions are fully respected.
*   **Anti-IDOR** — Every API call verifies the authenticated user matches the requested userId. Admins can access all users.
*   **XSS Protection** — All user-generated content and item IDs are HTML-escaped before DOM injection.
*   **Anti-Spam** — 30-second cooldown on manual history regeneration to protect server resources.
*   **Atomic Writes** — Cleared state is written to a temp file then renamed, preventing corruption on crash.
*   **Parameterized SQL** — All database queries use parameterized statements to prevent SQL injection.

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
| **Quotas** | Maximum number of items to display per category. |
| **Monitored Libraries** | Check the folders you want to appear in notifications. |
| **Category Mapping** | Rename your libraries for display in the bell (e.g. *"My Movies"* → *"Movies"*). |
| **Manual Library IDs** | Add library IDs or names manually for advanced setups (Channels, XFusion). |
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
*   **Design Netflix-Style** — Menu déroulant en glass-morphism avec flou d'arrière-plan. La pastille rouge disparait instantanément dès l'ouverture de la cloche, par simple prise de connaissance.
*   **Section Hero** — Le dernier ajout est mis en avant avec une grande image de fond, son titre et un horodatage relatif. Un clic mène directement à la page du contenu.
*   **Regroupement Intelligent** — Les épisodes d'une même série sont automatiquement groupés (ex. *"3 nouveaux épisodes"*). Les pistes musicales sont groupées par album (ex. *"5 nouvelles pistes"*).
*   **Filtres par Catégorie** — Des filtres rapides (Tout / Films / Séries / Musique / personnalisé) permettent de cibler instantanément ce qui vous intéresse.
*   **Suppression Individuelle** — Supprimez n'importe quelle notification d'un simple clic — animation fluide de glissement, persisté par utilisateur côté serveur.
*   **État Lu/Non-lu Côté Serveur** — Le statut de lecture est stocké sur le serveur et synchronisé entre tous les navigateurs et appareils. Fini la dépendance au localStorage.
*   **Animation Pulse** — La cloche s'anime quand un nouveau contenu arrive via WebSocket. Anti-spam visuel intégré.
*   **Vider la Liste (Sans Risque)** — Un bouton pour effacer toutes les notifications sans jamais impacter votre historique de lecture Jellyfin (les médias restent "Non vus").
*   **i18n Automatique** — L'interface (cloche + page config) suit le paramètre de langue Jellyfin de l'utilisateur (Français / Anglais), avec fallback sur la langue du navigateur.
*   **Horodatage Relatif** — Toutes les dates sont affichées sous forme *"il y a 2 heures"*, *"hier"*, *"il y a 3 jours"* selon la locale du navigateur.
*   **Filtrage des Génériques** — Exclusion automatique des openings, endings, NCOP/NCED et thèmes musicaux.
*   **Responsive** — S'adapte au bureau, tablette et mobile (via l'app officielle Jellyfin). *Note : Non supporté sur TV.*

### 🚀 Performance
*   **WebSockets Temps Réel** — Mise à jour instantanée via les WebSockets natifs Jellyfin. Aucun polling, aucun délai.
*   **Cache ETag / 304** — Zéro bande passante quand rien n'a changé. Le client envoie son ETag, le serveur renvoie 304 Not Modified.
*   **Cache RAM par Utilisateur** — Les réponses sérialisées sont mises en cache en mémoire par utilisateur. Les requêtes suivantes sautent entièrement la sérialisation.
*   **Chargement Paresseux des Images** — Les miniatures sont chargées à la demande via IntersectionObserver au fil du défilement.
*   **SQLite WAL** — Les données de notification sont persistées dans une base SQLite en mode WAL pour des lectures concurrentes rapides.
*   **Buffer d'Événements** — Les événements de bibliothèque (ItemAdded, ItemUpdated) sont regroupés et traités par lots pour éviter la contention de la base de données.
*   **Optimisé .NET 9** — TieredPGO, sérialisation JSON compatible AOT, inlining agressif sur les chemins critiques.

### 🛡️ Sécurité & Confidentialité
*   **Permissions Strictes** — Chaque utilisateur ne voit que le contenu des bibliothèques auxquelles il a accès. Tags, classifications et restrictions sont pleinement respectés.
*   **Anti-IDOR** — Chaque appel API vérifie que l'utilisateur authentifié correspond au userId demandé. Les administrateurs peuvent accéder à tous les utilisateurs.
*   **Protection XSS** — Tout le contenu et les identifiants sont échappés avant injection dans le DOM.
*   **Anti-Spam** — Cooldown de 30 secondes sur la régénération manuelle de l'historique pour protéger les ressources serveur.
*   **Écriture Atomique** — L'état "vidé" est écrit dans un fichier temporaire puis renommé, empêchant la corruption en cas de crash.
*   **SQL Paramétré** — Toutes les requêtes utilisent des paramètres pour prévenir l'injection SQL.

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
| **Quotas** | Nombre maximum d'éléments affichés par catégorie. |
| **Bibliothèques Surveillées** | Cochez les dossiers que vous souhaitez voir apparaître dans la cloche. |
| **Mappage des Catégories** | Renommez vos bibliothèques pour l'affichage (ex. *"Mes Films"* → *"Films"*). |
| **IDs Manuels** | Ajoutez des IDs ou noms de bibliothèques manuellement pour les configurations avancées (Channels, XFusion). |
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
