## 🚀 Nouveautés & Corrections (v4.8.0.3)

Cette version clôture un audit de bout en bout de l'architecture du plugin afin d'assurer une stabilité *Enterprise-grade*, particulièrement sur les environnements Linux et Docker.

### 🛡️ Stabilité & Intégrité des Données
* **[CRITIQUE] Prévention de Corruption de Fichiers (I/O)** : L'écriture de l'historique de visionnage (`users_seen.json`) utilise désormais des écritures atomiques via un buffer temporaire (`.tmp`). Cela garantit que vos données ne seront **jamais effacées ou corrompues** si le conteneur Docker ou le serveur redémarre brutalement ou subit une micro-coupure à la milliseconde de la sauvegarde. Ce correctif est essentiel pour les utilisateurs Linux.

### ⚡ Performances CPU & Mémoire (Anti-Lag)
* **Élimination du Goulot d'Étranglement Mémoire (Lock Escalation)** : Le système de vérification des caches itérait de façon agressive sur le `ConcurrentDictionary`. Cela forçait le moteur .NET à figer des parties de la mémoire (Lock Escalation), pénalisant les performances globales du serveur sous forte charge. Ce mécanisme a été remplacé par des itérateurs asynchrones légers.
* **Algorithme de Visibilité Multi-Threadé** : Au lieu de vérifier les restrictions d'accès de 2 000 éléments les uns après les autres sur un seul cœur, l'API distribue désormais intelligemment la charge sur l'ensemble des cœurs de votre processeur vi un `Parallel.ForEach`. Bilan : les temps de réponse de la cloche de notification chutent drastiquement.

---

### 📦 Installation ou Mise à jour
👉 **Pour mettre à jour** : 
1. Allez dans le **Tableau de bord** de Jellyfin > **Extensions** > **Katalog/Catalogue**.
2. Trouvez **NotifySync** et mettez-le à jour.
3. **Redémarrez votre serveur** et le tour est joué ! (Pensez à vider le cache de votre navigateur web si la cloche ne s'affiche pas).

*Si vous préférez l'installation manuelle, téléchargez l'archive `.zip` ci-dessous et extrayez la `NotifySync.dll` dans votre dossier `plugins`.*
