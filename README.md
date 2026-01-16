# NotifySync

**NotifySync** transforme l'expérience Jellyfin en ajoutant un centre de notifications moderne (style cloche "Netflix"), fluide et intelligent.

> [!IMPORTANT]
> **Mise à jour critique v4.6.5**
> Cette version corrige des failles de sécurité importantes (XSS, Fuite de données entre utilisateurs) et intègre des protections contre le déni de service (DoS). La mise à jour est fortement recommandée.

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