# NotifySync

**NotifySync** transforme l'expérience Jellyfin en ajoutant un centre de notifications moderne (style cloche "Netflix"), fluide et intelligent.

> [!IMPORTANT]
> **Mise à jour v4.6.5 : Stabilité, Musique & Sécurité**

---

## 🛡️ Sécurité & Performance (v4.6.5)

### 🔒 Sécurité Renforcée
* **Protection XSS** : Le client JavaScript échappe désormais systématiquement les titres et descriptions, empêchant l'injection de code malveillant via les métadonnées des fichiers médias.
* **Confidentialité (Privacy)** : L'API filtre désormais les notifications côté serveur. Un utilisateur "Enfant" ne recevra plus les métadonnées (titres/images) des contenus qui lui sont interdits.
* **Anti-Spam (Rate Limiting)** : La fonction "Refresh" est limitée à une exécution par minute pour empêcher la surcharge du serveur (DoS).

### 🚀 Moteur .NET 9
* **Algorithme O(1)** : Vérification instantanée des bibliothèques via `HashSet` (plus de ralentissement avec de grosses bibliothèques).
* **Zéro-Allocation** : Gestion mémoire optimisée pour réduire la pression sur le serveur.
* **Navigation Fluide** : Le client utilise `decoding="async"` pour ne pas bloquer le défilement lors du chargement des images.

### 🔄 Synchronisation Instantanée
* **Support du Renommage** : Si vous renommez un film ou une série dans Jellyfin, la notification se met désormais à jour **automatiquement** dans la cloche. Plus besoin de rafraîchir manuellement la page.
* **Cache Intelligent (ETag)** : Le navigateur ne retélécharge les données que si le contenu a réellement changé sur le serveur. Cela garantit que vous voyez toujours le titre le plus récent sans surcharger la bande passante.
* **Refresh Fiabilisé** : Le bouton de rafraîchissement manuel a été ajusté pour garantir que les nouvelles données sont prêtes avant d'être affichées.

### 🎵 Support Musique Corrigé
* **Filtre Intelligent** : Le moteur de scan distingue désormais correctement les "Albums Musicaux" des "Dossiers génériques". Vos albums apparaissent enfin dans la cloche.
* **Scan "Blindé"** : Ajout d'une protection d'erreurs au niveau de chaque item. Si un média spécifique fait planter le scan (données corrompues), le plugin l'ignore et continue de charger les autres notifications.

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