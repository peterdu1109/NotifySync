#!/bin/bash

# NotifySync Client Uninstaller
# Usage: sudo ./uninstall.sh [optional_path_to_index.html]

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${YELLOW}🔍 Recherche de l'interface web Jellyfin...${NC}"

# Détection automatique du chemin Jellyfin
PATHS=(
    "/usr/share/jellyfin/web/index.html"      # Linux Native (Debian/Ubuntu)
    "/jellyfin/jellyfin-web/index.html"       # Docker Standard
    "/app/jellyfin-web/index.html"            # Docker LSIO
)

TARGET=""

# Gestion d'un argument manuel
if [ ! -z "$1" ]; then
    TARGET="$1"
else
    for p in "${PATHS[@]}"; do
        if [ -f "$p" ]; then
            TARGET="$p"
            break
        fi
    done
fi

if [ -z "$TARGET" ]; then
    echo -e "${RED}❌ Erreur : Impossible de trouver l'interface web de Jellyfin.${NC}"
    echo "   Essayez de spécifier le chemin : ./uninstall.sh /chemin/vers/index.html"
    exit 1
fi

echo -e "📂 Cible identifiée : ${GREEN}$TARGET${NC}"

# Vérification des droits d'écriture
if [ ! -w "$TARGET" ]; then
    echo -e "${RED}❌ Erreur : Permission refusée. Lancez le script avec sudo.${NC}"
    exit 1
fi

# Désinstallation
if grep -q "NotifySync/Client.js" "$TARGET"; then
    echo "🧹 Nettoyage du script..."
    
    # Suppression de la ligne injectée
    sed -i 's|<script src="/NotifySync/Client.js" defer></script>||g' "$TARGET"
    
    if ! grep -q "NotifySync/Client.js" "$TARGET"; then
        echo -e "${GREEN}✅ Désinstallation terminée avec succès !${NC}"
        
        # Restauration potentielle du backup si l'utilisateur le souhaite (optionnel, ici on clean juste)
        # Mais on garde le fichier cleané.
        echo "👉 Pensez à vider le cache de votre navigateur."
    else
        echo -e "${RED}❌ Erreur lors de la suppression.${NC}"
        exit 1
    fi
else
    echo -e "${YELLOW}⚠️ Le client NotifySync n'est PAS installé sur ce fichier.${NC}"
fi
