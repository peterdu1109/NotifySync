# 🔔 NotifySync

![Version](https://img.shields.io/badge/Version-1.0.0-blue?style=flat-square)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.5%2B-purple?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)

**NotifySync** est un plugin complet pour Jellyfin qui intègre un centre de notifications interactif et moderne directement dans l'en-tête de votre interface utilisateur. Ne ratez plus jamais les derniers ajouts de votre bibliothèque !

---

## ✨ Fonctionnalités Principales

### 🛎️ Centre de Notification Intégré
*   **Intégration transparente** : Ajoute une icône "Cloche" dans la barre de navigation.
*   **Indicateur visuel** : Badge rouge dynamique affichant le nombre d'éléments non vus.
*   **Design Moderne** : Interface soignée avec effet de flou (*Glassmorphism*).

### 🧠 Gestion Intelligente des Médias
*   **Regroupement** : Les épisodes d'une même série ajouté simultanément sont regroupés (ex: "3 nouveaux épisodes").
*   **Distinction Films/Séries** : Badges dédiés pour identifier rapidement le type de contenu.
*   **Indicateur "Nouveau"** : Badge clignotant pour les médias ajoutés il y a moins de 48h.
*   **Suivi de lecture** : Barre de progression visible pour les médias en cours.

### 🎮 Expérience Utilisateur (UX)
*   **Mobile Friendly** : Glissez vers la droite (*Swipe*) pour marquer une notification comme vue sur mobile.
*   **Raccourcis** : Touche `N` pour ouvrir/fermer le panneau rapidement.
*   **Lecture Directe** : Lancez la lecture immédiatement depuis la notification.
*   **Tout marquer comme vu** : Un bouton unique pour nettoyer votre liste.
*   **Notifications Sonores** : Feedback audio optionnel lors de l'arrivée de nouveaux médias.

### ⚙️ Performance & Synchronisation
*   **Par utilisateur** : Le statut "Vu" est synchronisé et propre à chaque utilisateur.
*   **Optimisé** : Chargement asynchrone avec effet "Skeleton" pour une fluidité maximale.

---

## 🚀 Installation

1.  Téléchargez la dernière version (`.dll`) depuis la page des [Releases](https://github.com/peterdu1109/NotifySync/releases).
2.  Copiez le fichier `NotifySync.dll` dans le dossier `plugins` de votre serveur Jellyfin.
3.  Redémarrez votre serveur Jellyfin.
4.  L'icône de notification devrait apparaître dans la barre supérieure !

---

## 🛠️ Configuration

Une page de configuration est disponible dans votre Tableau de Bord Jellyfin :
`Tableau de bord > Extensions > NotifySync`

Vous pouvez y configurer :
*   Le nombre maximum d'éléments à afficher dans le menu (Défaut : 5).
*   L'activation des notifications sonores.

---

## 🏗️ Développement

Ce projet est construit avec **.NET 9.0**.

### Pré-requis
*   .NET 9.0 SDK
*   Jellyfin 10.11.5+ (Binaries for reference)

### Compilation
```bash
dotnet build --configuration Release
```

---

*Créé avec ❤️ pour la communauté Jellyfin.*
