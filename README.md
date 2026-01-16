# NotifySync

**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, fluide et intelligent, inspiré des plateformes de streaming majeures.

> [!NOTE]
> **Version 4.6.3 - Hyper-Optimisation**
> Cette version se concentre sur la performance brute. Elle réduit la charge CPU des scans de 90% sur les grosses bibliothèques et élimine les micro-lags liés à la gestion mémoire.

---

## ⚡ Nouveautés de la v4.6.3

### 🧠 Optimisations Backend (C# .NET 9)
* **Algorithme en O(1)** : Le filtrage des bibliothèques utilise désormais des `HashSet` au lieu de listes linéaires.
    * *Impact* : La vérification d'une bibliothèque est **instantanée**, peu importe le nombre de dossiers que vous possédez.
* **Zero-Allocation Versioning** : Remplacement des GUIDs (lourds) par des compteurs atomiques (`Interlocked.Read/Increment`).
    * *Impact* : Réduction drastique de la pression sur le Garbage Collector (GC), rendant le serveur plus stable lors des mises à jour fréquentes.
* **Thread-Safety Avancé** : Utilisation de primitives de verrouillage légères (`System.Threading.Lock`) introduites dans .NET 9.

### 🎨 Optimisations Client (JS)
* **Intl.RelativeTimeFormat** : Le calcul du temps ("il y a 5 minutes") est maintenant délégué au moteur natif du navigateur.
    * *Impact* : Script plus léger, exécution plus rapide sur mobile et traductions grammaticalement parfaites pour toutes les langues.

---

## 🚀 Installation

### 1. Pré-requis
* **Jellyfin 10.11.5** ou supérieur.
* Plugin **"JavaScript Injector"** installé (Catalogue Jellyfin > Général).

### 2. Installation du Backend (DLL)
1.  Téléchargez `NotifySync.dll` (v4.6.3) depuis les Releases.
2.  Créez le dossier `plugins/NotifySync` dans votre serveur Jellyfin.
3.  Copiez le fichier `.dll` à l'intérieur.

**Chemins typiques :**
* **Docker** : `/config/plugins/NotifySync`
* **Linux** : `/var/lib/jellyfin/plugins/NotifySync`
* **Windows** : `%ProgramData%\Jellyfin\Server\plugins\NotifySync`

> ⚠️ **Linux/Docker** : Vérifiez les permissions (`chown -R jellyfin:jellyfin ...`).

### 3. Activation du Frontend
Pour afficher la cloche, injectez le script via le plugin **JS Injector** (Tableau de bord) :

1.  Ajoutez un nouveau script.
2.  Cochez **Requires Authentication** (Indispensable).
3.  Collez le code suivant :
    ```javascript
    var script = document.createElement('script');
    script.src = '/NotifySync/Client.js';
    script.defer = true;
    document.head.appendChild(script);
    ```

---

## 🛠️ Configuration

Rendez-vous dans **Tableau de bord > Extensions > NotifySync** :

1.  **Quota** : Nombre d'éléments à afficher par catégorie.
2.  **Bibliothèques** : Cochez les dossiers à surveiller.
3.  **Mappage** : Personnalisez les noms de catégories (ex: "4K Movies" -> "Films").
4.  **Maintenance** : Utilisez le bouton pour forcer un re-scan complet si nécessaire.

---

## 🏗️ Compilation (Pour les devs)

```bash
# Nécessite le SDK .NET 9
dotnet restore
dotnet publish -c Release -o bin/Publish