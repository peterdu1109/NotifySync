# 🔔 NotifySync

![Version](https://img.shields.io/badge/Version-4.3.18-blue?style=flat-square)
![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.5%2B-purple?style=flat-square)
![Framework](https://img.shields.io/badge/.NET-9.0-512bd4?style=flat-square)

**NotifySync** est un plugin complet pour Jellyfin qui intègre un tableau de bord de suivi des nouveautés directement dans l'en-tête de votre interface utilisateur.

> [!IMPORTANT]
> **Mise à jour v4.3 "Surgical Update"** : Synchronisation précise du statut "Vu", Quotas par catégories et Support Musique complet.

---

## ✨ Fonctionnalités Clés (v4.3)

### 🧠 Intelligence & Quotas
* **Quotas par Catégorie** : Fini les films écrasés par une saison de série ! Configurez "5 éléments" pour avoir les **5 derniers Films** + **5 dernières Séries** + **5 derniers Albums**.
* **Support Multi-Média** : Gestion native des Films, Séries, Animes et **Albums de Musique** (avec affichage carré des pochettes).

### 👁️ Synchronisation "Chirurgicale"
* **Vérification Réelle** : Le plugin interroge la base de données Jellyfin item par item pour savoir si vous avez *vraiment* vu un épisode.
* **Gestion des Groupes** : Si vous avez vu le dernier épisode d'une série, le groupe entier est marqué comme "Vu".
* **Persistance** : Même les vieux ajouts sont correctement marqués comme "Vus" ou "Non Vus".

### 🎨 Interface "Clean Mode" (Zen)
* **Zéro Stress** : Plus de pastille rouge "9+" sur la cloche.
* **Indicateurs Discrets** :
    * **Non Vu** : Fine bordure rouge à gauche + Badge "NOUVEAU" sur la bannière.
    * **Déjà Vu** : Affichage normal et propre (sans être grisé/illisible), pour garder un historique clair.
* **Hero Banner Dynamique** : Le dernier média ajouté s'affiche en grand en haut du panneau.

### 🛠️ Robustesse Technique
* **Détection "Bulldozer"** : Détection des bibliothèques infaillible (par ID ou par NOM de dossier), idéal pour les configurations Docker/Samba complexes.
* **Scan Profond** : Analyse jusqu'à 300 éléments en arrière pour remplir vos quotas par catégorie.

---

## 🚀 Installation

1.  Téléchargez la dernière version (`.dll`) depuis la page des [Releases](https://github.com/peterdu1109/NotifySync/releases).
2.  Copiez le fichier `NotifySync.dll` dans le dossier `plugins` de votre serveur Jellyfin.
3.  Redémarrez votre serveur Jellyfin.
4.  L'icône de notification apparaîtra dans la barre supérieure (pensez à vider le cache navigateur `CTRL+F5`).

---

## 🛠️ Configuration

Une page de configuration est disponible dans votre Tableau de Bord Jellyfin :
`Tableau de bord > Extensions > NotifySync`

Vous pouvez y configurer :
* **Quota par catégorie** : Le nombre d'éléments à garder pour *chaque* type de média (Défaut : 5).
* **Bibliothèques** : Cochez celles à surveiller ou entrez leurs noms manuellement (ex: "Animes").
* **Catégories** : Renommez vos bibliothèques (ex: La bibliothèque "Jap-Anim" -> Affiche "Anime").
* **Maintenance** : Bouton "Régénérer" pour forcer un nouveau scan complet.

---

## 🏗️ Développement

Ce projet est construit avec **.NET 9.0**.

### Pré-requis
* .NET 9.0 SDK
* Jellyfin 10.11.5+ (Binaries for reference)

### Compilation
```bash
dotnet build --configuration Release