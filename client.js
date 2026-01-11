/* NOTIFYSYNC V4.3.18 - SURGICAL CHECK MODE */
(function () {
    let currentData = null;
    let groupedData = null;
    let isFetching = false;
    let activeFilter = 'All';
    let pollInterval = 60000;
    
    const SOUND_KEY = 'notifysync_muted';
    
    const STRINGS = {
        fr: { header: "Derniers Ajouts", empty: "Aucun média...", markAll: "Tout marquer comme vu", badgeNew: "NOUVEAU", newEps: "épisodes", filterAll: "Tout" },
        en: { header: "Latest Media", empty: "No media...", markAll: "Mark all read", badgeNew: "NEW", newEps: "episodes", filterAll: "All" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;

    let hoverAudio = null;
    let hoverTimeout = null;

    // --- CSS ---
    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;
        const css = `
            :root { --ns-red: #e50914; --ns-dark: #141414; }
            #bell-container{display:flex!important;align-items:center;justify-content:center;z-index:1000}
            #netflix-bell{background:0 0;border:none;cursor:pointer;padding:10px;color:inherit;position:relative}
            
            #notify-backdrop { position: fixed; inset: 0; z-index: 999998; display: none; }
            #notification-dropdown{position:fixed;top:70px;right:20px;width:380px;max-width:90vw;background:var(--ns-dark);color:#fff;border:1px solid #333;border-radius:6px;box-shadow:0 10px 40px rgba(0,0,0,0.8);z-index:999999;display:none; font-family: sans-serif; animation: dropIn 0.2s ease;}
            
            .dropdown-header { display:flex;justify-content:space-between;padding:15px;border-bottom: 1px solid #333; background:#000; align-items:center;}
            .header-tools { display:flex; gap:15px; }
            .tool-icon { cursor:pointer; opacity:0.7; transition:opacity 0.2s; font-size: 20px; }
            .tool-icon:hover { opacity:1; }
            .filter-bar { padding: 10px; display: flex; gap: 8px; border-bottom: 1px solid #333; overflow-x: auto; background: #1a1a1a; }
            .filter-pill { font-size: 11px; padding: 4px 12px; border-radius: 20px; border: 1px solid #555; cursor: pointer; opacity: 0.8; white-space: nowrap; }
            .filter-pill.active { background: #fff; color: #000; border-color: #fff; opacity: 1; font-weight: bold; }
            .list-container { max-height: 450px; overflow-y: auto; }
            
            .dropdown-item{display:flex;padding:12px;border-bottom:1px solid #222;cursor:pointer;transition: background .2s;}
            .dropdown-item:hover{background:#222;}
            
            /* --- STYLE CLEAN --- */
            /* NON VU : Bordure Rouge + Fond léger */
            .dropdown-item.style-new { 
                border-left: 3px solid var(--ns-red); 
                background: rgba(255,255,255,0.05); 
            }
            /* VU : Normal (Transparent) */
            .dropdown-item.style-seen { 
                border-left: 3px solid transparent; 
                opacity: 1; 
            }

            .thumb-wrapper{width:100px;height:56px;margin-right:12px;flex-shrink:0;background:#333;border-radius:4px;overflow:hidden; display:flex; justify-content:center; align-items:center;}
            .dropdown-thumb{width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity 0.3s;}
            .dropdown-thumb.music-thumb{object-fit:contain;}
            .dropdown-thumb.loaded{opacity:1;}
            .dropdown-info{flex:1;display:flex;flex-direction:column;justify-content:center;}
            .dropdown-title{font-weight:700;font-size:13px;margin-bottom:3px; line-height:1.2;}
            .dropdown-subtitle{font-size:11px;color:#aaa;}
            
            .hero-section { position: relative; height: 180px; cursor: pointer; display: flex; align-items: flex-end; }
            .hero-bg { position: absolute; inset: 0; background-size: cover; background-position: center; transition: transform 4s ease; }
            .hero-content { position: relative; z-index: 2; padding: 15px; width:100%; }
            .hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, #141414 0%, transparent 100%); }
            
            #notifysync-toast{position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:#fff;color:#000;padding:8px 20px;border-radius:20px;font-weight:bold;z-index:10000;opacity:0;transition:opacity .3s;pointer-events:none;}
            #notifysync-toast.visible{opacity:1;}
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
        if (!document.getElementById('notifysync-toast')) { const t = document.createElement('div'); t.id = 'notifysync-toast'; document.body.appendChild(t); }
    };

    const showToast = (msg) => { const t = document.getElementById('notifysync-toast'); if (t) { t.innerHTML = msg; t.classList.add('visible'); setTimeout(() => t.classList.remove('visible'), 2000); } };
    const getCategory = (item) => item.Category || 'Movie';

    const processGrouping = (items) => {
        const g = {}; const r = [];
        items.forEach(i => {
            if (i.Type === 'Episode' && i.SeriesName) {
                if (!g[i.SeriesName]) { 
                    g[i.SeriesName] = { ...i, IsGroup: true, GroupCount: 1, LatestDate: i.DateCreated }; 
                    r.push(g[i.SeriesName]); 
                } else { 
                    g[i.SeriesName].GroupCount++; 
                    if(new Date(i.DateCreated) > new Date(g[i.SeriesName].LatestDate)) {
                        g[i.SeriesName].LatestDate = i.DateCreated;
                        g[i.SeriesName].Id = i.Id; 
                        // Le groupe hérite du statut du dernier épisode
                        g[i.SeriesName].Played = i.Played; 
                    }
                }
            } else { r.push(i); }
        });
        return r;
    };

    const toggleDropdown = () => {
        const drop = document.getElementById('notification-dropdown');
        const backdrop = document.getElementById('notify-backdrop');
        if (!drop) return;
        const isOpen = drop.style.display === 'block';
        if (!isOpen) {
            refreshPlayStates().then(() => {
                drop.style.display = 'block'; backdrop.style.display = 'block';
                updateList(drop);
            });
        } else {
            drop.style.display = 'none'; backdrop.style.display = 'none';
            stopHoverSound();
        }
    };

    const playHoverSound = (itemId) => {
        if (localStorage.getItem(SOUND_KEY) === 'true') return;
        if (hoverAudio) { hoverAudio.pause(); hoverAudio = null; }
        clearTimeout(hoverTimeout);
        hoverTimeout = setTimeout(async () => {
            const client = window.ApiClient;
            try {
                const songs = await client.getThemeSongs(client.getCurrentUserId(), itemId);
                if (songs && songs.Items.length > 0) {
                    hoverAudio = new Audio(client.getUrl(`Audio/${songs.Items[0].Id}/stream`));
                    hoverAudio.volume = 0.2; hoverAudio.play().catch(() => {});
                }
            } catch (e) {}
        }, 600);
    };
    const stopHoverSound = () => { clearTimeout(hoverTimeout); if (hoverAudio) { hoverAudio.pause(); hoverAudio = null; } };

    const renderFilters = (drop, items) => {
        const bar = drop.querySelector('.filter-bar');
        const cats = new Set(['All']);
        items.forEach(i => cats.add(getCategory(i)));
        let html = '';
        cats.forEach(c => {
            const label = T['filter' + c] || c; 
            const active = activeFilter === c ? 'active' : '';
            html += `<div class="filter-pill ${active}" data-f="${c}">${label}</div>`;
        });
        bar.innerHTML = html;
        bar.querySelectorAll('.filter-pill').forEach(pill => {
            pill.onclick = (e) => {
                activeFilter = e.target.getAttribute('data-f');
                updateList(drop);
            };
        });
    };

    const updateList = async (drop) => {
        const container = drop.querySelector('.list-container');
        container.innerHTML = '<div style="padding:20px;text-align:center;color:#666;">Chargement...</div>';

        let filtered = groupedData || [];

        renderFilters(drop, filtered);
        if (activeFilter !== 'All') filtered = filtered.filter(i => getCategory(i) === activeFilter);

        if (filtered.length === 0) {
            container.innerHTML = `<div style="padding:50px 0;text-align:center;color:#666;">${T.empty}</div>`;
            return;
        }

        let html = '';
        const client = window.ApiClient;

        // HERO
        const hero = filtered[0];
        if (hero) {
            let heroImg = hero.BackdropImageTags && hero.BackdropImageTags.length > 0 
                ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=60&maxWidth=600`) 
                : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=60&maxWidth=400`);
            if (hero.IsGroup) heroImg = client.getUrl(`Items/${hero.SeriesId}/Images/Backdrop/0?quality=60&maxWidth=600`);
            if(hero.Category === 'Music' && (!hero.BackdropImageTags || hero.BackdropImageTags.length === 0)) {
                 heroImg = client.getUrl(`Items/${hero.Id}/Images/Primary?quality=60&maxWidth=400`);
            }

            // BADGE NOUVEAU SI PAS VU
            const badgeHtml = !hero.Played 
                ? `<div style="background:var(--ns-red);color:#fff;display:inline-block;padding:2px 6px;font-size:10px;font-weight:bold;border-radius:2px;margin-bottom:4px;">${T.badgeNew}</div>` 
                : '';

            html += `
            <div class="hero-section" onclick="window.location.hash='#!/details?id=${hero.IsGroup ? hero.SeriesId : hero.Id}'">
                <div class="hero-bg" style="background-image:url('${heroImg}')"></div>
                <div class="hero-overlay"></div>
                <div class="hero-content">
                    ${badgeHtml}
                    <div style="font-size:18px;font-weight:bold;text-shadow:0 1px 2px #000;">${hero.IsGroup ? hero.SeriesName : (hero.SeriesName || hero.Name)}</div>
                </div>
            </div>`;
        }

        // LIST
        filtered.slice(1).forEach(item => {
            let imgOptions = (item.Category === 'Music') ? 'fillHeight=100&fillWidth=100&quality=90' : 'fillHeight=112&fillWidth=200&quality=90';
            let imgUrl = client.getUrl(`Items/${item.IsGroup ? item.SeriesId : item.Id}/Images/Primary?${imgOptions}`);
            let title = item.IsGroup ? item.SeriesName : (item.SeriesName || item.Name);
            let sub = item.IsGroup ? `${item.GroupCount} ${T.newEps}` : (item.SeriesName ? item.Name : item.ProductionYear);
            let imgClass = (item.Category === 'Music') ? 'dropdown-thumb music-thumb' : 'dropdown-thumb';

            // CLASSE CSS : style-seen (Vu) ou style-new (Pas vu)
            const statusClass = item.Played ? 'style-seen' : 'style-new';

            html += `
            <div class="dropdown-item ${statusClass}" id="notif-item-${item.Id}">
                <div class="thumb-wrapper"><img data-src="${imgUrl}" class="${imgClass}" loading="lazy"></div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    <div class="dropdown-subtitle">${sub} &bull; ${getCategory(item)}</div>
                </div>
            </div>`;
        });
        container.innerHTML = html;

        const obs = new IntersectionObserver((entries, o) => { entries.forEach(e => { if (e.isIntersecting) { const i = e.target; i.src = i.dataset.src; i.onload = () => i.classList.add('loaded'); o.unobserve(i); } }); });
        container.querySelectorAll('img[data-src]').forEach(i => obs.observe(i));
        container.querySelectorAll('.dropdown-item').forEach(el => { 
            el.onmouseenter = () => playHoverSound(el.id.replace('notif-item-', ''));
            el.onmouseleave = stopHoverSound;
            el.onclick = () => { window.location.hash = `#!/details?id=${el.id.replace('notif-item-', '')}`; toggleDropdown(); }; 
        });
    };

    const buildUI = () => {
        if (document.getElementById('notification-dropdown')) return;
        const bg = document.createElement('div'); bg.id = 'notify-backdrop'; bg.onclick = toggleDropdown; document.body.appendChild(bg);
        const d = document.createElement('div'); d.id = 'notification-dropdown';
        d.innerHTML = `
            <div class="dropdown-header">
                <span style="font-weight:bold;font-size:16px;">${T.header}</span>
                <div class="header-tools">
                    <span class="material-icons tool-icon" id="ns-refresh" title="${T.refresh}">refresh</span>
                    <span class="material-icons tool-icon" id="ns-mute">volume_up</span>
                </div>
            </div>
            <div class="filter-bar"></div>
            <div class="list-container"></div>`;
        d.onclick = e => e.stopPropagation();
        document.body.appendChild(d);

        d.querySelector('#ns-refresh').onclick = () => { fetchData(); toggleDropdown(); };
        d.querySelector('#ns-mute').onclick = (e) => {
            const m = localStorage.getItem(SOUND_KEY) === 'true';
            localStorage.setItem(SOUND_KEY, !m);
            e.target.innerText = !m ? 'volume_off' : 'volume_up';
        };
    };

    const installBell = () => {
        try {
            const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right');
            if (!header || document.getElementById('netflix-bell')) return;
            
            injectStyles();
            const div = document.createElement('div'); div.id = 'bell-container';
            div.innerHTML = `<button id="netflix-bell" class="paper-icon-button-light headerButton"><span class="material-icons notifications"></span></button>`;
            div.firstChild.onclick = (e) => { e.preventDefault(); e.stopPropagation(); buildUI(); toggleDropdown(); };
            header.prepend(div);
            
            fetchData();
        } catch(e) { console.error("NotifySync: Bell install failed", e); }
    };

    // --- SYNCHRONISATION CHIRURGICALE (ITEM PAR ITEM) ---
    const refreshPlayStates = async () => {
        if (!currentData || currentData.length === 0) return;
        
        const client = window.ApiClient;
        const uid = client.getCurrentUserId();
        
        console.log("NotifySync: Starting surgical UserData check...");

        // On crée un tableau de promesses pour vérifier chaque item individuellement
        // C'est le seul moyen 100% fiable d'avoir le UserData si le bulk échoue
        const promises = currentData.map(async (item) => {
            try {
                // On récupère l'item complet avec ses infos UserData
                const fullItem = await client.getItem(uid, item.Id);
                
                let played = false;
                if (fullItem.UserData) {
                    played = fullItem.UserData.Played || fullItem.UserData.PlayedPercentage > 90;
                }
                
                // Debug log
                // console.log(`Check: ${item.Name} -> Played: ${played}`);
                
                // Mise à jour directe
                item.Played = played;
            } catch (err) {
                console.warn(`NotifySync: Failed to check ${item.Name}`, err);
                item.Played = false;
            }
        });

        // On attend que tout soit vérifié
        await Promise.all(promises);

        // On refait les groupes avec les nouvelles infos
        groupedData = processGrouping(currentData);
        
        // Propagation aux groupes (si le dernier épisode est vu, le groupe est vu)
        groupedData.forEach(g => {
            if (g.IsGroup) {
                // On trouve l'item source dans currentData pour confirmer
                const source = currentData.find(x => x.Id === g.Id);
                if (source && source.Played) g.Played = true;
            }
        });
        
        console.log("NotifySync: Check complete.");
    };

    const fetchData = async () => {
        if (isFetching) return; isFetching = true;
        try {
            const res = await fetch('/NotifySync/Data?t=' + Date.now());
            if (res.ok) {
                currentData = await res.json();
                await refreshPlayStates();
            }
        } catch (e) { console.error(e); } finally { isFetching = false; }
    };

    const obs = new MutationObserver(() => {
        if(!document.getElementById('netflix-bell')) installBell();
    });
    obs.observe(document.body, { childList: true, subtree: true });

    setInterval(() => {
        if (document.visibilityState === 'visible') fetchData();
    }, pollInterval);
    
    installBell();
})();