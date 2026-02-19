#!/bin/bash

# NotifySync Client Injector
# Usage: sudo ./install.sh [optional_path_to_index.html]

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
    echo "   Essayez de spécifier le chemin : ./install.sh /chemin/vers/index.html"
    exit 1
fi

echo -e "📂 Cible identifiée : ${GREEN}$TARGET${NC}"

# Vérification des droits d'écriture
if [ ! -w "$TARGET" ]; then
    echo -e "${RED}❌ Erreur : Permission refusée. Lancez le script avec sudo.${NC}"
    exit 1
fi

# Vérification si déjà installé
if grep -q "NotifySync/Client.js" "$TARGET"; then
    echo -e "${YELLOW}⚠️ Le client NotifySync est DÉJÀ installé.${NC}"
else
    # Backup
    echo "📦 Création d'une sauvegarde (index.html.bak)..."
    cp "$TARGET" "$TARGET.bak"
    
    # Injection
    echo "💉 Injection du script..."
    # Utilisation de sed pour remplacer </body> par le script + </body>
    sed -i 's|</body>|<script src="/NotifySync/Client.js" defer></script></body>|' "$TARGET"
    
    if grep -q "NotifySync/Client.js" "$TARGET"; then
        echo -e "${GREEN}✅ Installation terminée avec succès !${NC}"
        echo "👉 Pensez à vider le cache de votre navigateur (Ctrl+F5)."
    else
        echo -e "${RED}❌ Erreur lors de l'injection.${NC}"
        # Restauration en cas d'échec
        mv "$TARGET.bak" "$TARGET"
        exit 1
    fi
fi
