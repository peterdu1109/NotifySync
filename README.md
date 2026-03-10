<h1 align="center">🔔 NotifySync</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-5.3.0.0-blue" alt="Version">
  <img src="https://img.shields.io/badge/.NET-9.0-purple" alt=".NET Framework">
  <img src="https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet" alt="Jellyfin">
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

NotifySync transforms the Jellyfin interface by adding a native notification icon (bell). It allows your users to instantly see the latest additions (Movies, Series, Music) without leaving their current page, all with a fluid design inspired by major streaming platforms.

### 🖼️ Preview
<p align="center">
  <a href="docs/screenshots/notifysync_demo.mp4">
    <img src="docs/screenshots/dropdown_preview.png" alt="Watch Video Demo" width="600">
    <br>
    <b>▶️ Click to watch the video demo</b>
  </a>
</p>

## ✨ Key Features

### 🎨 User Experience
*   **Modern Design**: Seamless "Netflix-Style" integration with a "New" badge and glassmorphism visual effects.
*   **Intuitive Navigation**: "Hero Section" for latest additions and smart episode grouping.
*   **Smart Sync**: Real-time "Seen/Unseen" indicators. Read media automatically disappears from the bell.
*   **Advanced Filtering**: Automatic exclusion of theme songs (OP/ED).
*   **Compatibility**: PC/Mac and Mobile (via official app). *Note: Not supported on TV.*

### 🚀 Performance
*   **Absolute Real-Time**: Instant updates via native Jellyfin WebSockets (no more polling).
*   **Zero-Latency**: Smart RAM cache system for instant display.
*   **Optimized for .NET 9**: High performance and low memory footprint.
*   **Efficiency**: Network optimized (ETags) and database protection to prevent saturation.

### 🛡️ Security & Privacy
*   **Permissions**: Strict data isolation (Tags, Rating, Libraries).
*   **Strong Authentication**: API endpoint protection and identity verification (Anti-IDOR).
*   **Active Security**: XSS protection, Anti-Spam, and atomic data writing.

## 📦 Installation

### 1. Prerequisites
* **Jellyfin 10.11.X**
* **.NET 9 Runtime**

### 2. Steps
1.  Open your Jellyfin dashboard > **Plugins** > **Repositories**.
2.  Add a new repository: `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Go to the **Catalog**, find **NotifySync** and click **Install**.
4.  Restart your Jellyfin server.

### 🔔 Enable the Bell

#### Method 1: File Transformation (Highly Recommended) ✅
Install the **File Transformation** plugin for automatic injection (No file editing required):
1.  Add repository: `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
2.  Install **File Transformation**.
3.  Restart Jellyfin → `Ctrl+F5`.

#### Method 2: Manual Injection
If you don't want to install another plugin, manually add the script to your `index.html`:
- **Linux**: `sudo sed -i 's|</body>|    <script src="/NotifySync/client.js"></script>\n</body>|' /usr/share/jellyfin/web/index.html`
- **Docker**: `docker exec jellyfin sed -i 's|</body>|    <script src="/NotifySync/client.js"></script>\n</body>|' /jellyfin/jellyfin-web/index.html`
- **Windows**: Add `<script src="/NotifySync/client.js"></script>` before `</body>` in `C:\Program Files\Jellyfin\Server\jellyfin-web\index.html`.

## ⚙️ Configuration
Go to **Dashboard > Plugins > NotifySync**.
* **Quotas**: Define how many items to display per category.
* **Monitored Libraries**: Check the folders you want to see in notifications.
* **Category Mapping**: Rename your libraries for display.
* **Maintenance**: Click "Regenerate history" after changing libraries to force a scan.

## ❓ Troubleshooting
| Issue | Solution |
|----------|----------|
| **Bell doesn't appear** | Check **File Transformation** or manual script. Clear browser cache (`Ctrl+Shift+R`). |
| **Badge count missing** | Click "Regenerate history" in config. Clear browser localStorage. |
| **Music not synced** | Use "Regenerate history" in config to rescan audio tracks. |
| **Content missing** | Ensure the library is checked in "Monitored Libraries". |
| **Unauthorized content** | Plugin respects Jellyfin permissions. Check user restrictions. |
| **429 Error** | Wait 1 minute between "Regenerate history" clicks (anti-spam). |
| **Incompatible** | Ensure you are on Jellyfin 10.11.X with .NET 9. |

---

<a name="français"></a>
# Français

NotifySync transforme l'interface de Jellyfin en ajoutant une icône de notification (cloche) native. Il permet à vos utilisateurs de voir instantanément les derniers ajouts (Films, Séries, Musique) sans quitter leur page actuelle.

## 🖼️ Aperçu
<p align="center">
  <a href="docs/screenshots/notifysync_demo.mp4">
    <img src="docs/screenshots/dropdown_preview.png" alt="Voir la démo vidéo" width="600">
    <br>
    <b>▶️ Cliquez pour voir la démo vidéo</b>
  </a>
</p>


### 🎨 Expérience Utilisateur
*   **Design Moderne** : Intégration fluide "Netflix-Style" avec badge de nouveautés et effets visuels (Glassmorphism).
*   **Navigation Intuitive** : "Hero Section" pour les derniers ajouts et regroupement intelligent des épisodes.
*   **Synchronisation Intelligente** : Indicateurs "Vu/Non vu" en temps réel. Les médias lus disparaissent automatiquement.
*   **Filtrage Avancé** : Exclusion automatique des génériques (OP/ED).
*   **Compatibilité** : PC/Mac et Mobiles. *Note : Non supporté sur TV.*

### 🚀 Performance
*   **Temps Réel Absolu** : Mise à jour instantanée via WebSockets natifs Jellyfin.
*   **Zéro-Latence** : Système de cache RAM intelligent pour un affichage instantané.
*   **Optimisé .NET 9** : Architecture haute performance et faible consommation.
*   **Efficacité** : Gestion optimisée du réseau (ETags) et protection de la base de données.

### 🛡️ Sécurité & Confidentialité
*   **Respect des Permissions** : Isolation stricte des données (Tags, Classification, Bibliothèques).
*   **Authentification Forte** : Protection des endpoints API et vérification d'identité (Anti-IDOR).
*   **Sécurité Active** : Protection XSS, Anti-Spam et écriture atomique.

## 📦 Installation

### 1. Pré-requis
* **Jellyfin 10.11.X**
* **.NET 9 Runtime**

### 2. Étapes
1.  Tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez : `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Installez **NotifySync** et redémarrez Jellyfin.

### 🔔 Activer la Cloche
#### Méthode 1 : File Transformation (Recommandé) ✅
Injection automatique sans modification de fichier :
1.  Dépôt : `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
2.  Installez **File Transformation** et redémarrez Jellyfin → `Ctrl+F5`.

#### Méthode 2 : Injection Manuelle
Si vous ne voulez pas d'extension tierce :
- **Linux/Docker** : `sudo sed -i 's|</body>|    <script src="/NotifySync/client.js"></script>\n</body>|' /usr/share/jellyfin/web/index.html`
- **Windows** : Ajoutez `<script src="/NotifySync/client.js"></script>` avant `</body>` dans `index.html`.

## ⚙️ Configuration
Allez dans **Tableau de bord > Extensions > NotifySync**.
* **Quotas** : Nombre d'éléments par catégorie.
* **Bibliothèques Surveillées** : Dossiers à inclure dans la cloche.
* **Maintenance** : Cliquez sur "Régénérer l'historique" après avoir changé les bibliothèques.

## ❓ Dépannage
| Problème | Solution |
|----------|----------|
| **La cloche n'apparaît pas** | Vérifiez **File Transformation**. Videz le cache (`Ctrl+Shift+R`). |
| **Badge absent** | Cliquez sur "Régénérer l'historique". Videz le localStorage. |
| **Musique non synchro** | Utilisez "Régénérer l'historique" pour rescanner les pistes Audio. |
| **Contenu manquant** | Vérifiez que la bibliothèque est cochée dans la configuration. |
| **Contenu non autorisé** | Le plugin respecte les permissions Jellyfin. |
| **Erreur 429** | Attendez 1 minute entre chaque clic sur "Régénérer l'historique". |
| **Incompatible** | Vérifiez Jellyfin 10.11.X et .NET 9. |
