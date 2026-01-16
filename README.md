**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, performant et intelligent, inspiré des plateformes de streaming majeures.

> [!IMPORTANT]
> **Mise à jour v4.6.0 (Stability & Playability)**
> Cette version introduit la sauvegarde atomique (fini les fichiers corrompus), le "Click-to-Play" immédiat, et une refonte du rendu DOM pour une fluidité maximale sur mobile.

---

## ✨ Nouveautés de la v4.6.0

### 🛡️ Fiabilité & Backend (C#)
* **Sauvegarde Atomique** : Les fichiers (`notifications.json` et `user_data.json`) sont désormais écrits dans un fichier temporaire `.tmp` avant d'être déplacés. Cela empêche totalement la corruption de données en cas de crash serveur pendant l'écriture.
* **Sauvegarde Non-Bloquante** : La mise à jour du statut "Vu" (`LastSeen`) se fait en arrière-plan (Fire-and-Forget), rendant l'interface instantanée.
* **Sécurité Timer** : Correction de potentiels bugs de réentrance sur les timers de traitement.

### ⚡ Expérience & Frontend (JS)
* **Optimisation DOM** : Réécriture du moteur de rendu (utilisation de `Array.join` au lieu de concaténation) pour un affichage beaucoup plus rapide des longues listes sur mobile.
* **Gestion Mémoire** : Les observateurs (`MutationObserver`) se déconnectent intelligemment quand ils ne sont pas nécessaires pour économiser les ressources.
* **Logic Batching** : Amélioration du regroupement (fenêtre de 12h) pour mieux distinguer les ajouts de saisons complètes des sorties hebdomadaires.

---

## 🚀 Installation

### 1. Pré-requis
* Avoir installé le plugin **"JavaScript Injector"** (disponible dans le catalogue officiel de Jellyfin sous la section "Général").

### 2. Installation du Backend (DLL)
1.  Téléchargez `NotifySync.dll` (v4.6.0) depuis les [Releases](https://github.com/peterdu1109/NotifySync/releases).
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
dotnet build --configuration Release