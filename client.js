/* NOTIFYSYNC V5.7.3.0 */
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
        fr: { header: "Quoi de neuf ?", empty: "Vous êtes à jour !", clearAll: "Vider la liste", clearCat: "Vider", dismiss: "Retirer", badgeNew: "NOUVEAU", badgeUpgrade: "MAJ", eps: "épisodes", eps1: "épisode", epPrefix: "Ép.", tracks: "pistes", tracks1: "piste", filterAll: "Tout", filterMovie: "Films", filterSeries: "Séries", filterMusic: "Musique", filterFav: "Favoris", kindQuality: "Qualité", kindCodec: "Codec", kindAudio: "Audio", kindAll: "Tout", season: "Saison", secToday: "Aujourd'hui", secWeek: "Cette semaine", secOlder: "Plus ancien" },
        en: { header: "What's New?", empty: "You're all caught up!", clearAll: "Clear list", clearCat: "Clear", dismiss: "Dismiss", badgeNew: "NEW", badgeUpgrade: "UPD", eps: "episodes", eps1: "episode", epPrefix: "Ep.", tracks: "tracks", tracks1: "track", filterAll: "All", filterMovie: "Movies", filterSeries: "Series", filterMusic: "Music", filterFav: "Favorites", kindQuality: "Quality", kindCodec: "Codec", kindAudio: "Audio", kindAll: "All", season: "Season", secToday: "Today", secWeek: "This week", secOlder: "Earlier" }
    };
    let T = strings[userLang.startsWith('fr') ? 'fr' : 'en'];

    // Locale-aware date/time formatters, rebuilt when the Jellyfin language changes.
    // Intl picks the right convention per locale (FR → "14:30" 24h, EN → "2:30 PM").
    const buildFormatters = (lang) => ({
        time: new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit' }),
        day: new Intl.DateTimeFormat(lang, { weekday: 'short' }),
        date: new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short' }),
        dateYear: new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short', year: 'numeric' })
    });
    let fmt = buildFormatters(userLang);

    const detectJellyfinLang = () => {
        try {
            const userId = getUserId();
            if (!userId) return;
            const jfLang = localStorage.getItem(userId + '-language') || userLang;
            const key = jfLang.startsWith('fr') ? 'fr' : 'en';
            if (T !== strings[key]) {
                T = strings[key];
                userLang = jfLang;
                fmt = buildFormatters(userLang);
                updateBadge();
                const drop = document.getElementById('notification-dropdown');
                if (drop && drop.style.display === 'flex') updateList(drop);
            }
        } catch (e) { /* silently use browser fallback */ }
    };

    // Absolute timestamp that complements the recency sections instead of repeating
    // them: clock time for today (the section already says "Today"), weekday for
    // this week, date for older. Boundaries mirror sectionLabel exactly.
    const timeAgo = (date) => {
        const d = new Date(date);
        if (isNaN(d.getTime())) return '';
        const now = new Date();
        const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
        const t = d.getTime();
        if (t >= midnight) return fmt.time.format(d);
        if (t >= midnight - 6 * 86400000) return fmt.day.format(d);
        return (d.getFullYear() === now.getFullYear() ? fmt.date : fmt.dateYear).format(d);
    };

    // Recency bucket for the dropdown's section separators (Netflix-style).
    // Items are already sorted newest-first, so sections render in order.
    // Boundaries are CALENDAR-based, not a rolling window: "Today" means since
    // local midnight (an item from yesterday evening reads as "This week", not
    // "Today", even if it's less than 24h old).
    const sectionLabel = (date) => {
        const t = new Date(date).getTime();
        const now = new Date();
        const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
        if (t >= midnight) return T.secToday;
        if (t >= midnight - 6 * 86400000) return T.secWeek; // within the last 7 calendar days
        return T.secOlder;
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
            @keyframes bellPulse { 0%, 100% { transform: scale(1); } 15% { transform: scale(1.3) rotate(-10deg); } 30% { transform: scale(1.3) rotate(10deg); } 45% { transform: scale(1.2) rotate(-5deg); } 60% { transform: scale(1.1); } }
            .ns-pulse { animation: bellPulse 0.8s ease-in-out; }
            @keyframes badgeBounce { 0% { transform: scale(1.5); } 100% { transform: scale(1); } }
            .ns-pulse .ns-badge { animation: badgeBounce 0.5s ease-out; }
            .dismiss-btn { position:absolute; top:6px; right:6px; background:rgba(255,255,255,0.1); border:none; color:#888; cursor:pointer; width:24px; height:24px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:16px; line-height:1; opacity:0; transition:opacity 0.2s, background 0.2s; z-index:2; padding:0; font-family:'Material Icons'; }
            .dropdown-item:hover .dismiss-btn { opacity:1; }
            .dismiss-btn:hover { background:rgba(255,255,255,0.2); color:#fff; }
            @media (pointer: coarse) { .dismiss-btn { opacity:0.6; } .dismiss-btn::after { content:''; position:absolute; inset:-10px; border-radius:50%; } }
            .dismiss-btn:focus-visible { opacity:1; outline:2px solid #fff; outline-offset:2px; }
            @keyframes dismissSlide { to { opacity:0; transform:translateX(50px); height:0; padding:0; margin:0; overflow:hidden; } }
            .dismissing { animation: dismissSlide 0.3s ease-out forwards; }
            .dropdown-item { transition: transform 0.2s ease, background 0.2s; }
            .dropdown-item .swipe-delete { position:absolute; right:0; top:0; bottom:0; width:80px; background:var(--ns-red); color:#fff; display:flex; align-items:center; justify-content:center; font-size:12px; font-weight:700; opacity:0; pointer-events:none; border-radius:0 0 0 0; }
            .dropdown-item.swiping .swipe-delete { opacity:1; pointer-events:auto; }
            .dropdown-header { display:flex; justify-content:space-between; padding:16px 20px; border-bottom: 1px solid var(--ns-border); background: rgba(0,0,0,0.3); align-items:center; flex-shrink: 0; }
            .header-title { font-weight: 700; font-size: 15px; letter-spacing: 0.5px; }
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
            .ns-section { font-size: 11px; color: #999; letter-spacing: 1.1px; text-transform: uppercase; padding: 12px 20px 4px; display: flex; align-items: center; gap: 8px; user-select: none; }
            .ns-section::after { content: ''; flex: 1; height: 1px; background: var(--ns-border); }
            .ns-fav { color: #f0a92d; font-size: 11px; vertical-align: 1px; }
            @media (prefers-reduced-motion: reduce) {
                #notification-dropdown, .ns-pulse, .ns-pulse .ns-badge { animation: none; }
                .dismissing { animation-duration: 0.01ms; }
                .dropdown-item { transition: none; }
            }
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
    };

    const getUserId = () => {
        if (!window.ApiClient) return null;
        const userId = window.ApiClient.getCurrentUserId();
        return (userId && userId !== 'null' && userId !== 'undefined') ? userId : null;
    };

    const nsKey = (key) => { const uid = getUserId(); return uid ? `ns-${uid}-${key}` : `ns-${key}`; };

    // Formats a sorted set of season numbers into a compact label.
    //   [4]             → "Saison 4"    (single season: full word, locale-aware)
    //   [1,2,3,4,5]     → "S1-S5"       (consecutive range, no comma)
    //   [1,2,4]         → "S1-S2, S4"   (mixed: range + singleton, comma-separated)
    //   [1,3,5,6,7]     → "S1, S3, S5-S7"
    // Specials (season 0) and non-integers are filtered out — they shouldn't
    // appear in a season summary label.
    const formatSeasons = (seasons) => {
        if (!seasons || seasons.length === 0) return null;
        const sorted = [...new Set(seasons.filter(s => Number.isInteger(s) && s > 0))].sort((a, b) => a - b);
        if (sorted.length === 0) return null;
        if (sorted.length === 1) return `${T.season} ${sorted[0]}`;
        const runs = [[sorted[0]]];
        for (let i = 1; i < sorted.length; i++) {
            const last = runs[runs.length - 1];
            if (sorted[i] === last[last.length - 1] + 1) { last.push(sorted[i]); }
            else { runs.push([sorted[i]]); }
        }
        return runs.map(r => r.length === 1 ? `S${r[0]}` : `S${r[0]}-S${r[r.length - 1]}`).join(', ');
    };

    // Same run-length encoding as formatSeasons, but for episode numbers within
    // a single season, with a localized prefix once at the front:
    //   [1..10] → "Ép. 1-10", [1,8] → "Ép. 1, 8", [1,2,4] → "Ép. 1-2, 4".
    // Only meaningful when a group covers ONE season — across seasons an episode
    // number alone is ambiguous, so the caller falls back to a plain count.
    const formatEpisodes = (episodes) => {
        if (!episodes || episodes.length === 0) return null;
        const sorted = [...new Set(episodes.filter(e => Number.isInteger(e) && e > 0))].sort((a, b) => a - b);
        if (sorted.length === 0) return null;
        const runs = [[sorted[0]]];
        for (let i = 1; i < sorted.length; i++) {
            const last = runs[runs.length - 1];
            if (sorted[i] === last[last.length - 1] + 1) { last.push(sorted[i]); }
            else { runs.push([sorted[i]]); }
        }
        const body = runs.map(r => r.length === 1 ? `${r[0]}` : `${r[0]}-${r[r.length - 1]}`).join(', ');
        return `${T.epPrefix} ${body}`;
    };

    // Builds the subtitle for a grouped card. Single-season groups show the exact
    // episode range ("Saison 4 • E1-E10"); multi-season groups keep the season
    // span + count ("S1-S5 • 120 épisodes"); music keeps a plain track count.
    const groupSubtitle = (g) => {
        const isMusic = g.Type === 'Audio';
        const count = g.ShowBadge ? (g.NewCount || g.GroupCount) : g.GroupCount;
        const lbl = isMusic ? (count > 1 ? T.tracks : T.tracks1) : (count > 1 ? T.eps : T.eps1);
        if (isMusic) return `${count} ${lbl}`;
        const seasonsLabel = formatSeasons(g.Seasons);
        const uniqSeasons = g.Seasons ? [...new Set(g.Seasons.filter(s => Number.isInteger(s) && s > 0))] : [];
        let detail = `${count} ${lbl}`;
        if (uniqSeasons.length === 1) {
            const epRange = formatEpisodes(g.Episodes);
            if (epRange) detail = epRange;
        }
        return seasonsLabel ? `${seasonsLabel} • ${detail}` : detail;
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
            // Separate new episodes from upgrades, and split upgrades by UpgradeKind
            // so a series with mixed UPD reasons (e.g. one Codec + one Audio) renders
            // as two distinct group cards instead of merging into one ambiguous group.
            const newEps = eps.filter(e => !e.IsUpgrade);
            const upgradesByKind = new Map();
            eps.filter(e => e.IsUpgrade).forEach(e => {
                const kind = e.UpgradeKind || '_nokind';
                if (!upgradesByKind.has(kind)) upgradesByKind.set(kind, []);
                upgradesByKind.get(kind).push(e);
            });
            const subsets = [newEps, ...upgradesByKind.values()];
            subsets.forEach(subset => {
                if (subset.length === 0) return;
                const latest = subset[0];
                const hasNew = subset.some(e => e.IsNew);
                const hasBadge = subset.some(e => e.ShowBadge);
                const newCount = subset.filter(e => e.ShowBadge).length;
                if (subset.length > 1) {
                    // Carry the seasons covered by this group so the renderer can label
                    // it as "Saison N — X épisodes" or "S1-S2 — X épisodes".
                    const seasons = subset.map(e => e.ParentIndexNumber);
                    const episodes = subset.map(e => e.IndexNumber);
                    result.push({ ...latest, IsGroup: true, GroupCount: subset.length, NewCount: newCount, Name: latest.SeriesName || latest.Name, Id: latest.SeriesId || latest.Id, IsNew: hasNew, ShowBadge: hasBadge, Seasons: seasons, Episodes: episodes, IsFavorite: subset.some(e => e.IsFavorite) });
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

        // '__fav' is the virtual favorites filter, not a real category.
        const matches = category === '__fav' ? (i) => i.IsFavorite : (i) => i.Category === category;
        const idsToDismiss = currentData.filter(matches).map(i => i.Id);
        if (idsToDismiss.length === 0) return;

        // Dismiss all items in a single bulk request
        const success = await bulkDismissOnServer(idsToDismiss);
        if (!success) return;

        // Remove from local data
        currentData = currentData.filter(i => !matches(i));
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

    const updateBadge = () => {
        const bell = document.getElementById('netflix-bell');
        if (!bell) return;
        let badge = bell.querySelector('.ns-badge');
        if (!badge) { badge = document.createElement('div'); badge.className = 'ns-badge'; bell.appendChild(badge); }
        const count = groupedData.filter(i => i.IsNew).length;
        if (count > 0) { badge.innerText = count > 9 ? '9+' : count; badge.classList.add('visible'); }
        else { badge.classList.remove('visible'); }
    };

    // Returns a localized label for the upgrade kind(s) — hybrid format:
    //   1 kind  → full word         "Quality" / "Codec" / "Audio"
    //   2 kinds → full words + "&"  "Codec & Audio" / "Quality & Audio"
    //   3 kinds → single keyword    "Tout" / "All"
    // The full-words form for 2 kinds reads naturally and still fits in the badge
    // width (longest case "Quality & Codec" = 15 chars). The all-three case is rare
    // enough that a single keyword is the cleanest summary.
    const upgradeKindLabel = (item) => {
        if (!item.IsUpgrade || !item.UpgradeKind) return '';
        const fullMap = { quality: T.kindQuality, codec: T.kindCodec, audio: T.kindAudio };
        const kinds = String(item.UpgradeKind).split(',').map(k => k.trim()).filter(k => fullMap[k]);
        if (kinds.length === 0) return '';
        if (kinds.length === 1) return fullMap[kinds[0]];
        if (kinds.length === 2) return `${fullMap[kinds[0]]} & ${fullMap[kinds[1]]}`;
        return T.kindAll;
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
        if (activeFilter === '__fav') filtered = filtered.filter(i => i.IsFavorite);
        else if (activeFilter !== 'All') filtered = filtered.filter(i => i.Category === activeFilter);
        const cats = new Set(['All']); groupedData.forEach(i => cats.add(i.Category));
        const filterBar = drop.querySelector('.filter-bar');
        let pillsHtml = Array.from(cats).map(c => `<div class="filter-pill ${activeFilter === c ? 'active' : ''}" data-category="${escapeHtml(c)}" tabindex="0" role="button" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();this.click()}">${T['filter' + c] || escapeHtml(c)}</div>`).join('');
        // Favorites pill — only rendered when the list actually contains favorites,
        // so users who never favorite anything don't get a dead filter.
        if (groupedData.some(i => i.IsFavorite)) {
            pillsHtml += `<div class="filter-pill ${activeFilter === '__fav' ? 'active' : ''}" data-category="__fav" tabindex="0" role="button" onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();this.click()}"><span class="ns-fav">★</span> ${T.filterFav}</div>`;
        }
        filterBar.innerHTML = pillsHtml;

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
                heroSub = groupSubtitle(hero);
            }

            const safeHeroId = escapeHtml(hero.Id);
            const heroNavId = escapeHtml(hero.RealItemId || hero.Id);
            const heroFallbackImg = client.getUrl(`Items/${encodeURIComponent(hero.SeriesId || hero.Id)}/Images/Primary?quality=70&fillWidth=380&fillHeight=160&format=webp`);
            htmlParts.push(`<div class="hero-section" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${heroNavId}'}))"><div class="hero-bg"><img src="${escapeHtml(heroImg)}" alt="" style="width:100%;height:100%;object-fit:cover;" onerror="if(this.dataset.fb){this.src=this.dataset.fb;this.removeAttribute('data-fb')}else{this.style.display='none'}" data-fb="${escapeHtml(heroFallbackImg)}"></div><div class="hero-overlay"></div><button class="dismiss-btn" title="${T.dismiss}" aria-label="${T.dismiss}" onclick="event.stopPropagation(); document.dispatchEvent(new CustomEvent('ns-dismiss', {detail: '${safeHeroId}'}))">close</button><div class="hero-content">${hero.IsUpgrade && hero.ShowBadge ? `<span class="hero-badge-upgrade">${escapeHtml(upgradeBadgeText(hero))}</span>` : hero.ShowBadge ? `<span class="hero-badge">${T.badgeNew}</span>` : ''}<div style="font-size:18px;font-weight:700;text-shadow:0 2px 4px #000;line-height:1.2;">${heroTitle}${hero.IsFavorite ? ' <span class="ns-fav">★</span>' : ''}</div><div style="font-size:12px;opacity:0.8;margin-top:4px">${timeAgo(hero.DateCreated)} &bull; ${heroSub}</div></div></div>`);
        }

        let curSection = null;
        filtered.filter(x => x !== hero).forEach(item => {
            const sec = sectionLabel(item.DateCreated);
            if (sec !== curSection) { curSection = sec; htmlParts.push(`<div class="ns-section">${sec}</div>`); }
            const isMusic = item.Type === 'Audio';
            const isGroup = !!item.IsGroup;
            const imgUrl = client.getUrl(`Items/${encodeURIComponent(item.Id)}/Images/Primary?tag=${encodeURIComponent(item.PrimaryImageTag || '')}&${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`);
            const fallbackUrl = item.SeriesId ? client.getUrl(`Items/${encodeURIComponent(item.SeriesId)}/Images/Primary?${isMusic ? 'fillHeight=100&fillWidth=100' : 'fillHeight=112&fillWidth=200'}&quality=80&format=webp`) : '';

            let title = escapeHtml(item.Name), sub = escapeHtml(String(item.ProductionYear ?? ''));
            if (!isGroup && item.Type === 'Episode') { title = escapeHtml(formatEpisodeTitle(item)); sub = escapeHtml(item.SeriesName); }
            if (isGroup) { sub = groupSubtitle(item); }

            const safeId = escapeHtml(item.Id);
            const navId = escapeHtml(item.RealItemId || item.Id);
            const badgeHtml = item.ShowBadge ? `<span class="item-badge">${escapeHtml(upgradeBadgeText(item))}</span>` : '';
            htmlParts.push(`<div class="dropdown-item ${item.IsUpgrade && item.ShowBadge ? 'style-upgrade' : item.ShowBadge ? 'style-new' : 'style-seen'}" data-item-id="${safeId}" onclick="document.dispatchEvent(new CustomEvent('ns-navigate', {detail: '${navId}'}))"><button class="dismiss-btn" title="${T.dismiss}" aria-label="${T.dismiss}" onclick="event.stopPropagation(); document.dispatchEvent(new CustomEvent('ns-dismiss', {detail: '${safeId}'}))">close</button><div class="swipe-delete">${T.dismiss}</div><div class="thumb-wrapper"><img data-src="${imgUrl}" decoding="async" class="dropdown-thumb ${isMusic ? 'music' : ''}" loading="lazy" onerror="if(this.dataset.fallback){this.src=this.dataset.fallback;this.removeAttribute('data-fallback')}else{this.style.display='none'}" data-fallback="${fallbackUrl}"><span class="material-icons" style="color:#555;font-size:24px;">${isMusic ? 'album' : 'movie'}</span></div><div class="dropdown-info">${badgeHtml}<div class="dropdown-title" title="${title}">${title}${item.IsFavorite ? ' <span class="ns-fav">★</span>' : ''}</div><div class="dropdown-subtitle" title="${sub}"><span class="sub-time">${timeAgo(item.DateCreated)} &bull;</span><span class="sub-text">${sub}</span></div></div></div>`);
        });

        if (activeFilter === 'All') {
            htmlParts.push(`<div class="footer-tools" data-action="clearall">${T.clearAll}</div>`);
        } else {
            const catLabel = activeFilter === '__fav' ? `★ ${T.filterFav}` : (T['filter' + activeFilter] || escapeHtml(activeFilter));
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


    // Anchor the dropdown directly below the bell so it doesn't overlap or
    // float above the Jellyfin header — header heights vary by version, theme,
    // zoom level, and DPI, so hard-coded `top` values in the CSS would always
    // be wrong for some setup. Called at open time AND on resize/rotation:
    // the position is viewport-dependent, so an open dropdown must follow
    // when the phone rotates or the window is resized.
    const positionDropdown = (drop) => {
        const bell = document.getElementById('netflix-bell');
        if (!bell || !drop) return;
        const rect = bell.getBoundingClientRect();
        drop.style.top = (rect.bottom + 8) + 'px';
        if (window.innerWidth > 600) {
            // Desktop: pin the right edge under the bell's right edge.
            drop.style.right = Math.max(0, window.innerWidth - rect.right) + 'px';
            drop.style.left = 'auto';
        } else {
            // Mobile: clear inline horizontal pins so the @media full-width
            // rule (left:10px; right:10px) takes over.
            drop.style.right = '';
            drop.style.left = '';
        }
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

            drop.innerHTML = `<div class="dropdown-header"><span class="header-title">${T.header}</span></div><div class="filter-bar"></div><div class="list-container"></div>`;
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
            positionDropdown(drop);
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
        // childList: catches node add/remove (SPA navigation, lazy renders).
        // attributes + attributeFilter[d]: catches in-place SVG path updates when
        // React/MUI re-renders the sidebar icon on resize without replacing the node.
        // Without this, the folder glyph re-appears whenever React patches the existing
        // <svg><path> instead of remounting it.
        observerInstance.observe(document.body, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['d']
        });
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

    // Keep an open dropdown anchored to the bell when the phone rotates or the
    // window is resized (its position is computed from the bell's bounding rect,
    // which moves with the layout). 'resize' also fires on orientation change.
    window.addEventListener('resize', () => {
        const drop = document.getElementById('notification-dropdown');
        if (drop && drop.style.display === 'flex') positionDropdown(drop);
    });

    setupEvents();
    startMainObserver();
})();