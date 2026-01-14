**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, performant et intelligent, inspiré des plateformes de streaming majeures.

> [!IMPORTANT]
> **Mise à jour v4.5.3 **
> Ajout des dates relatives ("il y a 5 min"), optimisation pour l'affichage mobile, et correction critique de la détection utilisateur via requêtes par lots.
> Correction sur le regroupement d'épisode
---

## ✨ Nouveautés de la v4.5.3

### 🕒 Time Ago & Sous-titres Riches
* **Dates Relatives** : Fini les dates brutes. Le plugin affiche désormais le temps écoulé : *"à l'instant"*, *"il y a 2 h"*, *"il y a 5 jours"*.
* **Métadonnées Contextuelles** : Les sous-titres combinent intelligemment le nom de la série, le numéro de saison/épisode et le temps écoulé (ex: *S02E04 - Arcane • il y a 2 h*).

### 📱 Optimisation Mobile
* **Interface Réactive** : Le panneau de notification s'adapte désormais parfaitement aux écrans mobiles (largeur dynamique, hauteur ajustée).
* **Navigation Tactile** : Amélioration du scrolling et de la taille des zones tactiles pour une utilisation fluide sur smartphone.

### 🎨 Interface "Glassmorphism" & Hero Banner
* **Design Translucide** : L'interface utilise un effet de flou moderne (Glassmorphism) qui s'adapte à votre arrière-plan.
* **Hero Banner Dynamique** : Le contenu le plus récent s'affiche en grand en haut de la liste avec son image "Backdrop".
* **Groupement Intelligent** : Les épisodes d'une même série sont regroupés en une seule ligne pour ne pas polluer l'affichage.

### ⚡ Performance & Correctifs (Batch Fix)
* **Correction UserID** : L'identification de l'utilisateur est maintenant explicite, corrigeant les problèmes de statut "Vu" sur les profils multiples.
* **Vérification "Batch"** : Le plugin vérifie le statut de lecture de tous les éléments en **une seule requête** ultra-rapide.

---

## 🚀 Installation

### 1. Pré-requis
* Avoir installé le plugin **"JavaScript Injector"** (disponible dans le catalogue officiel de Jellyfin sous la section "Général").

### 2. Installation du Backend (DLL)
1.  Téléchargez `NotifySync.dll` depuis les [Releases](https://github.com/peterdu1109/NotifySync/releases).
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

---

## 🛠️ Configuration

Une page de configuration est disponible dans `Tableau de bord > Extensions > NotifySync`.

* **Quota par catégorie** : Nombre d'éléments à garder par type (Min: 3, Défaut: 5).
* **Bibliothèques** : Cochez celles à surveiller.
* **Mappage** : Renommez vos bibliothèques (ex: "Jap-Anim" -> "Anime").
* **Maintenance** : Bouton "Régénérer" pour forcer un nouveau scan complet de l'historique.

---

## 🏗️ Développement

Ce projet est construit avec **.NET 9.0**.

### Pré-requis
* .NET 9.0 SDK
* Jellyfin 10.11.5+

### Compilation
```bash
dotnet build --configuration Release