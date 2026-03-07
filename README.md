# 🔔 NotifySync

![Dernière Version](https://img.shields.io/badge/version-5.2.0.0-blue)
![Net Framework](https://img.shields.io/badge/.NET-9.0-purple)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet)

**Le centre de notifications moderne que Jellyfin attendait.**

NotifySync transforme l'interface de Jellyfin en ajoutant une icône de notification (cloche) native. Il permet à vos utilisateurs de voir instantanément les derniers ajouts (Films, Séries, Musique) sans quitter leur page actuelle, le tout avec un design fluide inspiré des plateformes de streaming majeures.

---


## ✨ Fonctionnalités Principales

### 🎨 Expérience Utilisateur
*   **Design Moderne** : Intégration fluide "Netflix-Style" avec badge de nouveautés et effets visuels (Glassmorphism).
*   **Navigation Intuitive** : "Hero Section" pour les derniers ajouts et regroupement intelligent des épisodes.
<<<<<<< HEAD
*   **Synchronisation Intelligente** : Indicateurs "Vu/Non vu" en temps réel avec le serveur Jellyfin (un clic sur "Tout marquer comme lu" coche instantanément vos médias dans la base).
=======
*   **Synchronisation Intelligente** : Indicateurs "Vu/Non vu" en temps réel avec le serveur Jellyfin (les médias lus disparaissent automatiquement de la cloche) et un bouton "Tout marquer comme lu" qui synchronise la base de données native.
>>>>>>> 357fa6edf8e29c765a263b63e0fe1c89fd0fda50
*   **Filtrage Avancé** : Exclusion automatique des génériques (OP/ED), thèmes musicaux et respect strict des bibliothèques actives.
*   **Compatibilité** : PC/Mac et Mobiles (via app officielle). *Note : Non supporté sur TV.*

### 🚀 Performance
*   **Temps Réel Absolu** : Mise à jour instantanée via WebSockets natifs Jellyfin (plus de polling 60s).
*   **Zéro-Latence** : Système de cache RAM intelligent pour un affichage instantané.
*   **Optimisé .NET 9** : Architecture haute performance et faible consommation.
*   **Efficacité** : Gestion optimisée du réseau (ETags) et limitation stricte et saine de la base de données intégrée pour empêcher la saturation.

### 🛡️ Sécurité & Confidentialité
*   **Respect des Permissions** : Isolation stricte des données (Tags, Classification, Bibliothèques).
*   **Authentification Forte** : Protection de tous les endpoints API et vérification d'identité (Anti-IDOR).
*   **Sécurité Active** : Protection XSS, Anti-Spam et écriture atomique des données.

## 📦 Installation

### 1. Pré-requis
* **Jellyfin 10.11.X**
* **.NET 9 Runtime**

### Installation via le Dépôt NotifySync
1.  Ouvrez votre tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez un nouveau dépôt :
    * **Nom :** NotifySync Repo
    * **URL :** `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Allez dans le **Catalogue**, trouvez **NotifySync** et cliquez sur **Installer**.
4.  Redémarrez votre serveur Jellyfin.

## Étape 2 : Activer la Cloche

### Méthode recommandée : File Transformation (automatique, tous OS) ✅
Installez le plugin **File Transformation** — NotifySync détectera sa présence et injectera automatiquement la cloche :
1.  **Tableau de bord** > **Extensions** > **Dépôts** → ajoutez : `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
2.  Installez **File Transformation** depuis le Catalogue
3.  Redémarrez Jellyfin → `Ctrl+F5`

> 💡 Aucune modification de fichier nécessaire ! Fonctionne sur Linux, Docker et Windows.

### Alternative : Injection manuelle (une seule fois)
Si vous ne souhaitez pas installer File Transformation, ajoutez manuellement le script :

**Linux** :
```bash
sudo sed -i 's|</body>|    <script src="/NotifySync/client.js"></script>\n</body>|' /usr/share/jellyfin/web/index.html
sudo systemctl restart jellyfin
```

**Docker** :
```bash
docker exec jellyfin sed -i 's|</body>|    <script src="/NotifySync/client.js"></script>\n</body>|' /jellyfin/jellyfin-web/index.html
docker restart jellyfin
```

**Windows** : Ouvrez `C:\Program Files\Jellyfin\Server\jellyfin-web\index.html` en admin, ajoutez avant `</body>` :
```html
<script src="/NotifySync/client.js"></script>
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
| **La cloche n'apparaît pas** | Vérifiez que **File Transformation** est installé, ou que le script a été ajouté manuellement dans `index.html`. Videz le cache navigateur (`Ctrl+Shift+R`). |
| **Le badge (chiffre) ne s'affiche pas** | Cliquez sur "Régénérer l'historique" dans la config du plugin. Videz le localStorage du navigateur. |
| **Musique non synchronisée avec l'accueil** | Allez dans Config > "Régénérer l'historique" pour rescanner les pistes Audio. |
| **Certains contenus n'apparaissent pas** | Vérifiez que la bibliothèque est cochée dans "Bibliothèques Surveillées". |
| **Contenus visibles par un utilisateur non autorisé** | Le plugin respecte les permissions Jellyfin. Vérifiez les restrictions de l'utilisateur dans Jellyfin. |
| **Erreur 429 lors du rafraîchissement** | Attendez 1 minute entre chaque clic sur "Régénérer l'historique" (protection anti-spam). |
| **Plugin incompatible après mise à jour Jellyfin** | Vérifiez que vous utilisez Jellyfin 10.11.X avec .NET 9 Runtime. |
