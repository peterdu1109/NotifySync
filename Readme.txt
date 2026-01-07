=== NotifySync ===
Version: 1.0.0
Auteur: [Votre Nom/Pseudo]
Framework: .NET 9.0
Compatibilité: Jellyfin 10.11.5+

NotifySync est un plugin complet pour Jellyfin qui ajoute un centre de notifications interactif (une cloche) dans l'en-tête de l'interface utilisateur. Il permet aux utilisateurs de voir rapidement les derniers ajouts médias sans quitter leur page actuelle.

== Fonctionnalités Principales ==

1. Centre de Notification Intégré :
   - Ajout automatique d'une icône "Cloche" dans la barre de navigation supérieure.
   - Badge rouge indiquant le nombre de nouveaux éléments non vus.
   - Dropdown (menu déroulant) moderne avec effet de flou (Glassmorphism).

2. Gestion Intelligente des Médias :
   - Regroupement des épisodes : Si plusieurs épisodes d'une même série sont ajoutés, ils sont regroupés en une seule ligne (ex: "3 nouveaux épisodes").
   - Distinction visuelle claire entre FILMS et SÉRIES via des badges dédiés.
   - Indicateur "NOUVEAU" clignotant pour les ajouts de moins de 48h.
   - Barre de progression affichée sur les médias en cours de lecture.

3. Interactions Utilisateur (UX) :
   - "Swipe to Mark Read" (Mobile) : Glissez une notification vers la droite sur mobile pour la marquer comme vue.
   - Raccourci Clavier : Appuyez sur la touche 'N' pour ouvrir/fermer les notifications.
   - Lecture Directe : Bouton de lecture (Play) sur la jaquette pour lancer le média immédiatement.
   - "Tout marquer comme vu" : Un bouton pour vider la liste des notifications.

4. Synchronisation & Backend :
   - Suivi "Dernier Vu" par utilisateur : Le plugin mémorise quel utilisateur a vu quel contenu via une API dédiée.
   - Persistance : Les données sont sauvegardées dans la configuration du plugin (fichier XML) côté serveur.
   - Notifications Sonores : Option pour activer/désactiver un son lors de l'arrivée d'une nouvelle notification (stocké en local).

5. Performance :
   - Chargement "Skeleton" : Affichage d'un squelette de chargement pendant la récupération des données pour une interface fluide.
   - Optimisation réseau : Les requêtes sont mises en cache et le script gère les tentatives de reconnexion automatique (Retry Logic).

== Configuration ==
Une page de configuration est disponible dans le Tableau de bord > Extensions > NotifySync pour définir le nombre maximum d'éléments à afficher dans le menu (Défaut : 5).