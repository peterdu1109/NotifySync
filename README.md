# 🔔 NotifySync pour Jellyfin

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

### 🚀 Performance (.NET 9)
* **Moteur Haute Performance :** Réécrit en .NET 9 avec des algorithmes optimisés (O(1)) pour une vérification instantanée, même avec d'immenses bibliothèques.
* **Zéro-Allocation :** Gestion mémoire stricte pour ne pas impacter les performances de votre serveur.
* **Smart Caching (ETag) :** Le client ne retélécharge les données que si nécessaire.
* **Renommage Auto :** Si vous renommez un fichier, la notification se met à jour automatiquement.

### 🛡️ Sécurité & Confidentialité
* **Respect des Permissions (Privacy) :** Un utilisateur ne recevra JAMAIS de notification (ni image, ni titre) pour un contenu auquel il n'a pas accès (ex: profils enfants).
* **Protection XSS :** Assainissement rigoureux des métadonnées pour empêcher toute injection de code malveillant.
* **Anti-Spam :** Protection intégrée contre le rafraîchissement excessif (Rate Limiting).

---

## 📦 Installation

### 1. Pré-requis
* **Jellyfin 10.11.X** ou supérieur.
* **.NET 9 Runtime** (généralement inclus avec Jellyfin récent).

### Méthode 1 : Via le Dépôt (Recommandé)
1.  Ouvrez votre tableau de bord Jellyfin > **Extensions** > **Dépôts**.
2.  Ajoutez un nouveau dépôt :
    * **Nom :** NotifySync Repo
    * **URL :** `https://raw.githubusercontent.com/peterdu1109/NotifySync/refs/heads/main/repository.json`
3.  Allez dans le **Catalogue**, trouvez **NotifySync** et cliquez sur **Installer**.
4.  Redémarrez votre serveur Jellyfin.

### Méthode 2 : Installation Manuelle
1.  Téléchargez le fichier `.zip` depuis la page [Releases](https://github.com/peterdu1109/NotifySync/releases/tag/4.6.5).
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
