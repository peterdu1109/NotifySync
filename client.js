/* NOTIFYSYNC V4.6.5 */
(function () {
    let currentData = [];
    let groupedData = [];
    let lastSeenDate = new Date(0);
    let isFetching = false;
    let retryDelay = 2000;
    let activeFilter = 'All';
    let observerInstance = null; 
    
    const userLang = navigator.language || 'en';
    const T = userLang.startsWith('fr') 
        ? { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", markAll: "Tout marquer comme vu", badgeNew: "NOUVEAU", newEps: "nouveaux épisodes", eps: "épisodes", filterAll: "Tout", filterMovie: "Films", filterSeries: "Séries", filterMusic: "Musique" }
        : { header: "What's New?", empty: "You're all caught up!", markAll: "Mark all read", badgeNew: "NEW", newEps: "new episodes", eps: "episodes", filterAll: "All", filterMovie: "Movies", filterSeries: "Series", filterMusic: "Music" };

    const rtf = new Intl.RelativeTimeFormat(userLang, { numeric: 'auto' });
    
    const timeAgo = (date) => {
        const diff = (new Date(date) - new Date()) / 1000;
        if (Math.abs(diff) < 60) return rtf.format(Math.round(diff), 'second');
        if (Math.abs(diff) < 3600) return rtf.format(Math.round(diff / 60), 'minute');
        if (Math.abs(diff) < 86400) return rtf.format(Math.round(diff / 3600), 'hour');
        if (Math.abs(diff) < 2592000) return rtf.format(Math.round(diff / 86400), 'day');
        if (Math.abs(diff) < 31536000) return rtf.format(Math.round(diff / 2592000), 'month');
        return rtf.format(Math.round(diff / 31536000), 'year');
    };

    const escapeHtml = (unsafe) => {
        if (!unsafe) return "";
        return unsafe
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
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
                max-height: 80vh; 
            }
            @media (max-width: 600px) { #notification-dropdown { top: 60px; right: 10px; left: 10px; width: auto; max-width: none; } }
            @media (max-height: 500px) {
                #notification-dropdown { top: 10px; bottom: 10px; right: 20px; left: auto; width: 400px; max-height: none; }
                .hero-section { height: 110px !important; flex-shrink: 0; }
                .list-container { flex: 1; overflow-y: auto; max-height: none !important; }
            }
            @media (max-height: 500px) and (max-width: 600px) { #notification-dropdown { left: 10px; right: 10px; width: auto; } }
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
            .list-container { max-height: 500px; overflow-y: auto; -webkit-overflow-scrolling: touch; content-visibility: auto; contain-intrinsic-size: 500px; flex: 1; }
            .dropdown-item { display:flex; padding:12px 20px; border-bottom:1px solid var(--ns-border); cursor:pointer; transition: background .2s; position: relative; }
            .dropdown-item:hover { background: rgba(255,255,255,0.08); }
            .status-dot { position: absolute; left: 6px; top: 50%; transform: translateY(-50%); width: 4px; height: 4px; border-radius: 50%; background: var(--ns-red); box-shadow: 0 0 5px var(--ns-red); display: none; }
            .style-new .status-dot { display: block; }
            .style-new { background: rgba(229, 9, 20, 0.05); }
            .thumb-wrapper { width:90px; height:50px; margin-right:15px; flex-shrink:0; background:#222; border-radius:6px; overflow:hidden; display:flex; justify-content:center; align-items:center; box-shadow: 0 2px 5px rgba(0,0,0,0.3); }
            .dropdown-thumb { width:100%; height:100%; object-fit:cover; opacity:0; transition:opacity 0.3s; }
            .dropdown-thumb.music { object-fit:contain; }
            .dropdown-thumb.loaded { opacity:1; }
            .dropdown-info { flex:1; display:flex; flex-direction:column; justify-content:center; min-width: 0; }
            .dropdown-title { font-weight:600; font-size:13px; margin-bottom:4px; white-space: normal; line-height: 1.2; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
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
                if (!group) { group = []; seriesMap.set(item.SeriesId, group); }
                group.push(item);
            } else { result.push(item); }
        }
        seriesMap.forEach((eps) => {
            eps.sort((a,b) => new Date(b.DateCreated) - new Date(a.DateCreated));
            if (eps.length === 0) return;
            const latest = eps[0];
            const batchWindow = 12 * 60 * 60 * 1000; 
            const recentBatch = eps.filter(e => e.IsNew && (new Date(latest.DateCreated).getTime() - new Date(e.DateCreated).getTime() < batchWindow));
            if (recentBatch.length > 1) {
                result.push({ ...latest, IsGroup: true, GroupCount: recentBatch.length, Name: latest.SeriesName, Id: latest.SeriesId, IsNew: true });
            } else { result.push(latest); }
        });
        return result.sort((a,b) => new Date(b.DateCreated) - new Date(a.DateCreated));
    };

    const fetchLastSeen = async () => {
        try {
            const res = await fetch(`/NotifySync/LastSeen/${getUserId()}`, { headers: getAuthHeaders() });
            lastSeenDate = new Date(JSON.parse(await res.text()));
        } catch(e) { lastSeenDate = new Date(0); }
    };

    const updateLastSeen = async () => {
        await fetch(`/NotifySync/LastSeen/${getUserId()}?date=${encodeURIComponent(new Date().toISOString())}`, { method: 'POST', headers: getAuthHeaders() });
        lastSeenDate = new Date();
        currentData.forEach(i => i.IsNew = false);
        groupedData = processGrouping(currentData);
        updateBadge();
        closeDropdown();
    };

    const refreshPlayStates = async () => {
        if (!currentData.length) return;
        try {
            const res = await fetch(`/NotifySync/BulkUserData?userId=${getUserId()}`, { method: 'POST', headers: getAuthHeaders(), body: JSON.stringify(currentData.map(i => i.Id)) });
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
            const lastSeenPromise = fetchLastSeen();
            const lastEtag = localStorage.getItem('ns-etag') || '';
            const headers = getAuthHeaders();
            if(lastEtag) headers['If-None-Match'] = lastEtag;
            
            // FIX PRIVACY : Envoi de l'ID utilisateur
            const dataPromise = fetch(`/NotifySync/Data?userId=${getUserId()}`, { headers: headers });

            const [_, res] = await Promise.all([lastSeenPromise, dataPromise]);
            
            if (res.status === 304) {
                await refreshPlayStates();
            }
            else if (res.ok) {
                const json = await res.json();
                currentData = json;
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
    
    const loadFromCache = () => {
        try {
            const cached = localStorage.getItem('ns-data');
            if (cached) { currentData = JSON.parse(cached); groupedData = processGrouping(currentData); updateBadge(); }
        } catch(e) {}
    };

    const triggerHardRefresh = async () => {
        const btn = document.querySelector('.tool-icon.refresh-icon');
        if(btn) btn.classList.add('spinning');
        try {
            await fetch('/NotifySync/Refresh', { method: 'POST', headers: getAuthHeaders() });
            localStorage.removeItem('ns-etag');
            await new Promise(r => setTimeout(r, 500));
            await fetchData();
        } finally { if(btn) btn.classList.remove('spinning'); }
    };

    const updateBadge = () => {
        const bell = document.getElementById('netflix-bell');
        if(!bell) return;
        let badge = bell.querySelector('.ns-badge');
        if(!badge) { badge = document.createElement('div'); badge.className = 'ns-badge'; bell.appendChild(badge); }
        const count = groupedData.filter(i => i.IsNew).length;
        if(count > 0) { badge.innerText = count > 9 ? '9+' : count; badge.classList.add('visible'); } 
        else { badge.classList.remove('visible'); }
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
        drop.querySelector('.filter-bar').innerHTML = Array.from(cats).map(c => `<div class="filter-pill ${activeFilter===c?'active':''}" onclick="document.dispatchEvent(new CustomEvent('ns-filter', {detail:'${c}'}))">${T['filter'+c]||c}</div>`).join('');

        if (filtered.length === 0) { container.innerHTML = `<div style="padding:60px 20px;text-align:center;color:#666;font-style:italic;">${T.empty}</div>`; return; }

        const htmlParts = [];
        const client = window.ApiClient;
        const hero = filtered.find(i => i.IsNew) || filtered[0];
        
        if (hero) {
            const isGroup = !!hero.IsGroup; 
            let heroImg = (hero.BackdropImageTags && hero.BackdropImageTags[0]) ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?tag=${hero.BackdropImageTags[0]}&quality=70&maxWidth=600&format=webp`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=70&maxWidth=400&format=webp`);
            if(isGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=70&maxWidth=600&format=webp`);
            
            // XSS FIX
            let heroTitle = escapeHtml(hero.Name), heroSub = '';
            if (hero.Type === 'Episode') { heroTitle = escapeHtml(formatEpisodeTitle(hero)); heroSub = escapeHtml(hero.SeriesName); } else { heroSub = hero.ProductionYear; }
            if (isGroup) { heroSub = `${escapeHtml(hero.SeriesName)} • ${hero.GroupCount} ${T.newEps}`; }
            
            htmlParts.push(`<div class="hero-section" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${hero.Id}'}))"><div class="hero-bg" style="background-image:url('${heroImg}')"></div><div class="hero-overlay"></div><div class="hero-content">${hero.IsNew ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}<div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">${heroTitle}</div><div style="font-size:12px;opacity:0.8;margin-top:4px">${heroSub} &bull; ${timeAgo(hero.DateCreated)}</div></div></div>`);
        }

        filtered.filter(x => x !== hero).forEach(item => {
            const isMusic = item.Category === 'Music';
            const isGroup = !!item.IsGroup; 
            const imgUrl = client.getUrl(`Items/${item.Id}/Images/Primary?tag=${item.PrimaryImageTag || ''}&${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`);
            
            // XSS FIX
            let title = escapeHtml(item.Name), sub = item.ProductionYear;
            if (item.Type === 'Episode') { title = escapeHtml(formatEpisodeTitle(item)); sub = escapeHtml(item.SeriesName); }
            if (isGroup) { sub = `${escapeHtml(item.SeriesName)} • ${item.GroupCount} ${T.newEps}`; }
            
            htmlParts.push(`<div class="dropdown-item ${item.IsNew ? 'style-new' : 'style-seen'}" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${item.Id}'}))"><div class="status-dot"></div><div class="thumb-wrapper"><img data-src="${imgUrl}" decoding="async" class="dropdown-thumb ${isMusic?'music':''}" loading="lazy" onerror="this.style.display='none'"><span class="material-icons" style="color:#444;position:absolute;z-index:-1;">${isMusic?'album':'movie'}</span></div><div class="dropdown-info"><div class="dropdown-title">${title}</div><div class="dropdown-subtitle">${sub} &bull; ${timeAgo(item.DateCreated)}</div></div></div>`);
        });

        htmlParts.push(`<div class="footer-tools" onclick="document.dispatchEvent(new Event('ns-markall'))">${T.markAll}</div>`);
        const finalHtml = htmlParts.join('');
        if (container.innerHTML !== finalHtml) {
            container.innerHTML = finalHtml;
            const obs = new IntersectionObserver((entries, o) => { entries.forEach(e => { if (e.isIntersecting) { const i = e.target; i.src = i.dataset.src; i.classList.add('loaded'); o.unobserve(i); } }); });
            container.querySelectorAll('img[data-src]').forEach(i => obs.observe(i));
        }
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
            document.addEventListener('ns-navigate', (e) => { closeDropdown(); window.location.hash = '#!/details?id=' + e.detail; });

            drop.innerHTML = `<div class="dropdown-header"><span class="header-title">${T.header}</span><div class="header-tools"><span class="material-icons tool-icon refresh-icon" onclick="document.dispatchEvent(new Event('ns-refresh'))">refresh</span></div></div><div class="filter-bar"></div><div class="list-container"></div>`;
        }
        if(!backdrop) { const b = document.createElement('div'); b.id = 'notify-backdrop'; b.onclick = closeDropdown; document.body.appendChild(b); }

        if (drop.style.display !== 'flex') {
            fetchData().then(() => updateList(drop));
            document.getElementById('notify-backdrop').style.display = 'block';
            drop.style.display = 'flex'; 
        } else { closeDropdown(); }
    };

    const installBell = () => {
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right');
        if (!header || document.getElementById('bell-container')) {
            if(document.getElementById('bell-container') && observerInstance) { observerInstance.disconnect(); observerInstance = null; monitorBellDisappearance(); }
            return;
        }
        injectStyles();
        const div = document.createElement('div'); div.id = 'bell-container';
        div.innerHTML = `<button id="netflix-bell" class="paper-icon-button-light headerButton"><span class="material-icons notifications"></span></button>`;
        div.firstChild.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toggleDropdown(); };
        header.prepend(div);
        loadFromCache();
        fetchData();
    };

    const monitorBellDisappearance = () => {
        const obs = new MutationObserver(() => { if(!document.getElementById('bell-container')) { obs.disconnect(); startMainObserver(); } });
        obs.observe(document.body, { childList: true, subtree: true });
    };

    const startMainObserver = () => {
        observerInstance = new MutationObserver(() => installBell());
        observerInstance.observe(document.body, { childList: true, subtree: true });
        installBell();
    };
    
    setInterval(() => { if(document.visibilityState === 'visible') fetchData(); }, 60000);
    startMainObserver();
})();