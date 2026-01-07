# 🔔 NotifySync

![Version](https://img.shields.io/badge/Version-4.0.0-blue?style=flat-square)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.5%2B-purple?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)

**NotifySync** est un plugin complet pour Jellyfin qui intègre un centre de notifications interactif et moderne directement dans l'en-tête de votre interface utilisateur.

> [!IMPORTANT]
> **Mise à jour v4.0 "Performance"** : Nouvelle architecture backend, chargement instantané et séparation des données.

---

## ✨ Fonctionnalités (v4.0)

### ⚡ Performances Extrêmes
*   **JSON Backend** : Les notifications sont maintenant gérées et mises en cache côté serveur (`notifications.json`). Le client ne requête plus la base de données Jellyfin en direct.
*   **Lazy Loading** : Les images ne chargent que lorsqu'elles apparaissent à l'écran.
*   **Smart Polling** : Utilisation de la bande passante réduite au minimum.

### 🛡️ Interface Robuste (Overlay)
*   **Backdrop** : Protection contre les fermetures accidentelles (Cliquez dehors pour fermer).
*   **Skeleton UI** : Interface de chargement élégante pendant la récupération des données.



### 🌟 Expérience Visuelle "Hero"
*   **Hero Banner** : Le dernier média ajouté s'affiche en grand en haut du panneau.
*   **Cartes Interactives** : Liste verticale avec images larges (Backdrop) pour un look plus cinéma.

### 🧭 Navigation & Filtres
*   **Filtres Intelligents** : `[Tout]`, `[Films]`, `[Séries]`, `[Animes]`.
*   **Contrôles** : Boutons "Actualiser" et "Tout marquer comme vu" accessibles.

### 🛎️ Centre de Notification
*   **Intégration transparente** : Ajoute une icône "Cloche" dans la barre de navigation.
*   **Indicateur visuel** : Badge rouge dynamique affichant le nombre d'éléments non vus.
*   **Design Glassmorphism** : Interface sombre et transparente.

### 🧠 Gestion Intelligente
*   **Regroupement** : Les épisodes d'une même série ajouté simultanément sont regroupés (ex: "3 nouveaux épisodes").
*   **Indicateur "Nouveau"** : Badge clignotant pour les médias ajoutés il y a moins de 48h.
*   **Suivi de lecture** : Barre de progression visible pour les médias en cours.

### 🎮 Expérience Utilisateur (UX)
*   **Mobile Friendly** : Glissez vers la droite (*Swipe*) pour marquer une notification comme vue sur mobile.
*   **Lecture Directe** : Lancez la lecture immédiatement depuis la notification (bouton Play et Hero Banner).

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
