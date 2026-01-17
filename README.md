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
* **Jellyfin 10.11.5** ou supérieur.
* **.NET 9 Runtime** (généralement inclus avec Jellyfin récent).
* Plugin **"JavaScript Injector"** (Catalogue > Général).

### 2. Installation du Backend
1.  Téléchargez `NotifySync.dll` depuis les Releases.
2.  Placez le fichier dans le dossier `plugins/NotifySync` de votre serveur.
3.  Redémarrez Jellyfin.

| OS | Chemin des plugins |
| :--- | :--- |
| **Docker** | `/config/plugins/NotifySync` |
| **Linux** | `/var/lib/jellyfin/plugins/NotifySync` |
| **Windows** | `%ProgramData%\Jellyfin\Server\plugins\NotifySync` |

### 3. Injection du Client (Frontend)
Pour afficher la cloche, ajoutez ce snippet via le plugin **JavaScript Injector** :

1.  Ouvrez **Tableau de bord > JS Injector**.
2.  Ajoutez un script nommé `NotifySync`.
3.  **Cochez "Requires Authentication"** (⚠️ Indispensable pour la sécurité).
4.  Code :
    ```javascript
    var script = document.createElement('script');
    script.src = '/NotifySync/Client.js';
    script.defer = true;
    document.head.appendChild(script);
    ```

---

## ⚙️ Configuration

Allez dans **Tableau de bord > Extensions > NotifySync** :
* **Quota** : Nombre d'items par catégorie.
* **Bibliothèques** : Choix des dossiers à surveiller.
* **Mappage** : Renommage des catégories (ex: "Jap-Anim" -> "Animes").

---

## 🏗️ Compilation

```bash
dotnet restore
dotnet publish -c Release -o bin/Publish