# 🔔 NotifySync

![Dernière Version](https://img.shields.io/badge/version-4.6.9-blue)
![Net Framework](https://img.shields.io/badge/.NET-9.0-purple)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.X-blueviolet)

**Le centre de notifications moderne que Jellyfin attendait.**

NotifySync transforme l'interface de Jellyfin en ajoutant une icône de notification (cloche) native. Il permet à vos utilisateurs de voir instantanément les derniers ajouts (Films, Séries, Musique) sans quitter leur page actuelle, le tout avec un design fluide inspiré des plateformes de streaming majeures.

---

## ✨ Fonctionnalités

### 🎨 Expérience Utilisateur Premium
* **Design "Netflix-Style" :** Intégration transparente d'une cloche avec badge de nouveautés.
* **Interface Moderne :** Menu déroulant avec effet de flou ("Glassmorphism"), animations fluides et chargement différé des images (Lazy Loading).
* **Hero Section :** Mise en avant visuelle du contenu le plus récent en haut de la liste.
* **Regroupement Intelligent :** Fini le spam ! Les épisodes d'une même saison sont regroupés (ex: *"S01 • 3 nouveaux épisodes"*).
* **Support Complet :** Compatible avec les **Films**, **Séries** et **Albums de Musique**.
* **Indicateurs de lecture :** Synchronisation en temps réel avec le statut "Vu" de Jellyfin.
* **📱 Compatibilité :** Fonctionne sur PC (Windows/Linux) & Mac et applications mobiles (Android/Iphone).<br>(Note : Ne fonctionne pas sur les interfaces TV comme Android TV, Apple TV, Tizen, etc).

### 🚀 Performance
* **Zéro-Latence :** Architecture de cache "Per-User". Les notifications sont servies instantanément depuis le cache RAM, sans recalcul, tant que le contenu ne change pas sur le serveur.
* **.NET 9 Native :** Utilisation intensive de `FrozenSet` et `System.Threading.Lock` pour une rapidité extrême.
* **Optimisation Réseau :** ETags intelligents qui évitent tout retéléchargement inutile par les clients.
* **Moteur optimisé :** Algorithmes O(1) pour la résolution des bibliothèques parentes.

### 🛡️ Sécurité & Confidentialité
* **Respect des Permissions (Privacy) :** Isolation stricte via le moteur Jellyfin ("Core Engine Isolation"). Utilisation de `InternalItemsQuery` pour garantir qu'un utilisateur ne verra **jamais** de contenu non autorisé (par Tags, Classification, ou Librairie).
* **🔒 Authentification obligatoire** (**Nouveau v4.6.9**) : Tous les endpoints API sont protégés par `[Authorize]`. L'authentification Jellyfin est requise pour accéder aux données.
* **🛡️ Protection IDOR** (**Nouveau v4.6.9**) : Vérification que l'utilisateur authentifié correspond à l'utilisateur demandé. Un utilisateur ne peut pas accéder aux notifications d'un autre utilisateur (sauf les administrateurs).
* **Protection XSS :** Sanitisation HTML sur toutes les données affichées.
* **Anti-Spam :** Rate Limiting intégré.
* **Écriture Atomique :** Les fichiers de données (`user_data.json`) utilisent une écriture atomique (temp + rename) pour éviter toute corruption.
* **Optimisation Mémoire :** Pré-dimensionnement des `HashSet` pour réduire les allocations.

---

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
1.  Téléchargez le fichier `.zip` depuis la page [Releases](https://github.com/peterdu1109/NotifySync/releases/tag/v4.6.9).
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

* **Quotas :** Définissez combien d'éléments afficher par catégorie (ex: 5 films, 5 séries...).
* **Bibliothèques Surveillées :** Cochez les dossiers que vous souhaitez voir apparaître dans les notifications.
* **Mappage de Catégories :** Renommez vos bibliothèques pour l'affichage.
    * *Exemple :* Bibliothèque `4K-Movies` ➡️ Afficher comme `Films`.

---

## 📋 Changelog (v4.6.9)

*   🔒 **Authentification obligatoire** : Ajout de `[Authorize]` sur tous les endpoints API (sauf Client.js).
*   🛡️ **Protection IDOR** : Vérification d'identité sur les endpoints Data, BulkUserData, LastSeen.
*   🔐 **Vérification admin** : Les administrateurs peuvent accéder aux données de tous les utilisateurs.
*   🛠️ **Journalisation des erreurs** : Les erreurs de sauvegarde du fichier `user_data.json` sont désormais loguées au lieu d'être silencieusement ignorées.

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
