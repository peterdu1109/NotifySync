# 🔔 NotifySync

![Dernière Version](https://img.shields.io/badge/version-4.7.16-blue)
![Net Framework](https://img.shields.io/badge/.NET-9.0-purple)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet)

**Le centre de notifications moderne que Jellyfin attendait.**

NotifySync transforme l'interface de Jellyfin en ajoutant une icône de notification (cloche) native. Il permet à vos utilisateurs de voir instantanément les derniers ajouts (Films, Séries, Musique) sans quitter leur page actuelle, le tout avec un design fluide inspiré des plateformes de streaming majeures.

---


## ✨ Fonctionnalités Principales

### 🎨 Expérience Utilisateur
*   **Design Moderne** : Intégration fluide "Netflix-Style" avec badge de nouveautés et effets visuels (Glassmorphism).
*   **Navigation Intuitive** : "Hero Section" pour les derniers ajouts et regroupement intelligent des épisodes.
*   **Synchronisation** : Indicateurs "Vu/Non vu" en temps réel avec Jellyfin.
*   **Compatibilité** : PC/Mac et Mobiles (via app officielle). *Note : Non supporté sur TV.*

### 🚀 Performance
*   **Zéro-Latence** : Système de cache RAM intelligent pour un affichage instantané.
*   **Optimisé .NET 9** : Architecture haute performance et faible consommation.
*   **Efficacité** : Gestion optimisée du réseau (ETags) et des ressources serveur.

### 🛡️ Sécurité & Confidentialité
*   **Respect des Permissions** : Isolation stricte des données (Tags, Classification, Bibliothèques).
*   **Authentification Forte** : Protection de tous les endpoints API et vérification d'identité (Anti-IDOR).
*   **Sécurité Active** : Protection XSS, Anti-Spam et écriture atomique des données.

## 📦 Installation

### 1. Pré-requis
* **Jellyfin 10.11.X**
* **.NET 9 Runtime**

### Méthode 1 : Via le Dépôt (Recommandé)
1.  Ouvrez votre tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez un nouveau dépôt :
    * **Nom :** NotifySync Repo
    * **URL :** `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Allez dans le **Catalogue**, trouvez **NotifySync** et cliquez sur **Installer**.
4.  Redémarrez votre serveur Jellyfin.

### Méthode 2 : Installation Manuelle
1.  Téléchargez le fichier `.zip` depuis la page [Releases](https://github.com/peterdu1109/NotifySync/releases/tag/v4.7.16).
2.  Décompressez la DLL dans le dossier `plugins/NotifySync` de votre serveur.
3.  Redémarrez Jellyfin.

| OS | Chemin des plugins |
| :--- | :--- |
| **Docker** | `/config/plugins/NotifySync` |
| **Linux** | `/var/lib/jellyfin/plugins/NotifySync` |
| **Windows** | `%ProgramData%\Jellyfin\Server\plugins\NotifySync` |

## Étape 2 : Activer l'Interface (Client)
⚠️ **Cette étape est obligatoire** car Jellyfin 10.11+ sécurise l'interface web.

Vous devez ajouter **une seule ligne** à votre fichier `index.html` pour charger la cloche.

### Option A : Installation Automatique (Linux/Docker) - Recommandé

Un script est fourni pour faire cette modification automatiquement.

1.  Connectez-vous à votre serveur en SSH.
2.  Téléchargez et lancez le script d'installation :
    ```bash
    curl -sL https://raw.githubusercontent.com/peterdu1109/NotifySync/main/scripts/install.sh | sudo bash
    ```
    *Le script détecte automatiquement votre installation Jellyfin (Native ou Docker).*

### Option B : Modification Manuelle

Si vous préférez le faire à la main :

1.  Accédez au dossier d'installation de l'interface web Jellyfin :
    * **Linux :** `/usr/share/jellyfin/web/index.html`
    * **Docker :** `/jellyfin/jellyfin-web/index.html` (à monter en volume ou via CLI)
    * **Windows :** `C:\Program Files\Jellyfin\Server\jellyfin-web\index.html`

2.  Ouvrez `index.html` avec un éditeur de texte.

3.  Ajoutez cette ligne tout en bas du fichier, juste **avant** la balise `</body>` :

```html
<script src="/NotifySync/Client.js" defer></script>
```

---

## ⚙️ Configuration

Allez dans **Tableau de bord > Extensions > NotifySync**.

* **Quotas** : Définissez combien d'éléments afficher par catégorie (ex: 5 films, 5 séries...).
* **Bibliothèques Surveillées** : Cochez les dossiers que vous souhaitez voir apparaître dans les notifications.
* **Mappage de Catégories** : Renommez vos bibliothèques pour l'affichage.

---

## ❓ Dépannage

| Problème | Solution |
|----------|----------|
| **La cloche n'apparaît pas** | Videz le cache du navigateur (Ctrl+Shift+R). Vérifiez que le plugin est activé dans Extensions. |
| **Le badge (chiffre) ne s'affiche pas** | Cliquez sur "Régénérer l'historique" dans la config du plugin. Videz le localStorage du navigateur. |
| **Musique non synchronisée avec l'accueil** | Allez dans Config > "Régénérer l'historique" pour rescanner les pistes Audio. |
| **Certains contenus n'apparaissent pas** | Vérifiez que la bibliothèque est cochée dans "Bibliothèques Surveillées". |
| **Contenus visibles par un utilisateur non autorisé** | Le plugin respecte les permissions Jellyfin. Vérifiez les restrictions de l'utilisateur dans Jellyfin. |
| **Erreur 429 lors du rafraîchissement** | Attendez 1 minute entre chaque clic sur "Régénérer l'historique" (protection anti-spam). |
| **Plugin incompatible après mise à jour Jellyfin** | Vérifiez que vous utilisez Jellyfin 10.11.X avec .NET 9 Runtime. |
