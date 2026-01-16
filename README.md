# NotifySync

**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, performant et intelligent, inspiré des plateformes de streaming majeures.

> [!IMPORTANT]
> **Mise à jour v4.6.2 (Performance .NET 9)**
> Cette version migre le moteur vers **.NET 9** et introduit des optimisations majeures : utilisation de `System.Threading.Lock`, sérialisation JSON native (Source Generators) et réduction drastique de l'empreinte mémoire.

---

## ✨ Nouveautés de la v4.6.2

### 🚀 Performance & Backend (.NET 9)
* **High-Performance Locking** : Remplacement des verrous classiques par la nouvelle primitive `System.Threading.Lock` de .NET 9, réduisant la latence lors des accès concurrents.
* **JSON Source Generators** : La sérialisation n'utilise plus la réflexion mais des contextes générés à la compilation. Résultat : démarrage plus rapide et fichiers de données (`user_data.json`) lus/écrits instantanément.
* **Optimisation Mémoire** : Utilisation de collections modernes et réduction des allocations (GC Pressure) lors du scan des bibliothèques.

### 🛡️ Fiabilité
* **Sauvegarde Atomique** : Les fichiers critiques sont écrits dans un fichier temporaire `.tmp` avant d'être déplacés, garantissant zéro corruption en cas de crash.
* **Sécurité Timer** : Protection renforcée des timers d'arrière-plan pour éviter les arrêts silencieux du service de notification.

### ⚡ Expérience Frontend
* **Client v4.6.2** : Le script client a été mis à jour pour supporter la navigation native vers les pages de détails (compatible avec les "Theme Songs" de Jellyfin).
* **Rendu Optimisé** : Amélioration de la fluidité sur mobile via une refonte du rendu DOM.

---

## 🚀 Installation

### 1. Pré-requis
* **Jellyfin 10.11.5** ou supérieur.
* Avoir installé le plugin **"JavaScript Injector"** (disponible dans le catalogue officiel de Jellyfin sous la section "Général").

### 2. Installation du Backend (DLL)
1.  Téléchargez `NotifySync.dll` (v4.6.2) depuis les Releases.
2.  Créez un dossier nommé `NotifySync` dans le répertoire des plugins de votre serveur.
3.  Copiez le fichier `.dll` à l'intérieur.

**Chemins par défaut des plugins :**

| OS | Chemin typique |
| :--- | :--- |
| **🐳 Docker** | `/config/plugins/NotifySync` (ou `/var/lib/jellyfin/plugins/NotifySync`) |
| **🐧 Linux** | `/var/lib/jellyfin/plugins/NotifySync` |
| **🪟 Windows** | `%ProgramData%\Jellyfin\Server\plugins\NotifySync` |
| **🍎 macOS** | `~/.local/share/jellyfin/plugins/NotifySync` |

> ⚠️ **Note Linux/Docker :** Assurez-vous que l'utilisateur `jellyfin` a les droits de lecture/écriture sur ce dossier (`chown -R jellyfin:jellyfin ...`).

### 3. Activation du Frontend (JS Injector)
Pour que la cloche apparaisse, vous devez injecter le script client via l'interface d'administration.

1.  Redémarrez votre serveur Jellyfin pour charger la DLL.
2.  Allez dans **Tableau de bord > JS Injector**.
3.  Ajoutez un nouveau script avec les paramètres suivants :
    * **Script Name** : `Cloche` (ou NotifySync)
    * **Requires Authentication** : ☑️ **Cochez OBLIGATOIREMENT cette case** (nécessaire pour l'API utilisateur).
    * **Code Javascript** : Copiez-collez le bloc ci-dessous :

    ```javascript
    var script = document.createElement('script');
    script.src = '/NotifySync/Client.js';
    script.defer = true;
    document.head.appendChild(script);
    ```

---

## 🛠️ Configuration

Une page de configuration est disponible dans `Tableau de bord > Extensions > NotifySync`.

* **Quota par catégorie** : Nombre d'éléments à garder par type (Min: 3, Défaut: 5).
* **Bibliothèques** : Cochez celles à surveiller.
* **Mappage** : Renommez vos bibliothèques (ex: "Jap-Anim" -> "Anime").
* **Maintenance** : Bouton "Régénérer" pour forcer un nouveau scan complet de l'historique et purger le cache.

---

## 🏗️ Développement

Ce projet est construit avec **.NET 9.0**.

### Pré-requis
* .NET 9.0 SDK
* Jellyfin 10.11.5+

### Compilation
```bash
dotnet restore
dotnet publish -c Release -o bin/Publish