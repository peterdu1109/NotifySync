/* NOTIFYSYNC V4.9 - AUTHENTICATED CHECK */
(function () {
    let currentData = [];
    let groupedData = [];
    let lastSeenDate = new Date(0);
    let isFetching = false;
    let retryDelay = 2000;
    let activeFilter = 'All';
    
    const STRINGS = {
        fr: { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", markAll: "Tout marquer comme vu", badgeNew: "NOUVEAU", newEps: "épisodes", filterAll: "Tout" },
        en: { header: "What's New?", empty: "You're all caught up!", markAll: "Mark all read", badgeNew: "NEW", newEps: "episodes", filterAll: "All" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;

    // --- HELPER AUTH ---
    // Récupère le token Jellyfin actuel pour s'identifier auprès du plugin
    const getAuthHeaders = () => {
        return {
            'Content-Type': 'application/json',
            'X-Emby-Token': window.ApiClient.accessToken() // LA CLE DU PROBLEME EST ICI
        };
    };

    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;
        const css = `
            :root { --ns-red: #e50914; --ns-glass: rgba(20, 20, 20, 0.98); --ns-blur: 16px; --ns-border: rgba(255,255,255,0.15); }
            #bell-container { display:flex!important;align-items:center;justify-content:center; }
            #netflix-bell { background:0 0;border:none;cursor:pointer;padding:10px;color:inherit;position:relative; transition: transform 0.2s; }
            #netflix-bell:active { transform: scale(0.9); }
            .ns-badge {
                position: absolute; top: 4px; right: 4px;
                background: var(--ns-red); color: white;
                font-size: 10px; font-weight: bold;
                padding: 1px 5px; border-radius: 10px;
                box-shadow: 0 2px 5px rgba(0,0,0,0.5);
                opacity: 0; transition: opacity 0.3s, transform 0.3s;
                transform: scale(0.5); pointer-events: none;
            }
            .ns-badge.visible { opacity: 1; transform: scale(1); }
            #notify-backdrop { position: fixed; inset: 0; z-index: 999998; display: none; }
            #notification-dropdown {
                position: fixed; top: 70px; right: 20px; width: 380px; max-width: 90vw;
                background: var(--ns-glass); backdrop-filter: blur(var(--ns-blur)); -webkit-backdrop-filter: blur(var(--ns-blur));
                border: 1px solid var(--ns-border); border-radius: 12px;
                box-shadow: 0 20px 60px rgba(0,0,0,0.6);
                z-index: 999999; display: none; 
                font-family: 'Noto Sans', sans-serif; 
                animation: slideDown 0.25s cubic-bezier(0.2, 0.8, 0.2, 1);
                overflow: hidden;
            }
            @keyframes slideDown { from { opacity:0; transform:translateY(-10px); } to { opacity:1; transform:translateY(0); } }
            @keyframes spin { 100% { transform: rotate(360deg); } }
            .spinning { animation: spin 1s linear infinite; opacity: 1!important; }

            .dropdown-header { display:flex; justify-content:space-between; padding:16px 20px; border-bottom: 1px solid var(--ns-border); background: rgba(0,0,0,0.3); align-items:center; }
            .header-title { font-weight: 700; font-size: 15px; letter-spacing: 0.5px; }
            .header-tools { display:flex; gap:15px; }
            .tool-icon { cursor:pointer; opacity:0.6; transition:opacity 0.2s; font-size: 18px; }
            .tool-icon:hover { opacity:1; }
            .filter-bar { padding: 10px 20px; display: flex; gap: 8px; border-bottom: 1px solid var(--ns-border); overflow-x: auto; scrollbar-width: none; }
            .filter-pill { font-size: 11px; padding: 4px 12px; border-radius: 20px; background: rgba(255,255,255,0.05); cursor: pointer; transition: all 0.2s; border: 1px solid transparent; }
            .filter-pill.active { background: #fff; color: #000; font-weight: 700; box-shadow: 0 0 10px rgba(255,255,255,0.2); }
            .list-container { max-height: 500px; overflow-y: auto; }
            .dropdown-item { display:flex; padding:12px 20px; border-bottom:1px solid var(--ns-border); cursor:pointer; transition: background .2s; position: relative; }
            .dropdown-item:hover { background: rgba(255,255,255,0.08); }
            .status-dot {
                position: absolute; left: 6px; top: 50%; transform: translateY(-50%);
                width: 4px; height: 4px; border-radius: 50%; background: var(--ns-red);
                box-shadow: 0 0 5px var(--ns-red);
                display: none;
            }
            .style-new .status-dot { display: block; }
            .style-new { background: rgba(229, 9, 20, 0.05); }
            .thumb-wrapper { width:90px; height:50px; margin-right:15px; flex-shrink:0; background:#222; border-radius:6px; overflow:hidden; display:flex; justify-content:center; align-items:center; box-shadow: 0 2px 5px rgba(0,0,0,0.3); }
            .dropdown-thumb { width:100%; height:100%; object-fit:cover; opacity:0; transition:opacity 0.3s; }
            .dropdown-thumb.music { object-fit:contain; }
            .dropdown-thumb.loaded { opacity:1; }
            .dropdown-info { flex:1; display:flex; flex-direction:column; justify-content:center; min-width: 0; }
            .dropdown-title { font-weight:600; font-size:13px; margin-bottom:4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
            .dropdown-subtitle { font-size:11px; color:#aaa; }
            .hero-section { height: 160px; position: relative; cursor: pointer; display: flex; align-items: flex-end; margin-bottom: -1px; }
            .hero-bg { position: absolute; inset: 0; background-size: cover; background-position: center top; transition: transform 5s ease; }
            .hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, var(--ns-glass) 5%, transparent 100%); }
            .hero-content { position: relative; z-index: 2; padding: 20px; width: 100%; }
            .hero-badge { background: var(--ns-red); color: #fff; font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 3px; display: inline-block; margin-bottom: 5px; box-shadow: 0 2px 5px rgba(0,0,0,0.5); }
            .footer-tools { padding: 10px; text-align: center; border-top: 1px solid var(--ns-border); font-size: 11px; color: #888; cursor: pointer; transition: color 0.2s; }
            .footer-tools:hover { color: #fff; text-decoration: underline; }
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
    };

    const getUserId = () => window.ApiClient.getCurrentUserId();

    const processGrouping = (items) => {
        const g = {}; const r = [];
        items.forEach(i => {
            if (i.Type === 'Episode' && i.SeriesName) {
                if (!g[i.SeriesName]) { 
                    g[i.SeriesName] = { ...i, IsGroup: true, GroupCount: 1, LatestDate: i.DateCreated, GroupIds: [i.Id] }; 
                    r.push(g[i.SeriesName]); 
                } else { 
                    g[i.SeriesName].GroupCount++; 
                    g[i.SeriesName].GroupIds.push(i.Id);
                    if(new Date(i.DateCreated) > new Date(g[i.SeriesName].LatestDate)) {
                        g[i.SeriesName].LatestDate = i.DateCreated;
                        g[i.SeriesName].Id = i.Id; 
                    }
                }
            } else { 
                r.push(i); 
            }
        });
        return r.sort((a,b) => new Date(b.DateCreated) - new Date(a.DateCreated));
    };

    const fetchLastSeen = async () => {
        try {
            // AUTH HEADERS AJOUTES
            const res = await fetch(`/NotifySync/LastSeen/${getUserId()}`, { headers: getAuthHeaders() });
            const dateStr = await res.text();
            lastSeenDate = new Date(JSON.parse(dateStr));
        } catch(e) { lastSeenDate = new Date(0); }
    };

    const updateLastSeen = async () => {
        const now = new Date().toISOString();
        // AUTH HEADERS AJOUTES
        await fetch(`/NotifySync/LastSeen/${getUserId()}?date=${encodeURIComponent(now)}`, { 
            method: 'POST',
            headers: getAuthHeaders()
        });
        lastSeenDate = new Date();
        updateBadge();
        updateList(document.getElementById('notification-dropdown'));
    };

    const refreshPlayStates = async () => {
        if (!currentData.length) return;
        const ids = currentData.map(i => i.Id);
        try {
            // --- C'EST ICI QUE CA SE JOUE : AUTHENTICATION ---
            const res = await fetch(`/NotifySync/BulkUserData?userId=${getUserId()}`, {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify(ids)
            });

            if(res.ok) {
                const statusMap = await res.json();
                currentData.forEach(item => {
                    if (statusMap.hasOwnProperty(item.Id)) item.Played = statusMap[item.Id];
                    // Si item.Played est true (vient du serveur), IsNew sera false
                    item.IsNew = !item.Played && (new Date(item.DateCreated) > lastSeenDate);
                });

                groupedData = processGrouping(currentData);
                groupedData.forEach(g => {
                    if(g.IsGroup) {
                        const latest = currentData.find(x => x.Id === g.Id);
                        g.IsNew = latest ? latest.IsNew : false;
                    }
                });
                updateBadge();
            }
        } catch(e) { console.error("Bulk check failed", e); }
    };

    const fetchData = async () => {
        if (isFetching) return; 
        isFetching = true;
        try {
            await fetchLastSeen();
            // AUTH HEADERS AJOUTES
            const res = await fetch('/NotifySync/Data?t=' + Date.now(), { headers: getAuthHeaders() });
            if (res.ok) {
                currentData = await res.json();
                await refreshPlayStates(); 
                retryDelay = 2000;
            }
        } catch (e) { 
            setTimeout(fetchData, retryDelay);
            retryDelay = Math.min(retryDelay * 2, 60000); 
        } finally { isFetching = false; }
    };
    
    // Hard Refresh avec la flèche
    const triggerHardRefresh = async () => {
        const btn = document.querySelector('.tool-icon.refresh-icon');
        if(btn) btn.classList.add('spinning');
        
        try {
            await fetch('/NotifySync/Refresh', { method: 'POST', headers: getAuthHeaders() });
            await new Promise(r => setTimeout(r, 500));
            await fetchData();
            const drop = document.getElementById('notification-dropdown');
            if(drop && drop.style.display === 'block') updateList(drop);
        } catch(e) { console.error("Hard refresh failed", e); }
        finally { if(btn) btn.classList.remove('spinning'); }
    };

    const updateBadge = () => {
        const bell = document.getElementById('netflix-bell');
        if(!bell) return;
        let badge = bell.querySelector('.ns-badge');
        if(!badge) {
            badge = document.createElement('div'); badge.className = 'ns-badge'; bell.appendChild(badge);
        }
        const count = groupedData.filter(i => i.IsNew).length;
        if(count > 0) {
            badge.innerText = count > 9 ? '9+' : count;
            badge.classList.add('visible');
        } else {
            badge.classList.remove('visible');
        }
    };

    const updateList = (drop) => {
        if(!drop) return;
        const container = drop.querySelector('.list-container');
        let filtered = groupedData || [];
        if (activeFilter !== 'All') filtered = filtered.filter(i => i.Category === activeFilter);

        const cats = new Set(['All']); groupedData.forEach(i => cats.add(i.Category));
        const bar = drop.querySelector('.filter-bar');
        bar.innerHTML = Array.from(cats).map(c => 
            `<div class="filter-pill ${activeFilter===c?'active':''}" onclick="document.dispatchEvent(new CustomEvent('ns-filter', {detail:'${c}'}))">${T['filter'+c]||c}</div>`
        ).join('');

        if (filtered.length === 0) {
            container.innerHTML = `<div style="padding:60px 20px;text-align:center;color:#666;font-style:italic;">${T.empty}</div>`;
            return;
        }

        let html = '';
        const client = window.ApiClient;
        const hero = filtered.find(i => i.IsNew) || filtered[0];
        
        if (hero) {
            const tag = (hero.BackdropImageTags && hero.BackdropImageTags[0]) || '';
            let heroImg = tag ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?tag=${tag}&quality=70&maxWidth=600`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=70&maxWidth=400`);
            if(hero.IsGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${hero.SeriesId}/Images/Backdrop/0?quality=70&maxWidth=600`);

            html += `
            <div class="hero-section" onclick="window.location.hash='#!/details?id=${hero.IsGroup ? hero.SeriesId : hero.Id}'">
                <div class="hero-bg" style="background-image:url('${heroImg}')"></div>
                <div class="hero-overlay"></div>
                <div class="hero-content">
                    ${hero.IsNew ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}
                    <div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">
                        ${hero.IsGroup ? hero.SeriesName : (hero.SeriesName || hero.Name)}
                    </div>
                    <div style="font-size:12px;opacity:0.8;margin-top:4px;">${hero.IsGroup ? `${hero.GroupCount} ${T.newEps}` : hero.ProductionYear}</div>
                </div>
            </div>`;
        }

        filtered.filter(x => x !== hero).forEach(item => {
            const isMusic = item.Category === 'Music';
            const imgTag = item.PrimaryImageTag || '';
            const imgOpts = isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200';
            const imgUrl = client.getUrl(`Items/${item.IsGroup ? item.SeriesId : item.Id}/Images/Primary?tag=${imgTag}&${imgOpts}&quality=80`);
            const title = item.IsGroup ? item.SeriesName : (item.SeriesName || item.Name);
            const sub = item.IsGroup ? `${item.GroupCount} ${T.newEps}` : (item.SeriesName ? item.Name : item.ProductionYear);

            html += `
            <div class="dropdown-item ${item.IsNew ? 'style-new' : 'style-seen'}" onclick="window.location.hash='#!/details?id=${item.IsGroup ? item.SeriesId : item.Id}'">
                <div class="status-dot"></div>
                <div class="thumb-wrapper">
                    <img data-src="${imgUrl}" class="dropdown-thumb ${isMusic?'music':''}" loading="lazy" onerror="this.style.display='none'">
                    <span class="material-icons" style="color:#444;position:absolute;z-index:-1;">${isMusic?'album':'movie'}</span>
                </div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    <div class="dropdown-subtitle">${sub}</div>
                </div>
            </div>`;
        });

        html += `<div class="footer-tools" onclick="document.dispatchEvent(new Event('ns-markall'))">${T.markAll}</div>`;
        container.innerHTML = html;

        const obs = new IntersectionObserver((entries, o) => { 
            entries.forEach(e => { if (e.isIntersecting) { const i = e.target; i.src = i.dataset.src; i.classList.add('loaded'); o.unobserve(i); } }); 
        });
        container.querySelectorAll('img[data-src]').forEach(i => obs.observe(i));
    };

    const toggleDropdown = () => {
        let drop = document.getElementById('notification-dropdown');
        const backdrop = document.getElementById('notify-backdrop');
        
        if(!drop) {
            drop = document.createElement('div'); drop.id = 'notification-dropdown';
            document.body.appendChild(drop);
            document.addEventListener('ns-filter', (e) => { activeFilter = e.detail; updateList(drop); });
            document.addEventListener('ns-markall', () => { updateLastSeen(); drop.style.display='none'; backdrop.style.display='none'; });
            document.addEventListener('ns-refresh', () => { triggerHardRefresh(); });

            drop.innerHTML = `
                <div class="dropdown-header">
                    <span class="header-title">${T.header}</span>
                    <div class="header-tools">
                        <span class="material-icons tool-icon refresh-icon" onclick="document.dispatchEvent(new Event('ns-refresh'))">refresh</span>
                    </div>
                </div>
                <div class="filter-bar"></div>
                <div class="list-container"></div>
            `;
        }
        if(!backdrop) {
            const b = document.createElement('div'); b.id = 'notify-backdrop'; b.onclick = toggleDropdown; document.body.appendChild(b);
        }

        const isOpen = drop.style.display === 'block';
        if (!isOpen) {
            fetchData().then(() => updateList(drop));
            document.getElementById('notify-backdrop').style.display = 'block';
            drop.style.display = 'block';
        } else {
            drop.style.display = 'none';
            document.getElementById('notify-backdrop').style.display = 'none';
        }
    };

    const installBell = () => {
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right');
        if (!header || document.getElementById('bell-container')) return;
        injectStyles();
        const div = document.createElement('div'); div.id = 'bell-container';
        div.innerHTML = `<button id="netflix-bell" class="paper-icon-button-light headerButton"><span class="material-icons notifications"></span></button>`;
        div.firstChild.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toggleDropdown(); };
        header.prepend(div);
        fetchData();
    };

    const obs = new MutationObserver(() => { if(!document.getElementById('bell-container')) installBell(); });
    obs.observe(document.body, { childList: true, subtree: true });
    
    setInterval(() => { if(document.visibilityState === 'visible') fetchData(); }, 60000);

    installBell();
})();