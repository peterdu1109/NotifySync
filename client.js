/* NOTIFYSYNC V5.4.0.0 */
(function () {
    let currentData = [];
    let groupedData = [];
    let lastSeenDate = new Date(0);
    let isFetching = false;
    let retryDelay = 2000;
    let activeFilter = 'All';
    let observerInstance = null;
    let pollTimeout = null;
    let lastFetchTime = 0;
    let eventsRegistered = false;
    let lastPulseTime = 0;
    let previousDataIds = new Set();
    let localPlayed = JSON.parse(localStorage.getItem('ns-played') || '[]');
    let localPlayedDirty = false;
    let localPlayedSaveTimer = null;

    const flushLocalPlayed = () => {
        if (localPlayedDirty) {
            localStorage.setItem('ns-played', JSON.stringify(localPlayed));
            localPlayedDirty = false;
        }
    };

    const markLocalPlayed = (id) => {
        if (id && !localPlayed.includes(id)) {
            localPlayed.push(id);
            if (localPlayed.length > 500) localPlayed.shift();
            localPlayedDirty = true;
            clearTimeout(localPlayedSaveTimer);
            localPlayedSaveTimer = setTimeout(flushLocalPlayed, 1000);
        }
    };

    const markReadOnServer = async (itemIds) => {
        const userId = getUserId();
        if (!userId || !itemIds || itemIds.length === 0) return;
        try {
            await fetch(`/NotifySync/MarkRead?userId=${userId}`, {
                method: 'POST', headers: getAuthHeaders(), body: JSON.stringify(itemIds)
            });
        } catch (e) { console.warn("NotifySync: MarkRead failed, localStorage fallback active."); }
    };

    const dismissOnServer = async (itemId) => {
        const userId = getUserId();
        if (!userId || !itemId) return false;
        try {
            const res = await fetch(`/NotifySync/Dismiss/${userId}/${itemId}`, {
                method: 'POST', headers: getAuthHeaders()
            });
            return res.ok;
        } catch (e) { console.warn("NotifySync: Dismiss failed."); return false; }
    };

    const userLang = navigator.language || 'en';
    const T = userLang.startsWith('fr')
        ? { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", clearAll: "Vider la liste", badgeNew: "NOUVEAU", newEps: "nouveaux épisodes", eps: "épisodes", newTracks: "nouvelles pistes", tracks: "pistes", filterAll: "Tout", filterMovie: "Films", filterSeries: "Séries", filterMusic: "Musique" }
        : { header: "What's New?", empty: "You're all caught up!", clearAll: "Clear list", badgeNew: "NEW", newEps: "new episodes", eps: "episodes", newTracks: "new tracks", tracks: "tracks", filterAll: "All", filterMovie: "Movies", filterSeries: "Series", filterMusic: "Music" };

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
        if (!window.ApiClient) return {};
        return {
            'Content-Type': 'application/json',
            'X-Emby-Token': window.ApiClient.accessToken()
        };
    };

    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;
        const css = `
            :root { --ns-red: #e50914; --ns-glass: rgba(20, 20, 20, 0.98); --ns-blur: 16px; --ns-border: rgba(255,255,255,0.15); }
            #netflix-bell { background:0 0;border:none;cursor:pointer;color:inherit;position:relative; transition: transform 0.2s; width:35px;height:35px;overflow:visible;display:inline-flex!important;align-items:center;justify-content:center; }
            #netflix-bell:active { transform: scale(0.9); }
            .ns-badge {
                position: absolute!important; top: 4px; right: 4px; margin: 0!important;
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
                flex-direction: column;
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
            @keyframes bellPulse { 0%, 100% { transform: scale(1); } 15% { transform: scale(1.3) rotate(-10deg); } 30% { transform: scale(1.3) rotate(10deg); } 45% { transform: scale(1.2) rotate(-5deg); } 60% { transform: scale(1.1); } }
            .ns-pulse { animation: bellPulse 0.8s ease-in-out; }
            @keyframes badgeBounce { 0% { transform: scale(1.5); } 100% { transform: scale(1); } }
            .ns-pulse .ns-badge { animation: badgeBounce 0.5s ease-out; }
            .dismiss-btn { position:absolute; top:6px; right:6px; background:rgba(255,255,255,0.1); border:none; color:#888; cursor:pointer; width:20px; height:20px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:14px; line-height:1; opacity:0; transition:opacity 0.2s, background 0.2s; z-index:2; padding:0; }
            .dropdown-item:hover .dismiss-btn { opacity:1; }
            .dismiss-btn:hover { background:rgba(255,255,255,0.2); color:#fff; }
            @keyframes dismissSlide { to { opacity:0; transform:translateX(50px); height:0; padding:0; margin:0; overflow:hidden; } }
            .dismissing { animation: dismissSlide 0.3s ease-out forwards; }
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

    const getUserId = () => {
        if (!window.ApiClient) return null;
        const userId = window.ApiClient.getCurrentUserId();
        return (userId && userId !== 'null' && userId !== 'undefined') ? userId : null;
    };

    const processGrouping = (items) => {
        const seriesMap = new Map();
        const result = [];
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            if ((item.Type === 'Episode' && item.SeriesId) || (item.Type === 'Audio' && item.SeriesId)) {
                let group = seriesMap.get(item.SeriesId);
                if (!group) { group = []; seriesMap.set(item.SeriesId, group); }
                group.push(item);
            } else { result.push(item); }
        }
        seriesMap.forEach((eps) => {
            eps.sort((a, b) => new Date(b.DateCreated) - new Date(a.DateCreated));
            if (eps.length === 0) return;
            const latest = eps[0];
            const hasNew = eps.some(e => e.IsNew);
            const newCount = eps.filter(e => e.IsNew).length;
            if (eps.length > 1) {
                result.push({ ...latest, IsGroup: true, GroupCount: eps.length, NewCount: newCount, Name: latest.SeriesName || latest.Name, Id: latest.SeriesId || latest.Id, IsNew: hasNew });
            } else { result.push(latest); }
        });
        return result.sort((a, b) => new Date(b.DateCreated) - new Date(a.DateCreated));
    };

    const fetchLastSeen = async () => {
        const userId = getUserId();
        if (!userId) return; // Keep existing lastSeenDate (from cache)

        try {
            const res = await fetch(`/NotifySync/Cleared/${userId}`, { headers: getAuthHeaders() });
            if (res.ok) {
                const text = await res.text();
                // Ensure valid date string
                if (text && text.length > 5) {
                    // Support both plain ISO string and JSON-encoded string
                    const parsed = text.startsWith('"') ? JSON.parse(text) : text;
                    lastSeenDate = new Date(parsed);
                    localStorage.setItem('ns-cleared', lastSeenDate.toISOString());
                }
            }
        } catch (e) { console.warn("NotifySync: Cleared fetch failed, using cache."); }
    };

    const applyPlayStates = (statusMap) => {
        currentData.forEach(item => {
            let isServerPlayed = statusMap ? !!statusMap[item.Id] : item.Played;
            item.Played = isServerPlayed || localPlayed.includes(item.Id);
        });
    };

    const clearAllNotifications = async () => {
        const userId = getUserId();
        if (!userId) return;

        await fetch(`/NotifySync/Clear/${userId}?date=${encodeURIComponent(new Date().toISOString())}`, { method: 'POST', headers: getAuthHeaders() });
        lastSeenDate = new Date();

        currentData = []; // Clear local data
        groupedData = [];
        updateBadge();
        closeDropdown();
    };

    const refreshPlayStates = async () => {
        const userId = getUserId();
        if (!currentData.length || !userId) return;
        try {
            const idsToCheck = new Set();
            currentData.forEach(i => {
                idsToCheck.add(i.Id);
            });
            const res = await fetch(`/NotifySync/BulkUserData?userId=${userId}`, { method: 'POST', headers: getAuthHeaders(), body: JSON.stringify(Array.from(idsToCheck)) });
            if (res.ok) {
                const statusMap = await res.json();
                applyPlayStates(statusMap);
            }
        } catch (e) { console.error("Bulk check failed", e); }
    };

    const recalculateNewStatus = () => {
        currentData.forEach(item => {
            // IsNew = not read on server AND not played in Jellyfin AND not locally marked
            item.IsNew = !item.IsRead && !item.Played && !localPlayed.includes(item.Id);
        });
        groupedData = processGrouping(currentData);
        updateBadge();
    };

    const fetchData = async () => {
        if (isFetching) return;

        // Throttle: minimum 3 seconds between successful fetches
        const now = Date.now();
        if (now - lastFetchTime < 3000) return;

        const userId = getUserId();
        if (!userId) {
            console.warn("NotifySync: No userId auth found yet. Retrying in " + retryDelay + "ms");
            // No user ID yet (e.g. not logged in fully), retry with backoff
            if (pollTimeout) clearTimeout(pollTimeout);
            pollTimeout = setTimeout(fetchData, retryDelay);
            retryDelay = Math.min(retryDelay * 1.5, 60000); // 2s, 3s, 4.5s... max 60s
            return;
        }

        if (isFetching) return;
        console.log("NotifySync: Fetching data for UserID:", userId);
        isFetching = true;
        try {
            const lastSeenPromise = fetchLastSeen();
            const lastEtag = localStorage.getItem('ns-etag') || '';
            const headers = getAuthHeaders();
            if (lastEtag) headers['If-None-Match'] = lastEtag;

            const dataPromise = fetch(`/NotifySync/Data?userId=${userId}`, { headers: headers });

            const [_, res] = await Promise.all([lastSeenPromise, dataPromise]);

            if (res.status === 304) {
                console.log("NotifySync: Data 304 Not Modified");
                // Data unchanged, just refresh play states and recalculate
                await refreshPlayStates();
                recalculateNewStatus();
            }
            else if (res.ok) {
                const json = await res.json();
                console.log("NotifySync: Data received", json.length, "items");

                // Detect new items for pulse animation
                const newIds = new Set(json.map(i => i.Id));
                const hasNewItems = json.some(i => !previousDataIds.has(i.Id));

                currentData = json;
                const newEtag = res.headers.get('ETag');
                if (newEtag) localStorage.setItem('ns-etag', newEtag);
                localStorage.setItem('ns-data', JSON.stringify(currentData));

                // First show badge immediately with available data
                recalculateNewStatus();

                // Then fetch accurate play states and refresh
                await refreshPlayStates();
                recalculateNewStatus();
                retryDelay = 2000;

                // Pulse animation if new items arrived and dropdown is closed
                if (hasNewItems && previousDataIds.size > 0) {
                    const drop = document.getElementById('notification-dropdown');
                    const dropdownOpen = drop && drop.style.display === 'flex';
                    const now = Date.now();
                    if (!dropdownOpen && (now - lastPulseTime > 30000)) {
                        lastPulseTime = now;
                        const bell = document.getElementById('netflix-bell');
                        if (bell) {
                            bell.classList.add('ns-pulse');
                            setTimeout(() => bell.classList.remove('ns-pulse'), 2000);
                        }
                    }
                }
                previousDataIds = newIds;
            } else {
                console.error("NotifySync: Fetch failed", res.status, res.statusText);
            }

            const drop = document.getElementById('notification-dropdown');
            if (drop && drop.style.display === 'flex') updateList(drop);

            lastFetchTime = Date.now();
        } catch (e) {
            console.error("NotifySync: Error in fetchData", e);
            if (pollTimeout) clearTimeout(pollTimeout);
            pollTimeout = setTimeout(fetchData, retryDelay);
            retryDelay = Math.min(retryDelay * 2, 60000);
        } finally { isFetching = false; }
    };

    const loadFromCache = () => {
        try {
            // Restore lastSeenDate from localStorage first
            const cachedCleared = localStorage.getItem('ns-cleared');
            if (cachedCleared) {
                lastSeenDate = new Date(cachedCleared);
            }

            const cached = localStorage.getItem('ns-data');
            if (cached) {
                currentData = JSON.parse(cached);
                previousDataIds = new Set(currentData.map(i => i.Id));
                applyPlayStates();
                // Recalculate IsNew with IsRead from server + local state
                recalculateNewStatus();
            }
        } catch (e) { }
    };

    const triggerHardRefresh = async () => {
        const btn = document.querySelector('.tool-icon.refresh-icon');
        if (btn) btn.classList.add('spinning');
        try {
            await fetch('/NotifySync/Refresh', { method: 'POST', headers: getAuthHeaders() });
            localStorage.removeItem('ns-etag');
            // Le serveur lance un Task.Run en background. 
            // On laisse le WebSocketMessage ("LibraryChanged" ou "UserDataChanged") nous notifier 
            // lorsque le scan aura modifié la base de données.
            // On enlève le "spinning" manuellement au bout de 2 sec par pure esthétique UX.
            setTimeout(() => {
                if (btn) btn.classList.remove('spinning');
            }, 2000);
        } catch (e) {
            if (btn) btn.classList.remove('spinning');
        }
    };

    const updateBadge = () => {
        const bell = document.getElementById('netflix-bell');
        if (!bell) return;
        let badge = bell.querySelector('.ns-badge');
        if (!badge) { badge = document.createElement('div'); badge.className = 'ns-badge'; bell.appendChild(badge); }
        const count = groupedData.filter(i => i.IsNew).length;
        if (count > 0) { badge.innerText = count > 9 ? '9+' : count; badge.classList.add('visible'); }
        else { badge.classList.remove('visible'); }
    };

    const formatEpisodeTitle = (item) => {
        const s = item.ParentIndexNumber ? `S${item.ParentIndexNumber.toString().padStart(2, '0')}` : '';
        const e = item.IndexNumber ? `E${item.IndexNumber.toString().padStart(2, '0')}` : '';
        const se = (s || e) ? `${s}${e}` : '';
        if (se && item.Name.indexOf(se) !== -1) return item.Name;
        return se ? `${se} - ${item.Name}` : item.Name;
    };

    const updateList = (drop) => {
        if (!drop) return;
        const container = drop.querySelector('.list-container');
        let filtered = groupedData || [];
        if (activeFilter !== 'All') filtered = filtered.filter(i => i.Category === activeFilter);
        const cats = new Set(['All']); groupedData.forEach(i => cats.add(i.Category));
        drop.querySelector('.filter-bar').innerHTML = Array.from(cats).map(c => `<div class="filter-pill ${activeFilter === c ? 'active' : ''}" onclick="document.dispatchEvent(new CustomEvent('ns-filter', {detail:'${escapeHtml(c)}'}))">${T['filter' + c] || escapeHtml(c)}</div>`).join('');

        if (filtered.length === 0) { container.innerHTML = `<div style="padding:60px 20px;text-align:center;color:#666;font-style:italic;">${T.empty}</div>`; return; }

        const htmlParts = [];
        const client = window.ApiClient;
        const hero = filtered.find(i => i.IsNew) || filtered[0];

        if (hero) {
            const isGroup = !!hero.IsGroup;
            let heroImg = (hero.BackdropImageTags && hero.BackdropImageTags[0]) ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?tag=${hero.BackdropImageTags[0]}&quality=70&maxWidth=600&format=webp`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=70&maxWidth=400&format=webp`);
            if (isGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=70&maxWidth=600&format=webp`);

            let heroTitle = escapeHtml(hero.Name), heroSub = '';
            if (!isGroup && hero.Type === 'Episode') {
                heroTitle = escapeHtml(formatEpisodeTitle(hero)); heroSub = escapeHtml(hero.SeriesName);
            } else {
                heroSub = hero.ProductionYear;
            }
            if (isGroup) {
                const isMusic = hero.Type === 'Audio';
                const lbl = isMusic ? (hero.IsNew ? T.newTracks : T.tracks) : (hero.IsNew ? T.newEps : T.eps);
                heroSub = hero.IsNew ? `${hero.NewCount || hero.GroupCount} ${lbl}` : `${hero.GroupCount} ${lbl}`;
            }

            htmlParts.push(`<div class="hero-section" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${hero.Id}'}))"><div class="hero-bg" style="background-image:url('${heroImg}')"></div><div class="hero-overlay"></div><div class="hero-content">${hero.IsNew ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}<div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">${heroTitle}</div><div style="font-size:12px;opacity:0.8;margin-top:4px">${heroSub} &bull; ${timeAgo(hero.DateCreated)}</div></div></div>`);
        }

        filtered.filter(x => x !== hero).forEach(item => {
            const isMusic = item.Type === 'Audio';
            const isGroup = !!item.IsGroup;
            const imgUrl = client.getUrl(`Items/${item.Id}/Images/Primary?tag=${item.PrimaryImageTag || ''}&${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`);

            let title = escapeHtml(item.Name), sub = item.ProductionYear;
            if (!isGroup && item.Type === 'Episode') { title = escapeHtml(formatEpisodeTitle(item)); sub = escapeHtml(item.SeriesName); }
            if (isGroup) {
                const lbl = isMusic ? (item.IsNew ? T.newTracks : T.tracks) : (item.IsNew ? T.newEps : T.eps);
                sub = item.IsNew ? `${item.NewCount || item.GroupCount} ${lbl}` : `${item.GroupCount} ${lbl}`;
            }

            htmlParts.push(`<div class="dropdown-item ${item.IsNew ? 'style-new' : 'style-seen'}" data-item-id="${item.Id}" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${item.Id}'}))"><div class="status-dot"></div><button class="dismiss-btn" title="Dismiss" onclick="event.stopPropagation(); document.dispatchEvent(new CustomEvent('ns-dismiss', {detail: '${item.Id}'}))">&times;</button><div class="thumb-wrapper"><img data-src="${imgUrl}" decoding="async" class="dropdown-thumb ${isMusic ? 'music' : ''}" loading="lazy" onerror="this.style.display='none'"><span class="material-icons" style="color:#444;position:absolute;z-index:-1;">${isMusic ? 'album' : 'movie'}</span></div><div class="dropdown-info"><div class="dropdown-title">${title}</div><div class="dropdown-subtitle">${sub} &bull; ${timeAgo(item.DateCreated)}</div></div></div>`);
        });

        htmlParts.push(`<div class="footer-tools" onclick="document.dispatchEvent(new Event('ns-clearall'))">${T.clearAll}</div>`);
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
        if (drop) drop.style.display = 'none';
        if (back) back.style.display = 'none';
    };

    const toggleDropdown = () => {
        let drop = document.getElementById('notification-dropdown');
        const backdrop = document.getElementById('notify-backdrop');

        if (!drop) {
            drop = document.createElement('div'); drop.id = 'notification-dropdown';
            document.body.appendChild(drop);

            if (!eventsRegistered) {
                document.addEventListener('ns-filter', (e) => { activeFilter = e.detail; const d = document.getElementById('notification-dropdown'); if (d) updateList(d); });
                document.addEventListener('ns-clearall', () => { clearAllNotifications(); });
                document.addEventListener('ns-refresh', () => { triggerHardRefresh(); });
                document.addEventListener('ns-navigate', (e) => {
                    const id = e.detail;
                    closeDropdown();
                    window.location.hash = '#!/details?id=' + id;
                });
                document.addEventListener('ns-dismiss', async (e) => {
                    const itemId = e.detail;
                    // Optimistic UI: animate out immediately
                    const el = document.querySelector(`.dropdown-item[data-item-id="${itemId}"]`);
                    if (el) el.classList.add('dismissing');

                    const success = await dismissOnServer(itemId);
                    if (success) {
                        // Remove from local data
                        currentData = currentData.filter(i => i.Id !== itemId);
                        localStorage.setItem('ns-data', JSON.stringify(currentData));
                        localStorage.removeItem('ns-etag'); // Force fresh fetch next time
                        recalculateNewStatus();
                        setTimeout(() => {
                            const d = document.getElementById('notification-dropdown');
                            if (d) updateList(d);
                        }, 300); // Wait for animation to finish
                    } else {
                        // Revert animation on failure
                        if (el) el.classList.remove('dismissing');
                    }
                });
                eventsRegistered = true;
            }

            drop.innerHTML = `<div class="dropdown-header"><span class="header-title">${T.header}</span><div class="header-tools"><span class="material-icons tool-icon refresh-icon" onclick="document.dispatchEvent(new Event('ns-refresh'))">refresh</span></div></div><div class="filter-bar"></div><div class="list-container"></div>`;
        }
        if (!backdrop) { const b = document.createElement('div'); b.id = 'notify-backdrop'; b.onclick = closeDropdown; document.body.appendChild(b); }

        if (drop.style.display !== 'flex') {
            fetchData().then(() => {
                updateList(drop);
                
                // Mark all as read: optimistic local update + server sync
                const unreadIds = [];
                currentData.forEach(i => {
                    if (!i.IsRead && !i.Played && !localPlayed.includes(i.Id)) {
                        markLocalPlayed(i.Id); // Optimistic local cache
                        i.IsRead = true;       // Update local state immediately
                        unreadIds.push(i.Id);
                    }
                });

                if (unreadIds.length > 0) {
                    recalculateNewStatus(); // Remove red dots and badge immediately
                    updateList(drop);       // Refresh the list UI
                    // Sync to server in background (non-blocking)
                    markReadOnServer(unreadIds);
                }
            });
            document.getElementById('notify-backdrop').style.display = 'block';
            drop.style.display = 'flex';
        } else { closeDropdown(); }
    };

    const installBell = () => {
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right') || document.querySelector('.emby-header-right') || document.querySelector('.skinHeader-content');
        if (!header || document.getElementById('netflix-bell')) {
            if (document.getElementById('netflix-bell') && observerInstance) { observerInstance.disconnect(); observerInstance = null; monitorBellDisappearance(); }
            return;
        }
        injectStyles();
        const bellBtn = document.createElement('button');
        bellBtn.id = 'netflix-bell';
        bellBtn.className = 'paper-icon-button-light headerButton headerButtonRight';
        bellBtn.innerHTML = '<span class="material-icons notifications"></span>';
        bellBtn.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toggleDropdown(); };
        header.prepend(bellBtn);
        loadFromCache();

        // Initial fetch attempt
        fetchData();
    };

    const monitorBellDisappearance = () => {
        const obs = new MutationObserver(() => { if (!document.getElementById('netflix-bell')) { obs.disconnect(); startMainObserver(); } });
        obs.observe(document.body, { childList: true, subtree: true });
    };

    const startMainObserver = () => {
        observerInstance = new MutationObserver(() => installBell());
        observerInstance.observe(document.body, { childList: true, subtree: true });
        installBell();
    };

    // --- NEW: WebSockets Real-Time Sync ---
    let wsDebounceTimeout = null;
    let wsFollowUp1 = null;
    let wsFollowUp2 = null;
    const onWebSocketMessage = (e, msg) => {
        if (!msg || !msg.MessageType) return;

        // Listen to relevant real-time server events
        if (msg.MessageType === "LibraryChanged" || msg.MessageType === "UserDataChanged") {
            console.log("NotifySync: Intercepted WebSocket event ->", msg.MessageType);

            // Reset all timers on each event
            if (wsDebounceTimeout) clearTimeout(wsDebounceTimeout);
            if (wsFollowUp1) clearTimeout(wsFollowUp1);
            if (wsFollowUp2) clearTimeout(wsFollowUp2);

            wsDebounceTimeout = setTimeout(() => {
                retryDelay = 2000;
                fetchData();
                // Jellyfin propagates Played to episodes one-by-one (~15s for a season).
                // Multi-stage follow-up to catch stragglers.
                wsFollowUp1 = setTimeout(() => fetchData(), 5000);
                wsFollowUp2 = setTimeout(() => fetchData(), 12000);
            }, 2000);
        }
    };

    const setupEvents = () => {
        if (window.Events && window.ApiClient) {
            window.Events.on(window.ApiClient, "websocketmessage", onWebSocketMessage);

            // Re-fetch data instantly when user logs in or reconnects
            window.Events.on(window.ApiClient, "authenticated", () => {
                console.log("NotifySync: User authenticated! Fetching data immediately.");
                retryDelay = 1000;
                fetchData();
            });

            console.log("NotifySync: Events successfully hooked.");
        } else {
            console.warn("NotifySync: window.Events or ApiClient not ready. Retrying...");
            setTimeout(setupEvents, 2000);
        }
    };

    document.addEventListener('viewshow', () => {
        console.log("NotifySync: View changed, checking auth...");
        retryDelay = 1000; // Reset backoff to be more aggressive
        fetchData();
    });

    // Handle SPA navigation visibility changes
    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) {
            retryDelay = 1000;
            fetchData();
        }
    });

    setupEvents();
    startMainObserver();
})();