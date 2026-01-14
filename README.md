# 🔔 NotifySync

![Version](https://img.shields.io/badge/Version-4.5.0-blue?style=flat-square)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.5%2B-purple?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)

**NotifySync** est un centre de notifications avancé pour Jellyfin. Il remplace la cloche par défaut par un tableau de bord moderne, performant et intelligent.

> [!IMPORTANT]
> **Mise à jour v4.5 "Glassmorphism & Batch Performance"**
> Nouvelle interface translucide, groupement intelligent des épisodes, et correction définitive du statut "Vu" (Point rouge) via requêtes par lots.

---

## ✨ Nouveautés de la v4.5

### 🎨 Interface "Glassmorphism" & Hero Banner
* **Design Translucide** : L'interface utilise désormais un effet de flou moderne (Glassmorphism) qui s'adapte à votre arrière-plan.
* **Hero Banner Dynamique** : Le contenu le plus récent s'affiche en grand en haut de la liste avec son image "Backdrop".
* **Groupement Intelligent** : Les épisodes d'une même série sont regroupés en une seule ligne (ex: "Arcane - 3 nouveaux épisodes") pour ne pas polluer l'affichage.

### ⚡ Performance & Synchronisation (Batch Fix)
* **Vérification "Batch"** : Le plugin vérifie désormais le statut de lecture de tous les éléments en **une seule requête** ultra-rapide, au lieu de faire une boucle lente.
* **Correction "Point Rouge"** : L'identification de l'utilisateur est forcée explicitement, garantissant que le statut "Vu" est correctement détecté même sans recharger la page.
* **Règle des 90%** : Si Jellyfin n'a pas encore marqué un élément comme "Vu", le plugin le force si la lecture dépasse 90% de la durée.

---

## 🧠 Fonctionnalités Clés

### 📊 Intelligence & Quotas
* **Quotas par Catégorie** : Configurez des limites strictes (ex: 5 Films + 5 Séries + 5 Albums). Le plugin scanne jusqu'à **500 éléments** dans l'historique pour garantir que vos quotas sont toujours remplis.
* **Support Multi-Média** : Gestion native des Films, Séries, Animes et Albums de Musique.

### 👁️ Gestion "Zen"
* **Zéro Stress** : Plus de badge "9+" anxiogène.
* **Indicateurs Discrets** :
    * **Non Vu** : Badge "NOUVEAU" et point rouge (qui disparaît vraiment une fois vu).
    * **Déjà Vu** : Affichage propre pour garder un historique clair.

### 🛠️ Robustesse Technique
* **Détection "Bulldozer"** : Identification des bibliothèques par ID ou par NOM de dossier (idéal pour Docker/Samba).
* **Tampon d'événements** : Les ajouts rapides sont mis en file d'attente pour ne jamais manquer une notification.

---

## 🚀 Installation

1.  Téléchargez la dernière version (`.dll`) depuis la page des [Releases](https://github.com/peterdu1109/NotifySync/releases).
2.  Copiez le fichier `NotifySync.dll` dans le dossier `plugins` de votre serveur Jellyfin.
3.  Redémarrez votre serveur Jellyfin.
4.  L'icône de notification apparaîtra dans la barre supérieure (pensez à vider le cache navigateur `CTRL+F5`).

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
* [cite_start].NET 9.0 SDK [cite: 1]
* [cite_start]Jellyfin 10.11.5+ [cite: 1]

### Compilation
```bash
dotnet build --configuration Release