/* NOTIFYSYNC V5.5.11.8 */
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
    let lazyImageObserver = null;

    const markReadOnServer = async (itemIds) => {
        const userId = getUserId();
        if (!userId || !itemIds || itemIds.length === 0) return;
        try {
            await fetch(`/NotifySync/MarkRead?userId=${userId}`, {
                method: 'POST', headers: getAuthHeaders(), body: JSON.stringify(itemIds)
            });
        } catch (e) { /* MarkRead failed silently */ }
    };

    const dismissOnServer = async (itemId) => {
        const userId = getUserId();
        if (!userId || !itemId) return false;
        try {
            const res = await fetch(`/NotifySync/Dismiss/${userId}/${itemId}`, {
                method: 'POST', headers: getAuthHeaders()
            });
            return res.ok;
        } catch (e) { return false; }
    };

    const bulkDismissOnServer = async (itemIds) => {
        const userId = getUserId();
        if (!userId || !itemIds || itemIds.length === 0) return false;
        try {
            const res = await fetch(`/NotifySync/BulkDismiss/${userId}`, {
                method: 'POST', headers: getAuthHeaders(), body: JSON.stringify(itemIds)
            });
            return res.ok;
        } catch (e) { return false; }
    };

    const BADGE_DURATION_MS = 72 * 60 * 60 * 1000; // 72h — badges stay visible like Netflix

    let userLang = navigator.language || 'en';
    const strings = {
        fr: { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", clearAll: "Vider la liste", clearCat: "Vider", dismiss: "Retirer", badgeNew: "NOUVEAU", badgeUpgrade: "MAJ", newEps: "nouveaux épisodes", eps: "épisodes", updEps: "épisodes mis à jour", newTracks: "nouvelles pistes", tracks: "pistes", updTracks: "pistes mises à jour", filterAll: "Tout", filterMovie: "Films", filterSeries: "Séries", filterMusic: "Musique", kindQuality: "Qualité", kindCodec: "Codec", kindAudio: "Audio", kindMinor: "Mineur" },
        en: { header: "What's New?", empty: "You're all caught up!", clearAll: "Clear list", clearCat: "Clear", dismiss: "Dismiss", badgeNew: "NEW", badgeUpgrade: "UPD", newEps: "new episodes", eps: "episodes", updEps: "updated episodes", newTracks: "new tracks", tracks: "tracks", updTracks: "updated tracks", filterAll: "All", filterMovie: "Movies", filterSeries: "Series", filterMusic: "Music", kindQuality: "Quality", kindCodec: "Codec", kindAudio: "Audio", kindMinor: "Minor" }
    };
    let T = strings[userLang.startsWith('fr') ? 'fr' : 'en'];

    let rtf = new Intl.RelativeTimeFormat(userLang, { numeric: 'auto' });

    const detectJellyfinLang = () => {
        try {
            const userId = getUserId();
            if (!userId) return;
            const jfLang = localStorage.getItem(userId + '-language') || userLang;
            const key = jfLang.startsWith('fr') ? 'fr' : 'en';
            if (T !== strings[key]) {
                T = strings[key];
                userLang = jfLang;
                rtf = new Intl.RelativeTimeFormat(userLang, { numeric: 'auto' });
                updateBadge();
                const drop = document.getElementById('notification-dropdown');
                if (drop && drop.style.display === 'flex') updateList(drop);
            }
        } catch (e) { /* silently use browser fallback */ }
    };

    const timeAgo = (date) => {
        const d = new Date(date);
        if (isNaN(d.getTime())) return '';
        const diff = (d - new Date()) / 1000;
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
            :root { --ns-red: #e50914; --ns-upgrade: #2196F3; --ns-glass: rgba(20, 20, 20, 0.98); --ns-blur: 16px; --ns-border: rgba(255,255,255,0.15); }
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
            .dismiss-btn { position:absolute; top:6px; right:6px; background:rgba(255,255,255,0.1); border:none; color:#888; cursor:pointer; width:24px; height:24px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:16px; line-height:1; opacity:0; transition:opacity 0.2s, background 0.2s; z-index:2; padding:0; font-family:'Material Icons'; }
            .dropdown-item:hover .dismiss-btn { opacity:1; }
            .dismiss-btn:hover { background:rgba(255,255,255,0.2); color:#fff; }
            @media (pointer: coarse) { .dismiss-btn { opacity:0.6; } }
            .dismiss-btn:focus-visible { opacity:1; outline:2px solid #fff; outline-offset:2px; }
            @keyframes dismissSlide { to { opacity:0; transform:translateX(50px); height:0; padding:0; margin:0; overflow:hidden; } }
            .dismissing { animation: dismissSlide 0.3s ease-out forwards; }
            .dropdown-item { transition: transform 0.2s ease, background 0.2s; }
            .dropdown-item .swipe-delete { position:absolute; right:0; top:0; bottom:0; width:80px; background:var(--ns-red); color:#fff; display:flex; align-items:center; justify-content:center; font-size:12px; font-weight:700; opacity:0; pointer-events:none; border-radius:0 0 0 0; }
            .dropdown-item.swiping .swipe-delete { opacity:1; pointer-events:auto; }
            .dropdown-header { display:flex; justify-content:space-between; padding:16px 20px; border-bottom: 1px solid var(--ns-border); background: rgba(0,0,0,0.3); align-items:center; flex-shrink: 0; }
            .header-title { font-weight: 700; font-size: 15px; letter-spacing: 0.5px; }
            .header-tools { display:flex; gap:15px; }
            .tool-icon { cursor:pointer; opacity:0.6; transition:opacity 0.2s; font-size: 18px; }
            .tool-icon:hover { opacity:1; }
            .filter-bar { padding: 10px 20px; display: flex; gap: 8px; border-bottom: 1px solid var(--ns-border); overflow-x: auto; scrollbar-width: none; flex-shrink: 0; }
            .filter-pill { font-size: 11px; padding: 4px 12px; border-radius: 20px; background: rgba(255,255,255,0.05); cursor: pointer; transition: all 0.2s; border: 1px solid transparent; white-space: nowrap; position: relative; user-select: none; -webkit-user-select: none; }
            .filter-pill.active { background: #fff; color: #000; font-weight: 700; box-shadow: 0 0 10px rgba(255,255,255,0.2); }
            .list-container { max-height: 500px; overflow-y: auto; -webkit-overflow-scrolling: touch; content-visibility: auto; contain-intrinsic-size: 500px; flex: 1; }
            .dropdown-item { display:flex; padding:12px 20px; border-bottom:1px solid var(--ns-border); cursor:pointer; transition: background .2s; position: relative; overflow: hidden; }
            .dropdown-item:hover { background: rgba(255,255,255,0.08); }
            .item-badge { display: none; font-size: 9px; font-weight: bold; padding: 2px 5px; border-radius: 3px; color: #fff; line-height: 1; letter-spacing: 0.5px; width: fit-content; margin-bottom: 2px; box-shadow: 0 2px 5px rgba(0,0,0,0.5); text-transform: uppercase; }
            .style-new .item-badge { display: block; background: var(--ns-red); box-shadow: 0 1px 3px rgba(229,9,20,0.5); }
            .style-new { background: rgba(229, 9, 20, 0.05); }
            .style-upgrade .item-badge { display: block; background: var(--ns-upgrade); box-shadow: 0 1px 3px rgba(33,150,243,0.5); }
            .style-upgrade { background: rgba(33, 150, 243, 0.05); }
            .thumb-wrapper { width:90px; height:50px; margin-right:15px; flex-shrink:0; background:#222; border-radius:6px; overflow:hidden; display:flex; justify-content:center; align-items:center; box-shadow: 0 2px 5px rgba(0,0,0,0.3); position:relative; }
            .dropdown-thumb { width:100%; height:100%; object-fit:cover; opacity:0; transition:opacity 0.3s; position:absolute; inset:0; z-index:1; }
            .dropdown-thumb.music { object-fit:contain; }
            .dropdown-thumb.loaded { opacity:1; }
            .dropdown-info { flex:1; display:flex; flex-direction:column; justify-content:center; min-width: 0; }
            .dropdown-title { font-weight:600; font-size:13px; margin-bottom:2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; line-height: 1.2; }
            .dropdown-subtitle { font-size:11px; color:#aaa; white-space: nowrap; overflow: hidden; line-height: 1.2; display: flex; }
            .dropdown-subtitle .sub-text { overflow: hidden; text-overflow: ellipsis; flex: 1; min-width: 0; margin-left: 4px; }
            .dropdown-subtitle .sub-time { flex-shrink: 0; }
            .hero-section { height: 160px; position: relative; cursor: pointer; display: flex; align-items: flex-end; margin-bottom: -1px; flex-shrink: 0; }
            .hero-section .dismiss-btn { top:10px; right:10px; background:rgba(0,0,0,0.5); color:#fff; z-index:3; }
            .hero-section:hover .dismiss-btn { opacity:1; }
            .hero-section .dismiss-btn:hover { background:rgba(229,9,20,0.8); }
            .hero-bg { position: absolute; inset: 0; background-size: cover; background-position: center center; transition: transform 5s ease; background-color: #1a1a2e; }
            .hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, var(--ns-glass) 5%, transparent 100%); }
            .hero-content { position: relative; z-index: 2; padding: 20px; width: 100%; }
            .hero-badge { background: var(--ns-red); color: #fff; font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 3px; display: inline-block; margin-bottom: 5px; box-shadow: 0 2px 5px rgba(0,0,0,0.5); text-transform: uppercase; }
            .hero-badge-upgrade { background: var(--ns-upgrade); color: #fff; font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 3px; display: inline-block; margin-bottom: 5px; box-shadow: 0 2px 5px rgba(0,0,0,0.5); text-transform: uppercase; }
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

    const nsKey = (key) => { const uid = getUserId(); return uid ? `ns-${uid}-${key}` : `ns-${key}`; };

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
            // Separate upgrades from new episodes so they don't merge
            const upgrades = eps.filter(e => e.IsUpgrade);
            const newEps = eps.filter(e => !e.IsUpgrade);
            [newEps, upgrades].forEach(subset => {
                if (subset.length === 0) return;
                const latest = subset[0];
                const hasNew = subset.some(e => e.IsNew);
                const hasBadge = subset.some(e => e.ShowBadge);
                const newCount = subset.filter(e => e.ShowBadge).length;
                if (subset.length > 1) {
                    result.push({ ...latest, IsGroup: true, GroupCount: subset.length, NewCount: newCount, Name: latest.SeriesName || latest.Name, Id: latest.SeriesId || latest.Id, IsNew: hasNew, ShowBadge: hasBadge });
                } else { result.push(latest); }
            });
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
                    localStorage.setItem(nsKey('cleared'), lastSeenDate.toISOString());
                }
            }
        } catch (e) { /* use cache */ }
    };

    const clearAllNotifications = async () => {
        const userId = getUserId();
        if (!userId) return;

        try {
            const res = await fetch(`/NotifySync/Clear/${userId}?date=${encodeURIComponent(new Date().toISOString())}`, { method: 'POST', headers: getAuthHeaders() });
            if (!res.ok) return;
        } catch (e) { return; }

        lastSeenDate = new Date();
        currentData = [];
        groupedData = [];
        previousDataIds = new Set();
        lastFetchTime = 0;
        localStorage.removeItem(nsKey('etag'));
        updateBadge();
        const drop = document.getElementById('notification-dropdown');
        if (drop) updateList(drop);
    };

    const clearCategoryNotifications = async (category) => {
        const userId = getUserId();
        if (!userId) return;

        const idsToDismiss = currentData.filter(i => i.Category === category).map(i => i.Id);
        if (idsToDismiss.length === 0) return;

        // Dismiss all items in a single bulk request
        const success = await bulkDismissOnServer(idsToDismiss);
        if (!success) return;

        // Remove from local data
        currentData = currentData.filter(i => i.Category !== category);
        localStorage.setItem(nsKey('data'), JSON.stringify(currentData)); localStorage.setItem(nsKey('data-ts'), Date.now().toString());
        localStorage.removeItem(nsKey('etag'));
        lastFetchTime = 0;
        recalculateNewStatus();
        activeFilter = 'All';
        const drop = document.getElementById('notification-dropdown');
        if (drop) updateList(drop);
    };

    const recalculateNewStatus = () => {
        const now = Date.now();
        currentData.forEach(item => {
            // IsNew = unread (for bell counter)
            item.IsNew = !item.IsRead;
            // ShowBadge = recent item (for visual NEW/UPD badges, Netflix-style persistence)
            const age = now - new Date(item.DateCreated).getTime();
            item.ShowBadge = age < BADGE_DURATION_MS;
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
            // No user ID yet (e.g. not logged in fully), retry with backoff
            if (pollTimeout) clearTimeout(pollTimeout);
            pollTimeout = setTimeout(fetchData, retryDelay);
            retryDelay = Math.min(retryDelay * 1.5, 60000); // 2s, 3s, 4.5s... max 60s
            return;
        }

        isFetching = true;
        try {
            const lastSeenPromise = fetchLastSeen();
            const lastEtag = localStorage.getItem(nsKey('etag')) || '';
            const headers = getAuthHeaders();
            if (lastEtag) headers['If-None-Match'] = lastEtag;

            const dataPromise = fetch(`/NotifySync/Data?userId=${userId}`, { headers: headers });

            const [_, res] = await Promise.all([lastSeenPromise, dataPromise]);

            if (res.status === 304) {
                // Data unchanged, recalculate with existing state
                recalculateNewStatus();
            }
            else if (res.ok) {
                const json = await res.json();
                // Detect new items for pulse animation
                const newIds = new Set(json.map(i => i.Id));
                const hasNewItems = json.some(i => !previousDataIds.has(i.Id));

                currentData = json;
                const newEtag = res.headers.get('ETag');
                if (newEtag) localStorage.setItem(nsKey('etag'), newEtag);
                localStorage.setItem(nsKey('data'), JSON.stringify(currentData)); localStorage.setItem(nsKey('data-ts'), Date.now().toString());

                // Server already filters out played items in GetData(), no need for BulkUserData
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
            // Helper: read from scoped key, fallback to legacy unscoped key + migrate
            const getWithLegacy = (k) => {
                const scoped = nsKey(k);
                let val = localStorage.getItem(scoped);
                if (val) return val;
                const legacy = `ns-${k}`;
                if (legacy !== scoped) {
                    val = localStorage.getItem(legacy);
                    if (val) { localStorage.setItem(scoped, val); localStorage.removeItem(legacy); }
                }
                return val;
            };

            // Restore lastSeenDate from localStorage first
            const cachedCleared = getWithLegacy('cleared');
            if (cachedCleared) {
                lastSeenDate = new Date(cachedCleared);
            }

            // Check TTL (1 hour max)
            const cachedTime = parseInt(getWithLegacy('data-ts') || '0');
            if (Date.now() - cachedTime > 3600000) {
                localStorage.removeItem(nsKey('data'));
                localStorage.removeItem(nsKey('etag'));
                localStorage.removeItem(nsKey('data-ts'));
                return;
            }

            const cached = getWithLegacy('data');
            if (cached) {
                try {
                    currentData = JSON.parse(cached);
                    previousDataIds = new Set(currentData.map(i => i.Id));
                    recalculateNewStatus();
                } catch (pe) { localStorage.removeItem(nsKey('data')); localStorage.removeItem(nsKey('etag')); localStorage.removeItem(nsKey('data-ts')); }
            }
            // Migrate legacy etag if present
            getWithLegacy('etag');
        } catch (e) { }
    };

    const triggerHardRefresh = async () => {
        const btn = document.querySelector('.tool-icon.refresh-icon');
        if (btn) btn.classList.add('spinning');
        try {
            await fetch('/NotifySync/Refresh', { method: 'POST', headers: getAuthHeaders() });
            localStorage.removeItem(nsKey('etag'));
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

    // Returns a localized label for the upgrade kind (e.g. "Qualité"), or "" if none.
    const upgradeKindLabel = (item) => {
        if (!item.IsUpgrade || !item.UpgradeKind) return '';
        const map = { quality: T.kindQuality, codec: T.kindCodec, audio: T.kindAudio, minor: T.kindMinor };
        return map[item.UpgradeKind] || '';
    };

    // Returns the full badge text including the upgrade kind suffix when applicable.
    // Examples: "MAJ Qualité", "UPD Codec", or just "MAJ"/"UPD"/"NOUVEAU"/"NEW".
    const upgradeBadgeText = (item) => {
        if (!item.IsUpgrade) return T.badgeNew;
        const kind = upgradeKindLabel(item);
        return kind ? `${T.badgeUpgrade} ${kind}` : T.badgeUpgrade;
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
        const filterBar = drop.querySelector('.filter-bar');
        filterBar.innerHTML = Array.from(cats).map(c => `<div class="filter-pill ${activeFilter === c ? 'active' : ''}" data-category="${escapeHtml(c)}" tabindex="0" role="button" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();this.click()}">${T['filter' + c] || escapeHtml(c)}</div>`).join('');

        // Wire filter pills now — must stay clickable even when the current category becomes empty,
        // otherwise the user is trapped in the empty state and has to close/reopen the dropdown.
        filterBar.querySelectorAll('.filter-pill').forEach(pill => {
            pill.onclick = () => document.dispatchEvent(new CustomEvent('ns-filter', { detail: pill.dataset.category }));
        });

        if (filtered.length === 0) { container.innerHTML = `<div style="padding:60px 20px;text-align:center;color:#666;font-style:italic;">${T.empty}</div>`; return; }

        const htmlParts = [];
        const client = window.ApiClient;
        const hero = filtered.find(i => i.ShowBadge) || filtered[0];

        if (hero) {
            const isGroup = !!hero.IsGroup;
            let heroImg = (hero.BackdropImageTags && hero.BackdropImageTags[0]) ? client.getUrl(`Items/${encodeURIComponent(hero.Id)}/Images/Backdrop/0?tag=${encodeURIComponent(hero.BackdropImageTags[0])}&quality=70&fillWidth=380&fillHeight=160&format=webp`) : client.getUrl(`Items/${encodeURIComponent(hero.SeriesId || hero.Id)}/Images/Primary?quality=70&fillWidth=380&fillHeight=160&format=webp`);
            if (isGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${encodeURIComponent(hero.Id)}/Images/Backdrop/0?quality=70&fillWidth=380&fillHeight=160&format=webp`);

            let heroTitle = escapeHtml(hero.Name), heroSub = '';
            if (!isGroup && hero.Type === 'Episode') {
                heroTitle = escapeHtml(formatEpisodeTitle(hero)); heroSub = escapeHtml(hero.SeriesName);
            } else {
                heroSub = escapeHtml(String(hero.ProductionYear ?? ''));
            }
            if (isGroup) {
                const isMusic = hero.Type === 'Audio';
                const lbl = hero.IsUpgrade ? (isMusic ? T.updTracks : T.updEps) : isMusic ? (hero.ShowBadge ? T.newTracks : T.tracks) : (hero.ShowBadge ? T.newEps : T.eps);
                heroSub = hero.ShowBadge ? `${hero.NewCount || hero.GroupCount} ${lbl}` : `${hero.GroupCount} ${lbl}`;
            }

            const safeHeroId = escapeHtml(hero.Id);
            const heroNavId = escapeHtml(hero.RealItemId || hero.Id);
            const heroFallbackImg = client.getUrl(`Items/${encodeURIComponent(hero.SeriesId || hero.Id)}/Images/Primary?quality=70&fillWidth=380&fillHeight=160&format=webp`);
            htmlParts.push(`<div class="hero-section" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${heroNavId}'}))"><div class="hero-bg"><img src="${escapeHtml(heroImg)}" alt="" style="width:100%;height:100%;object-fit:cover;" onerror="if(this.dataset.fb){this.src=this.dataset.fb;this.removeAttribute('data-fb')}else{this.style.display='none'}" data-fb="${escapeHtml(heroFallbackImg)}"></div><div class="hero-overlay"></div><button class="dismiss-btn" title="${T.dismiss}" aria-label="${T.dismiss}" onclick="event.stopPropagation(); document.dispatchEvent(new CustomEvent('ns-dismiss', {detail: '${safeHeroId}'}))">close</button><div class="hero-content">${hero.IsUpgrade && hero.ShowBadge ? `<span class="hero-badge-upgrade">${escapeHtml(upgradeBadgeText(hero))}</span>` : hero.ShowBadge ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}<div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">${heroTitle}</div><div style="font-size:12px;opacity:0.8;margin-top:4px">${timeAgo(hero.DateCreated)} &bull; ${heroSub}</div></div></div>`);
        }

        filtered.filter(x => x !== hero).forEach(item => {
            const isMusic = item.Type === 'Audio';
            const isGroup = !!item.IsGroup;
            const imgUrl = client.getUrl(`Items/${encodeURIComponent(item.Id)}/Images/Primary?tag=${encodeURIComponent(item.PrimaryImageTag || '')}&${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`);
            const fallbackUrl = item.SeriesId ? client.getUrl(`Items/${encodeURIComponent(item.SeriesId)}/Images/Primary?${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`) : '';

            let title = escapeHtml(item.Name), sub = escapeHtml(String(item.ProductionYear ?? ''));
            if (!isGroup && item.Type === 'Episode') { title = escapeHtml(formatEpisodeTitle(item)); sub = escapeHtml(item.SeriesName); }
            if (isGroup) {
                const lbl = item.IsUpgrade ? (isMusic ? T.updTracks : T.updEps) : isMusic ? (item.ShowBadge ? T.newTracks : T.tracks) : (item.ShowBadge ? T.newEps : T.eps);
                sub = item.ShowBadge ? `${item.NewCount || item.GroupCount} ${lbl}` : `${item.GroupCount} ${lbl}`;
            }

            const safeId = escapeHtml(item.Id);
            const navId = escapeHtml(item.RealItemId || item.Id);
            const badgeHtml = item.ShowBadge ? `<span class="item-badge">${escapeHtml(upgradeBadgeText(item))}</span>` : '';
            htmlParts.push(`<div class="dropdown-item ${item.IsUpgrade && item.ShowBadge ? 'style-upgrade' : item.ShowBadge ? 'style-new' : 'style-seen'}" data-item-id="${safeId}" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${navId}'}))"><button class="dismiss-btn" title="${T.dismiss}" aria-label="${T.dismiss}" onclick="event.stopPropagation(); document.dispatchEvent(new CustomEvent('ns-dismiss', {detail: '${safeId}'}))">close</button><div class="swipe-delete">${T.dismiss}</div><div class="thumb-wrapper"><img data-src="${imgUrl}" decoding="async" class="dropdown-thumb ${isMusic ? 'music' : ''}" loading="lazy" onerror="if(this.dataset.fallback){this.src=this.dataset.fallback;this.removeAttribute('data-fallback')}else{this.style.display='none'}" data-fallback="${fallbackUrl}"><span class="material-icons" style="color:#555;font-size:24px;">${isMusic ? 'album' : 'movie'}</span></div><div class="dropdown-info">${badgeHtml}<div class="dropdown-title" title="${title}">${title}</div><div class="dropdown-subtitle" title="${sub}"><span class="sub-time">${timeAgo(item.DateCreated)} &bull;</span><span class="sub-text">${sub}</span></div></div></div>`);
        });

        if (activeFilter === 'All') {
            htmlParts.push(`<div class="footer-tools" data-action="clearall">${T.clearAll}</div>`);
        } else {
            const catLabel = T['filter' + activeFilter] || escapeHtml(activeFilter);
            htmlParts.push(`<div class="footer-tools" data-action="clearcat" data-category="${escapeHtml(activeFilter)}">${T.clearCat} ${catLabel}</div>`);
        }
        const finalHtml = htmlParts.join('');
        if (container.innerHTML !== finalHtml) {
            container.innerHTML = finalHtml;
            if (lazyImageObserver) lazyImageObserver.disconnect();
            lazyImageObserver = new IntersectionObserver((entries, o) => { entries.forEach(e => { if (e.isIntersecting) { const i = e.target; i.onload = () => i.classList.add('loaded'); i.src = i.dataset.src; o.unobserve(i); } }); });
            container.querySelectorAll('img[data-src]').forEach(i => lazyImageObserver.observe(i));
            initSwipeToDismiss(container);
        }

        // Wire footer "Clear" button via data attributes
        const footer = container.querySelector('.footer-tools');
        if (footer) {
            footer.onclick = () => {
                if (footer.dataset.action === 'clearall') {
                    document.dispatchEvent(new Event('ns-clearall'));
                } else if (footer.dataset.action === 'clearcat') {
                    document.dispatchEvent(new CustomEvent('ns-clearcat', { detail: footer.dataset.category }));
                }
            };
        }
    };

    let _swipeHandlers = null;
    const initSwipeToDismiss = (container) => {
        // Remove previous listeners to prevent accumulation
        if (_swipeHandlers) {
            container.removeEventListener('touchstart', _swipeHandlers.start);
            container.removeEventListener('touchmove', _swipeHandlers.move);
            container.removeEventListener('touchend', _swipeHandlers.end);
        }
        let startX = 0, startY = 0, currentX = 0, swiping = null;
        const threshold = 70;

        const onStart = (e) => {
            const item = e.target.closest('.dropdown-item');
            if (!item || e.target.closest('.dismiss-btn')) return;
            startX = e.touches[0].clientX;
            startY = e.touches[0].clientY;
            currentX = startX;
            swiping = item;
            swiping.style.transition = 'none';
        };
        const onMove = (e) => {
            if (!swiping) return;
            currentX = e.touches[0].clientX;
            const dy = e.touches[0].clientY - startY;
            const dx = currentX - startX;
            if (Math.abs(dx) > Math.abs(dy)) {
                e.preventDefault();
                if (dx < 0) {
                    const clampedDx = Math.max(dx, -120);
                    swiping.style.transform = `translateX(${clampedDx}px)`;
                    swiping.classList.toggle('swiping', Math.abs(dx) > 30);
                }
            }
        };
        const onEnd = () => {
            if (!swiping) return;
            const dx = currentX - startX;
            swiping.style.transition = 'transform 0.2s ease';
            if (dx < -threshold) {
                const itemId = swiping.dataset.itemId;
                swiping.style.transform = 'translateX(-100%)';
                swiping.style.opacity = '0';
                setTimeout(() => {
                    document.dispatchEvent(new CustomEvent('ns-dismiss', { detail: itemId }));
                }, 200);
            } else {
                swiping.style.transform = '';
                swiping.classList.remove('swiping');
            }
            swiping = null;
        };

        container.addEventListener('touchstart', onStart, { passive: true });
        container.addEventListener('touchmove', onMove, { passive: false });
        container.addEventListener('touchend', onEnd, { passive: true });
        _swipeHandlers = { start: onStart, move: onMove, end: onEnd };
    };


    const closeDropdown = () => {
        if (lazyImageObserver) { lazyImageObserver.disconnect(); }
        const drop = document.getElementById('notification-dropdown');
        const back = document.getElementById('notify-backdrop');
        if (drop) drop.style.display = 'none';
        if (back) back.style.display = 'none';
        recalculateNewStatus(); // Clear badges for next open
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
                document.addEventListener('ns-clearcat', (e) => { clearCategoryNotifications(e.detail); });
                document.addEventListener('ns-refresh', () => { triggerHardRefresh(); });
                document.addEventListener('ns-navigate', (e) => {
                    const id = e.detail;
                    closeDropdown();
                    const sid = window.ApiClient && window.ApiClient.serverId ? window.ApiClient.serverId() : '';
                    const dest = '#!/details?id=' + id + (sid ? '&serverId=' + sid : '');
                    // Use Jellyfin's internal router so theme music and page lifecycle trigger correctly
                    if (window.Emby && window.Emby.Page && window.Emby.Page.show) {
                        window.Emby.Page.show(dest);
                    } else {
                        window.location.hash = dest;
                    }
                });
                document.addEventListener('ns-dismiss', async (e) => {
                    const itemId = e.detail;
                    // Optimistic UI: animate out immediately
                    const el = document.querySelector(`.dropdown-item[data-item-id="${CSS.escape(itemId)}"]`);
                    if (el) el.classList.add('dismissing');

                    const success = await dismissOnServer(itemId);
                    if (success) {
                        // Remove from local data (by Id OR by SeriesId for group dismiss)
                        currentData = currentData.filter(i => !(i.Id === itemId || i.SeriesId === itemId));
                        localStorage.setItem(nsKey('data'), JSON.stringify(currentData)); localStorage.setItem(nsKey('data-ts'), Date.now().toString());
                        localStorage.removeItem(nsKey('etag')); // Force fresh fetch next time
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
            lastFetchTime = 0; // Bypass throttle on explicit user click
            fetchData().then(() => {
                updateList(drop);
                
                // Mark as read on server but keep visual badges in the dropdown
                const unreadIds = [];
                currentData.forEach(i => {
                    if (!i.IsRead) {
                        i.IsRead = true;
                        unreadIds.push(i.Id);
                    }
                });

                if (unreadIds.length > 0) {
                    // Hide bell counter only — list badges (NEW/UPD) stay visible
                    const badge = document.querySelector('.ns-badge');
                    if (badge) badge.classList.remove('visible');
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
        bellBtn.setAttribute('aria-label', T === strings.fr ? 'Notifications' : 'Notifications');
        bellBtn.innerHTML = '<span class="material-icons notifications"></span>';
        bellBtn.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toggleDropdown(); };
        header.prepend(bellBtn);
        loadFromCache();

        // Initial fetch attempt
        fetchData();
    };

    // Swap Jellyfin's default folder icon next to NotifySync in the admin sidebar for a bell.
    // Modern Jellyfin (10.11+) uses Material-UI (React) which renders icons as inline <svg><path>
    // rather than <span class="material-icons">. We rewrite the path data, which is the cleanest
    // way to replace the glyph in place (no extra DOM nodes, no duplicate icons, re-applied
    // automatically by the MutationObserver if React re-renders).
    const NS_NOTIFICATIONS_PATH = 'M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z';
    const installAdminSidebarIcon = () => {
        const links = document.querySelectorAll('a[href*="configurationpage"]');
        for (let i = 0; i < links.length; i++) {
            const link = links[i];
            const href = (link.getAttribute('href') || '').toLowerCase();
            if (href.indexOf('notifysync') < 0) continue;
            // Idempotent check — re-runs cheaply on every MutationObserver tick, only
            // touches the DOM when the path actually differs.
            const path = link.querySelector('svg path');
            if (path && path.getAttribute('d') !== NS_NOTIFICATIONS_PATH) {
                path.setAttribute('d', NS_NOTIFICATIONS_PATH);
                continue;
            }
            // Fallback for older themes that still render <span class="material-icons">…</span>
            const iconSpan = link.querySelector('.material-icons');
            if (iconSpan && iconSpan.textContent.trim() !== 'notifications') {
                iconSpan.textContent = 'notifications';
            }
        }
    };

    const monitorBellDisappearance = () => {
        const obs = new MutationObserver(() => { if (!document.getElementById('netflix-bell')) { obs.disconnect(); startMainObserver(); } });
        obs.observe(document.body, { childList: true, subtree: true });
    };

    let _installDebounce = null;
    const startMainObserver = () => {
        observerInstance = new MutationObserver(() => {
            if (_installDebounce) clearTimeout(_installDebounce);
            _installDebounce = setTimeout(() => {
                installBell();
                installAdminSidebarIcon();
            }, 200);
        });
        observerInstance.observe(document.body, { childList: true, subtree: true });
        installBell();
        installAdminSidebarIcon();
    };

    // --- NEW: WebSockets Real-Time Sync ---
    let wsDebounceTimeout = null;
    let wsFollowUp1 = null;
    const onWebSocketMessage = (e, msg) => {
        if (!msg || !msg.MessageType) return;

        // Listen to relevant real-time server events
        if (msg.MessageType === "LibraryChanged" || msg.MessageType === "UserDataChanged") {
            // Reset all timers on each event
            if (wsDebounceTimeout) clearTimeout(wsDebounceTimeout);
            if (wsFollowUp1) clearTimeout(wsFollowUp1);

            wsDebounceTimeout = setTimeout(() => {
                retryDelay = 2000;
                fetchData();
                // Jellyfin propagates Played to episodes one-by-one (~15s for a season).
                // Single follow-up at 8s catches stragglers without spamming the server
                // (the next viewshow/visibilitychange will catch any final propagation).
                wsFollowUp1 = setTimeout(() => fetchData(), 8000);
            }, 2000);
        }
    };

    const setupEvents = () => {
        if (window.Events && window.ApiClient) {
            window.Events.on(window.ApiClient, "websocketmessage", onWebSocketMessage);

            // Detect Jellyfin user language preference
            detectJellyfinLang();

            // Re-fetch data instantly when user logs in or reconnects
            window.Events.on(window.ApiClient, "authenticated", () => {
                retryDelay = 1000;
                detectJellyfinLang();
                fetchData();
            });

        } else {
            setTimeout(setupEvents, 2000);
        }
    };

    document.addEventListener('viewshow', () => {
        retryDelay = 1000;
        detectJellyfinLang();
        fetchData();
    });

    // Background polling every 5 min to catch metadata updates (images, etc.)
    setInterval(() => { if (!document.hidden && !isFetching) fetchData(); }, 300000);

    // Handle SPA navigation visibility changes
    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) {
            retryDelay = 1000;
            detectJellyfinLang();
            fetchData();
        }
    });

    setupEvents();
    startMainObserver();
})();