/* NOTIFYSYNC V4.5.5 - WEBP + ETAG + FIX */
(function () {
    let currentData = [];
    let groupedData = [];
    let lastSeenDate = new Date(0);
    let isFetching = false;
    let retryDelay = 2000;
    let activeFilter = 'All';
    
    const STRINGS = {
        fr: { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", markAll: "Tout marquer comme vu", badgeNew: "NOUVEAU", newEps: "nouveaux épisodes", eps: "épisodes", filterAll: "Tout" },
        en: { header: "What's New?", empty: "You're all caught up!", markAll: "Mark all read", badgeNew: "NEW", newEps: "new episodes", eps: "episodes", filterAll: "All" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;

    const timeAgo = (date) => {
        const seconds = Math.floor((new Date() - new Date(date)) / 1000);
        const isFr = userLang.startsWith('fr');
        let interval = seconds / 31536000;
        if (interval > 1) return isFr ? `il y a ${Math.floor(interval)} an(s)` : `${Math.floor(interval)}y ago`;
        interval = seconds / 2592000;
        if (interval > 1) return isFr ? `il y a ${Math.floor(interval)} mois` : `${Math.floor(interval)}mo ago`;
        interval = seconds / 86400;
        if (interval > 1) return isFr ? `il y a ${Math.floor(interval)} j` : `${Math.floor(interval)}d ago`;
        interval = seconds / 3600;
        if (interval > 1) return isFr ? `il y a ${Math.floor(interval)} h` : `${Math.floor(interval)}h ago`;
        interval = seconds / 60;
        if (interval > 1) return isFr ? `il y a ${Math.floor(interval)} min` : `${Math.floor(interval)}m ago`;
        return isFr ? "à l'instant" : "just now";
    };

    const getAuthHeaders = () => {
        return {
            'Content-Type': 'application/json',
            'X-Emby-Token': window.ApiClient.accessToken()
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
                display: flex; flex-direction: column; 
            }
            @media (max-width: 600px) {
                #notification-dropdown { top: 60px; right: 10px; left: 10px; width: auto; max-width: none; max-height: 80vh; }
                .hero-section { height: 130px !important; }
                .list-container { flex: 1; max-height: none !important; } 
            }
            @keyframes slideDown { from { opacity:0; transform:translateY(-10px); } to { opacity:1; transform:translateY(0); } }
            @keyframes spin { 100% { transform: rotate(360deg); } }
            .spinning { animation: spin 1s linear infinite; opacity: 1!important; }
            .dropdown-header { display:flex; justify-content:space-between; padding:16px 20px; border-bottom: 1px solid var(--ns-border); background: rgba(0,0,0,0.3); align-items:center; flex-shrink: 0; }
            .header-title { font-weight: 700; font-size: 15px; letter-spacing: 0.5px; }
            .header-tools { display:flex; gap:15px; }
            .tool-icon { cursor:pointer; opacity:0.6; transition:opacity 0.2s; font-size: 18px; }
            .tool-icon:hover { opacity:1; }
            .filter-bar { padding: 10px 20px; display: flex; gap: 8px; border-bottom: 1px solid var(--ns-border); overflow-x: auto; scrollbar-width: none; flex-shrink: 0; }
            .filter-pill { font-size: 11px; padding: 4px 12px; border-radius: 20px; background: rgba(255,255,255,0.05); cursor: pointer; transition: all 0.2s; border: 1px solid transparent; white-space: nowrap; }
            .filter-pill.active { background: #fff; color: #000; font-weight: 700; box-shadow: 0 0 10px rgba(255,255,255,0.2); }
            .list-container { 
                max-height: 500px; 
                overflow-y: auto; 
                -webkit-overflow-scrolling: touch;
                content-visibility: auto; 
                contain-intrinsic-size: 500px;
            }
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
            .dropdown-title { 
                font-weight:600; font-size:13px; margin-bottom:4px; 
                white-space: normal; line-height: 1.2;
                display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
            }
            .dropdown-subtitle { font-size:11px; color:#aaa; white-space: normal; line-height: 1.3; }
            .hero-section { height: 160px; position: relative; cursor: pointer; display: flex; align-items: flex-end; margin-bottom: -1px; flex-shrink: 0; }
            .hero-bg { position: absolute; inset: 0; background-size: cover; background-position: center top; transition: transform 5s ease; }
            .hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, var(--ns-glass) 5%, transparent 100%); }
            .hero-content { position: relative; z-index: 2; padding: 20px; width: 100%; }
            .hero-badge { background: var(--ns-red); color: #fff; font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 3px; display: inline-block; margin-bottom: 5px; box-shadow: 0 2px 5px rgba(0,0,0,0.5); }
            .footer-tools { padding: 10px; text-align: center; border-top: 1px solid var(--ns-border); font-size: 11px; color: #888; cursor: pointer; transition: color 0.2s; flex-shrink: 0; }
            .footer-tools:hover { color: #fff; text-decoration: underline; }
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
    };

    const getUserId = () => window.ApiClient.getCurrentUserId();

    const processGrouping = (items) => {
        const seriesMap = new Map();
        const result = [];

        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if (item.Type === 'Episode' && item.SeriesId) {
                let group = seriesMap.get(item.SeriesId);
                if (!group) {
                    group = [];
                    seriesMap.set(item.SeriesId, group);
                }
                group.push(item);
            } else {
                result.push(item);
            }
        }

        seriesMap.forEach((eps) => {
            eps.sort((a,b) => new Date(b.DateCreated) - new Date(a.DateCreated));
            let newCount = 0;
            for(let e of eps) { if(e.IsNew) newCount++; }

            if (newCount > 1) {
                const latest = eps[0];
                result.push({
                    ...latest,
                    IsGroup: true,
                    GroupCount: newCount,
                    GroupIds: eps.filter(e => e.IsNew).map(e => e.Id),
                    Name: latest.SeriesName, 
                    Id: latest.SeriesId,
                    BackdropImageTags: latest.BackdropImageTags,
                    IsNew: true
                });
            } 
            else if (eps.length > 0) {
                result.push(eps[0]);
            }
        });

        return result.sort((a,b) => new Date(b.DateCreated) - new Date(a.DateCreated));
    };

    const fetchLastSeen = async () => {
        try {
            const res = await fetch(`/NotifySync/LastSeen/${getUserId()}`, { headers: getAuthHeaders() });
            const dateStr = await res.text();
            lastSeenDate = new Date(JSON.parse(dateStr));
        } catch(e) { lastSeenDate = new Date(0); }
    };

    const updateLastSeen = async () => {
        const now = new Date().toISOString();
        await fetch(`/NotifySync/LastSeen/${getUserId()}?date=${encodeURIComponent(now)}`, { 
            method: 'POST',
            headers: getAuthHeaders()
        });
        lastSeenDate = new Date();
        // Update local state
        currentData.forEach(i => i.IsNew = false);
        groupedData = processGrouping(currentData);
        updateBadge();
        closeDropdown();
    };

    const refreshPlayStates = async () => {
        if (!currentData.length) return;
        const ids = currentData.map(i => i.Id); 
        try {
            const res = await fetch(`/NotifySync/BulkUserData?userId=${getUserId()}`, {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify(ids)
            });

            if(res.ok) {
                const statusMap = await res.json();
                currentData.forEach(item => {
                    if (statusMap.hasOwnProperty(item.Id)) item.Played = statusMap[item.Id];
                    item.IsNew = !item.Played && (new Date(item.DateCreated) > lastSeenDate);
                });
                groupedData = processGrouping(currentData);
                updateBadge();
            }
        } catch(e) { console.error("Bulk check failed", e); }
    };

    const fetchData = async () => {
        if (isFetching) return; 
        isFetching = true;
        try {
            await fetchLastSeen();
            
            // ETAG & CACHE HANDLING
            const lastEtag = localStorage.getItem('ns-etag') || '';
            const headers = getAuthHeaders();
            if(lastEtag) headers['If-None-Match'] = lastEtag;

            const res = await fetch('/NotifySync/Data', { headers: headers });
            
            if (res.status === 304) {
                await refreshPlayStates();
                console.debug("NotifySync: 304 Not Modified");
            }
            else if (res.ok) {
                const json = await res.json();
                currentData = json;
                
                // Save to Cache
                const newEtag = res.headers.get('ETag');
                if(newEtag) localStorage.setItem('ns-etag', newEtag);
                localStorage.setItem('ns-data', JSON.stringify(currentData));

                await refreshPlayStates(); 
                retryDelay = 2000;
            }
            
            const drop = document.getElementById('notification-dropdown');
            if(drop && drop.style.display === 'flex') updateList(drop);

        } catch (e) { 
            setTimeout(fetchData, retryDelay);
            retryDelay = Math.min(retryDelay * 2, 60000); 
        } finally { isFetching = false; }
    };
    
    // Initial Load from Cache (Instant UI)
    const loadFromCache = () => {
        try {
            const cached = localStorage.getItem('ns-data');
            if (cached) {
                currentData = JSON.parse(cached);
                groupedData = processGrouping(currentData);
                updateBadge();
            }
        } catch(e) { console.error("Cache load error", e); }
    };

    const triggerHardRefresh = async () => {
        const btn = document.querySelector('.tool-icon.refresh-icon');
        if(btn) btn.classList.add('spinning');
        try {
            await fetch('/NotifySync/Refresh', { method: 'POST', headers: getAuthHeaders() });
            localStorage.removeItem('ns-etag');
            await new Promise(r => setTimeout(r, 500));
            await fetchData();
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

    const formatEpisodeTitle = (item) => {
        const s = item.ParentIndexNumber ? `S${item.ParentIndexNumber.toString().padStart(2,'0')}` : '';
        const e = item.IndexNumber ? `E${item.IndexNumber.toString().padStart(2,'0')}` : '';
        const se = (s || e) ? `${s}${e}` : '';
        if (se && item.Name.indexOf(se) !== -1) return item.Name;
        return se ? `${se} - ${item.Name}` : item.Name;
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
        
        // --- HERO ---
        if (hero) {
            const isGroup = !!hero.IsGroup; 
            const tag = (hero.BackdropImageTags && hero.BackdropImageTags[0]) || '';
            
            // OPTIMISATION WEBP : &format=webp ajouté
            let heroImg = tag ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?tag=${tag}&quality=70&maxWidth=600&format=webp`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=70&maxWidth=400&format=webp`);
            if(isGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=70&maxWidth=600&format=webp`);

            let heroTitle = hero.Name;
            let heroSub = '';
            
            if (isGroup) {
                heroSub = `${hero.GroupCount} ${T.newEps}`;
            } else if (hero.Type === 'Episode') {
                heroTitle = formatEpisodeTitle(hero);
                heroSub = hero.SeriesName; 
            } else {
                heroSub = hero.ProductionYear;
            }

            const timeStr = timeAgo(hero.DateCreated);
            
            html += `
            <div class="hero-section" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${hero.Id}'}))">
                <div class="hero-bg" style="background-image:url('${heroImg}')"></div>
                <div class="hero-overlay"></div>
                <div class="hero-content">
                    ${hero.IsNew ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}
                    <div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">
                        ${heroTitle}
                    </div>
                    <div style="font-size:12px;opacity:0.8;margin-top:4px">
                        ${heroSub} &bull; ${timeStr}
                    </div>
                </div>
            </div>`;
        }

        // --- LISTE ---
        filtered.filter(x => x !== hero).forEach(item => {
            const isMusic = item.Category === 'Music';
            const isGroup = !!item.IsGroup; 
            
            const imgTag = item.PrimaryImageTag || '';
            const imgOpts = isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200';
            const targetId = item.Id; 
            // OPTIMISATION WEBP : &format=webp ajouté
            const imgUrl = client.getUrl(`Items/${targetId}/Images/Primary?tag=${imgTag}&${imgOpts}&quality=80&format=webp`);
            
            let title = item.Name;
            let sub = item.ProductionYear;
            const timeStr = timeAgo(item.DateCreated);

            if (isGroup) {
                sub = `${item.GroupCount} ${T.newEps}`;
            } else if (item.Type === 'Episode') {
                title = formatEpisodeTitle(item);
                sub = item.SeriesName; 
            }

            const finalSub = `${sub} &bull; ${timeStr}`;

            html += `
            <div class="dropdown-item ${item.IsNew ? 'style-new' : 'style-seen'}" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${targetId}'}))">
                <div class="status-dot"></div>
                <div class="thumb-wrapper">
                    <img data-src="${imgUrl}" class="dropdown-thumb ${isMusic?'music':''}" loading="lazy" onerror="this.style.display='none'">
                    <span class="material-icons" style="color:#444;position:absolute;z-index:-1;">${isMusic?'album':'movie'}</span>
                </div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    <div class="dropdown-subtitle">${finalSub}</div>
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

    const closeDropdown = () => {
        const drop = document.getElementById('notification-dropdown');
        const back = document.getElementById('notify-backdrop');
        if(drop) drop.style.display = 'none';
        if(back) back.style.display = 'none';
    };

    const toggleDropdown = () => {
        let drop = document.getElementById('notification-dropdown');
        const backdrop = document.getElementById('notify-backdrop');
        
        if(!drop) {
            drop = document.createElement('div'); drop.id = 'notification-dropdown';
            document.body.appendChild(drop);
            document.addEventListener('ns-filter', (e) => { activeFilter = e.detail; updateList(drop); });
            document.addEventListener('ns-markall', () => { updateLastSeen(); closeDropdown(); });
            document.addEventListener('ns-refresh', () => { triggerHardRefresh(); });
            document.addEventListener('ns-navigate', (e) => {
                window.location.hash = '#!/details?id=' + e.detail;
                closeDropdown();
            });

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
            const b = document.createElement('div'); b.id = 'notify-backdrop'; b.onclick = closeDropdown; document.body.appendChild(b);
        }

        const isOpen = drop.style.display === 'flex';
        if (!isOpen) {
            fetchData().then(() => updateList(drop));
            document.getElementById('notify-backdrop').style.display = 'block';
            drop.style.display = 'flex'; 
        } else {
            closeDropdown();
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
        
        loadFromCache();
        fetchData();
    };

    const obs = new MutationObserver(() => { if(!document.getElementById('bell-container')) installBell(); });
    obs.observe(document.body, { childList: true, subtree: true });
    
    setInterval(() => { if(document.visibilityState === 'visible') fetchData(); }, 60000);

    installBell();
})();