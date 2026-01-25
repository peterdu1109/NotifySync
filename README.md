# 🔔 NotifySync

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

### 🚀 Performance (v4.6.7)
* **Zéro-Latence (Nouveau) :** Architecture de cache "Per-User". Les notifications sont servies instantanément depuis le cache RAM, sans recalcul, tant que le contenu ne change pas sur le serveur.
* **.NET 9 Native :** Utilisation intensive de `FrozenSet` et `System.Threading.Lock` pour une rapidité extrême.
* **Optimisation Réseau :** ETags intelligents qui évitent tout retéléchargement inutile par les clients.
* **Moteur optimisé :** Algorithmes O(1) pour la résolution des bibliothèques parentes.

### 🛡️ Sécurité & Confidentialité
* **Respect des Permissions (Privacy) :** Isolation stricte via le moteur Jellyfin ("Core Engine Isolation"). Utilisation de `InternalItemsQuery` pour garantir qu'un utilisateur ne verra **jamais** de contenu non autorisé (par Tags, Classification, ou Librairie).
* **Protection IDOR & XSS :** Correctifs de sécurité avancés et sanitisation HTML.
* **Anti-Spam :** Rate Limiting intégré.

---

## 📦 Installation

### 1. Pré-requis
* **Jellyfin 10.11.X**
* **.NET 9 Runtime** (Obligatoire pour v4.6.7+).

### Méthode 1 : Via le Dépôt (Recommandé)
1.  Ouvrez votre tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez un nouveau dépôt :
    * **Nom :** NotifySync Repo
    * **URL :** `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Allez dans le **Catalogue**, trouvez **NotifySync** et cliquez sur **Installer**.
4.  Redémarrez votre serveur Jellyfin.

### Méthode 2 : Installation Manuelle
1.  Téléchargez le fichier `.zip` depuis la page [Releases](https://github.com/peterdu1109/NotifySync/releases/tag/v4.6.7).
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

## ❓ Dépannage

* **La cloche n'apparaît pas ?** Assurez-vous d'avoir vidé le cache de votre navigateur et que le script JS est bien injecté.
* **Mes albums de musique ne s'affichent pas ?** Vérifiez que le type de contenu de votre bibliothèque est bien défini sur "Music" dans Jellyfin.
