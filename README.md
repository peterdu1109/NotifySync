**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, performant et intelligent, inspiré des plateformes de streaming majeures.

> [!IMPORTANT]
> **Mise à jour v4.5.7 (Performance Update)**
> Cette version introduit un cache RAM pour les données utilisateurs et parallélise les requêtes client. La charge sur les disques (I/O) est drastiquement réduite.

---

## ✨ Nouveautés de la v4.5.7

### ⚡ Optimisations Backend (C#)
* **Cache Mémoire (RAM)** : La date de "dernière visite" (`LastSeen`) est désormais servie depuis la RAM via un `ConcurrentDictionary`. Fini la lecture du fichier `user_data.json` à chaque requête API (gain I/O massif).
* **Non-Bloquant (Copy-On-Write)** : Le tri et le groupement des notifications se font sur une copie locale de la liste. L'API reste disponible à 100% même pendant l'ajout massif de médias.
* **Sécurité des Threads** : Gestion fine des verrous (`ReaderWriterLockSlim`) pour garantir l'intégrité des données sans ralentir le serveur.

### 🚀 Optimisations Frontend (JS)
* **Chargement Parallèle** : Utilisation de `Promise.all` pour récupérer les données et le statut de lecture simultanément.
* **Anti-Scintillement** : Le DOM n'est mis à jour que si le contenu HTML a réellement changé, économisant le CPU du navigateur.
* **Optimisation WebP** : Les images demandées sont forcées en format WebP pour réduire la bande passante.
* **Mode Paysage : Prise en charge sur mobile
* **Regroupement : Correction sur le regroupement global
---

## 🚀 Installation

### 1. Pré-requis
* Avoir installé le plugin **"JavaScript Injector"** (disponible dans le catalogue officiel de Jellyfin sous la section "Général").

### 2. Installation du Backend (DLL)
1.  Téléchargez `NotifySync.dll` (v4.5.6) depuis les [Releases](https://github.com/peterdu1109/NotifySync/releases).
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