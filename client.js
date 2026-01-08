/* NOTIFYSYNC V4.0.0 - PERFORMANCE EDITION (JSON Backend, Skeleton, Lazy Load) */
(function () {
    let currentData = null;
    let groupedData = null;
    let observer = null;
    let lastCount = 0;
    let isFetching = false;
    let activeFilter = 'All';
    let pollInterval = 60000; // Smart polling base

    const PLUGIN_ID = "95655672-2342-4321-8291-321312312312";
    const SOUND_KEY = 'notifysync_muted';
    const SNAPSHOT_KEY = 'notifysync_snapshot'; // For checking changes

    const STRINGS = {
        fr: { header: "Notifications", empty: "Tout est calme...", markAll: "Tout vu", recent: "À l'instant", badgeNew: "NOUVEAU", newEps: "épisodes", badgeMovie: "FILM", badgeSeries: "SÉRIE", filterAll: "Tout", filterMovies: "Films", filterSeries: "Séries", filterAnime: "Animes", play: "Lecture", refresh: "Actualiser" },
        en: { header: "Notifications", empty: "All caught up!", markAll: "Mark all read", recent: "Just now", badgeNew: "NEW", newEps: "episodes", badgeMovie: "MOVIE", badgeSeries: "SERIES", filterAll: "All", filterMovies: "Movies", filterSeries: "Series", filterAnime: "Anime", play: "Play", refresh: "Refresh" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;
    const NOTIF_SOUND = new Audio("data:audio/mp3;base64,//uQRAAAAWMSLwUIYAAsYkXgoQwAEaYLWfkWgAI0wWs/ItAAAG1ineAAA0gAAAB5IneAAA0gAAABzLFwAHMwcAAedxYaMPFmPkQcnhsG8G2hxtxl0q5f77+6/v/7/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8//");

    let hoverAudio = null;
    let hoverTimeout = null;

    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;
        const css = `
            :root { --ns-red: #e50914; --ns-dark: #181818; }
            #bell-container{display:flex!important;align-items:center;justify-content:center;z-index:1000}
            #netflix-bell{display:flex!important;visibility:visible!important;opacity:1!important;position:relative;background:0 0;border:none;cursor:pointer;padding:10px;color:inherit}
            .notification-dot{position:absolute;top:4px;right:4px;min-width:15px;height:15px;padding:0 4px;background-color:var(--ns-red);border-radius:10px;opacity:0;pointer-events:none;transition:opacity .3s;display:flex!important;align-items:center;justify-content:center;color:#fff!important;font-size:9px!important;font-weight:700;box-shadow:0 0 5px rgba(0,0,0,.5);z-index:2}
            
            #notify-backdrop { position: fixed; inset: 0; z-index: 999998; display: none; cursor: default; }
            #notification-dropdown{position:fixed;top:70px;right:20px;width:380px;max-width:90vw;background:var(--ns-dark);color:#fff;border:1px solid rgba(255,255,255,.1);border-radius:8px;box-shadow:0 0 50px rgba(0,0,0,.8);z-index:999999;display:none;overflow:hidden; font-family: 'Netflix Sans', sans-serif; animation: dropIn 0.2s ease;}
            @keyframes dropIn { from { opacity: 0; transform: translateY(-10px); } to { opacity: 1; transform: translateY(0); } }
            
            .dropdown-header { display:flex;justify-content:space-between;padding:15px;background:#000; border-bottom: 1px solid rgba(255,255,255,0.1); }
            .header-tools { display:flex; gap:15px; }
            .tool-icon { cursor:pointer; opacity:0.7; transition:opacity 0.2s; }
            .tool-icon:hover { opacity:1; }

            .filter-bar { padding: 12px 15px; display: flex; gap: 10px; border-bottom: 1px solid rgba(255,255,255,0.1); background: rgba(30,30,30,0.95); backdrop-filter: blur(10px); position: sticky; top: 0; z-index: 10; overflow-x: auto; }
            .filter-pill { font-size: 11px; padding: 5px 12px; border-radius: 20px; border: 1px solid rgba(255,255,255,0.3); cursor: pointer; transition: all 0.2s; opacity: 0.7; white-space: nowrap; }
            .filter-pill:hover { border-color: #fff; opacity: 1; }
            .filter-pill.active { background: #fff; color: #000; border-color: #fff; opacity: 1; font-weight: bold; }

            .list-container { max-height: 400px; overflow-y: auto; overflow-x: hidden; }
            .list-container::-webkit-scrollbar { width: 6px; }
            .list-container::-webkit-scrollbar-thumb { background: #444; border-radius: 3px; }

            /* SKELETON LOADING */
            .skeleton-box { background: #333; position: relative; overflow: hidden; border-radius: 4px; }
            .skeleton-box::after { position: absolute; top: 0; right: 0; bottom: 0; left: 0; transform: translateX(-100%); background-image: linear-gradient(90deg, rgba(255, 255, 255, 0) 0, rgba(255, 255, 255, 0.05) 20%, rgba(255, 255, 255, 0.1) 60%, rgba(255, 255, 255, 0) 100%); animation: shimmer 2s infinite; content: ''; }
            @keyframes shimmer { 100% { transform: translateX(100%); } }
            .sk-item { padding: 12px; display: flex; align-items: flex-start; gap: 12px; border-bottom: 1px solid rgba(255,255,255,.05); }
            .sk-thumb { width: 90px; height: 56px; }
            .sk-lines { flex: 1; display: flex; flex-direction: column; gap: 6px; }
            .sk-line { height: 10px; width: 60%; }
            .sk-line.short { width: 40%; }

            /* ITEMS */
            .dropdown-item{display:flex;align-items:flex-start;padding:12px;border-bottom:1px solid rgba(255,255,255,.05);cursor:pointer;position:relative;transition: background .2s;}
            .dropdown-item:hover{background:#252525;}
            .thumb-wrapper{position:relative;width:90px;height:56px;margin-right:12px;flex-shrink:0;overflow:hidden;border-radius:4px;background: #222; }
            .dropdown-thumb{width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity 0.3s;}
            .dropdown-thumb.loaded{opacity:1;}
            
            .dropdown-info{flex:1;min-width:0;display:flex;flex-direction:column;}
            .dropdown-title{font-weight:700;font-size:13px;margin-bottom:2px; line-height:1.2;}
            .dropdown-subtitle{font-size:11px;color:#bbb;margin-bottom:4px;}
            .metadata-line { display: flex; gap: 8px; font-size: 10px; color: #888; align-items: center; }
            
            .hero-section { position: relative; height: 200px; width: 100%; overflow: hidden; display: flex; flex-direction: column; justify-content: flex-end; cursor: pointer; }
            .hero-bg { position: absolute; inset: 0; background-size: cover; background-position: center; transition: transform 6s ease; }
            .hero-section:hover .hero-bg { transform: scale(1.05); }
            .hero-overlay { position: absolute; inset: 0; background: linear-gradient(to top, #181818 0%, rgba(20,20,20,0.6) 50%, rgba(0,0,0,0.3) 100%); }
            .hero-content { position: relative; z-index: 2; padding: 15px; width: 100%; box-sizing: border-box; }
            .hero-title { font-size: 18px; font-weight: 800; text-shadow: 0 1px 2px rgba(0,0,0,0.8); margin-bottom: 5px; }

            #notifysync-toast{position:fixed;bottom:30px;left:50%;transform:translateX(-50%);background:#fff;color:#000;padding:10px 24px;border-radius:4px;font-weight:bold;z-index:10000;opacity:0;transition:opacity .3s,transform .3s;pointer-events:none;}#notifysync-toast.visible{opacity:1;transform:translateX(-50%) translateY(-10px)}
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
        if (!document.getElementById('notifysync-toast')) { const t = document.createElement('div'); t.id = 'notifysync-toast'; document.body.appendChild(t); }
    };

    const getRemoteLastSeenDate = async (uid) => { try { const r = await fetch(`/NotifySync/LastSeen/${uid}?t=` + Date.now()); if (r.ok) return await r.text(); } catch (e) { } return "2000-01-01T00:00:00.000Z"; };
    const setRemoteLastSeenDate = async (uid, dateStr) => { try { await fetch(`/NotifySync/LastSeen/${uid}?date=${encodeURIComponent(dateStr)}`, { method: 'POST' }); } catch (e) { } };
    const showToast = (msg) => { const t = document.getElementById('notifysync-toast'); if (t) { t.innerHTML = msg; t.classList.add('visible'); setTimeout(() => t.classList.remove('visible'), 3000); } };

    // --- AUDIO LOGIC ---
    const playHoverSound = (itemId) => {
        if (localStorage.getItem(SOUND_KEY) === 'true') return;
        if (hoverTimeout) clearTimeout(hoverTimeout);
        if (hoverAudio) { hoverAudio.pause(); hoverAudio = null; }
        hoverTimeout = setTimeout(async () => {
            // Future: Use /NotifySync/Audio/{id} if fully implemented
            const client = window.ApiClient;
            try {
                const songs = await client.getThemeSongs(client.getCurrentUserId(), itemId);
                if (songs && songs.Items.length > 0) {
                    hoverAudio = new Audio(client.getUrl(`Audio/${songs.Items[0].Id}/stream`));
                    hoverAudio.volume = 0; hoverAudio.play();
                    let v = 0; const f = setInterval(() => { v += 0.05; if (v >= 0.3) { v = 0.3; clearInterval(f); } hoverAudio.volume = v; }, 200);
                }
            } catch (e) { }
        }, 800);
    };
    const stopHoverSound = () => {
        if (hoverTimeout) clearTimeout(hoverTimeout);
        if (hoverAudio) {
            let a = hoverAudio; hoverAudio = null;
            let v = a.volume; const f = setInterval(() => { v -= 0.05; if (v <= 0) { a.pause(); clearInterval(f); } else a.volume = v; }, 100);
        }
    };

    // --- LOGIC ---
    // --- LOGIC ---
    const getCategory = (item) => {
        return item.Category || 'Movie'; // Server now handles logic (Series/Movie/Anime/Custom)
    };

    const processGrouping = (items) => {
        const g = {}; const r = [];
        items.forEach(i => {
            if (i.Type === 'Episode' && i.SeriesName) {
                if (!g[i.SeriesName]) { g[i.SeriesName] = { ...i, IsGroup: true, GroupCount: 1, LatestId: i.Id, LatestDate: i.DateCreated }; r.push(g[i.SeriesName]); }
                else { g[i.SeriesName].GroupCount++; }
            } else { r.push(i); }
        });
        return r;
    };

    const toggleDropdown = (forceOpen = null) => {
        const drop = document.getElementById('notification-dropdown');
        const backdrop = document.getElementById('notify-backdrop');
        if (!drop || !backdrop) return;

        const isOpen = drop.style.display === 'block';
        const shouldOpen = forceOpen !== null ? forceOpen : !isOpen;

        if (shouldOpen) {
            drop.style.display = 'block';
            backdrop.style.display = 'block';

            // Re-init filters in case data changed categories
            renderFilters(drop);
            updateList(drop);
        } else {
            drop.style.display = 'none';
            backdrop.style.display = 'none';
            stopHoverSound();
        }
    };

    const renderFilters = (drop) => {
        const bar = drop.querySelector('.filter-bar');
        if (!bar) return;

        // Extract Categories
        const cats = new Set(['All']);
        if (groupedData) {
            groupedData.forEach(i => cats.add(getCategory(i)));
        }

        // Render
        let html = '';
        cats.forEach(c => {
            const label = T['filter' + c] || c; // Try translation, else use raw
            const active = activeFilter === c ? 'active' : '';
            html += `<div class="filter-pill ${active}" data-f="${c}">${label}</div>`;
        });
        bar.innerHTML = html;

        // Bind
        bar.querySelectorAll('.filter-pill').forEach(pill => {
            pill.onclick = (e) => {
                bar.querySelectorAll('.filter-pill').forEach(p => p.classList.remove('active'));
                e.target.classList.add('active');
                activeFilter = e.target.getAttribute('data-f');
                updateList(drop);
            };
        });
    };

    const updateList = (drop) => {
        const container = drop.querySelector('.list-container');
        if (!container) return;

        // SHOW SKELETON if no data or fetching
        if (isFetching && !groupedData) {
            container.innerHTML = `
                <div class="sk-item"><div class="skeleton-box sk-thumb"></div><div class="skeleton-box sk-lines"><div class="skeleton-box sk-line"></div><div class="skeleton-box sk-line short"></div></div></div>
                <div class="sk-item"><div class="skeleton-box sk-thumb"></div><div class="skeleton-box sk-lines"><div class="skeleton-box sk-line"></div><div class="skeleton-box sk-line short"></div></div></div>
                <div class="sk-item"><div class="skeleton-box sk-thumb"></div><div class="skeleton-box sk-lines"><div class="skeleton-box sk-line"></div><div class="skeleton-box sk-line short"></div></div></div>`;
            return;
        }

        container.innerHTML = '';
        if (!groupedData || groupedData.length === 0) { container.innerHTML = `<div style="padding:40px;text-align:center;color:#666;">${T.empty}</div>`; return; }

        let filtered = groupedData;
        if (activeFilter !== 'All') filtered = groupedData.filter(i => getCategory(i) === activeFilter);
        if (filtered.length === 0) { container.innerHTML = `<div style="padding:40px;text-align:center;color:#666;">${T.empty}</div>`; return; }

        const client = window.ApiClient;
        let html = '';

        // HERO
        const hero = filtered[0];
        if (hero) {
            let heroImg = hero.BackdropImageTags && hero.BackdropImageTags.length > 0 ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=60&maxWidth=600`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=60&maxWidth=400`);
            if (hero.IsGroup && hero.SeriesId) heroImg = client.getUrl(`Items/${hero.SeriesId}/Images/Backdrop/0?quality=60&maxWidth=600`);

            let heroTitle = hero.IsGroup ? hero.SeriesName : (hero.SeriesName || hero.Name);
            html += `
            <div class="hero-section" onclick="window.location.hash='#!/details?id=${hero.IsGroup ? hero.SeriesId : hero.Id}'">
                <div class="hero-bg" style="background-image:url('${heroImg}')"></div>
                <div class="hero-overlay"></div>
                <div class="hero-content">
                    <div class="hero-new-badge">${T.badgeNew}</div>
                    <div class="hero-title">${heroTitle}</div>
                </div>
            </div>`;
        }

        // LIST
        filtered.slice(1).forEach((item, index) => {
            let imgUrl = client.getUrl(`Items/${item.Id}/Images/Primary?fillHeight=112&fillWidth=180&quality=90`);
            if (item.IsGroup) imgUrl = client.getUrl(`Items/${item.SeriesId}/Images/Primary?fillHeight=112&fillWidth=180&quality=90`);

            let title = item.IsGroup ? item.SeriesName : (item.SeriesName ? item.SeriesName : item.Name);
            let subtitle = item.IsGroup ? `${item.GroupCount} ${T.newEps}` : (item.SeriesName ? item.Name : '');
            let year = item.ProductionYear || '';

            html += `
            <div class="dropdown-item" id="notif-item-${item.Id}">
                <div class="thumb-wrapper">
                    <img data-src="${imgUrl}" class="dropdown-thumb" loading="lazy">
                </div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    <div class="metadata-line"><span>${year}</span> &bull; <span>${getCategory(item)}</span></div>
                    ${subtitle ? `<div class="dropdown-subtitle">${subtitle}</div>` : ''}
                </div>
            </div>`;
        });

        container.innerHTML = html;

        // Lazy Load Observer
        const imgObserver = new IntersectionObserver((entries, obs) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    const img = entry.target;
                    img.src = img.getAttribute('data-src');
                    img.onload = () => img.classList.add('loaded');
                    obs.unobserve(img);
                }
            });
        });
        container.querySelectorAll('img[data-src]').forEach(img => imgObserver.observe(img));

        // Click Events
        container.querySelectorAll('.dropdown-item').forEach(el => {
            el.addEventListener('mouseenter', () => playHoverSound(el.id.replace('notif-item-', '')));
            el.addEventListener('mouseleave', () => stopHoverSound());
            el.onclick = (e) => {
                const id = el.id.replace('notif-item-', '');
                window.location.hash = `#!/details?id=${id}`;
                toggleDropdown(false);
            };
        });
    };

    const buildStructure = () => {
        if (document.getElementById('notification-dropdown')) return;

        const backdrop = document.createElement('div');
        backdrop.id = 'notify-backdrop';
        backdrop.onclick = () => toggleDropdown(false);
        document.body.appendChild(backdrop);

        const drop = document.createElement('div');
        drop.id = 'notification-dropdown';
        drop.onclick = (e) => e.stopPropagation();
        document.body.appendChild(drop);
    };

    const initDropdown = (drop) => {
        if (drop.querySelector('.dropdown-header')) return;
        const isMuted = localStorage.getItem(SOUND_KEY) === 'true';
        let h = `<div class="dropdown-header">
                    <span style="font-weight:bold;font-size:16px;">${T.header}</span>
                    <div class="header-tools">
                        <span class="material-icons tool-icon refresh-btn" title="${T.refresh}">refresh</span>
                        <span class="material-icons tool-icon mute-btn">${isMuted ? 'volume_off' : 'volume_up'}</span>
                        <span class="material-icons tool-icon mark-all-btn" title="${T.markAll}">done_all</span>
                    </div>
                 </div>
                 <div class="filter-bar">
                    <!-- Dynamic Filters -->
                 </div>
                 <div class="list-container"></div>`;
        drop.innerHTML = h;

        renderFilters(drop); // Initial render

        drop.querySelector('.refresh-btn').onclick = () => fetchData();
        drop.querySelector('.mute-btn').onclick = (e) => {
            const m = localStorage.getItem(SOUND_KEY) === 'true';
            localStorage.setItem(SOUND_KEY, !m);
            e.target.innerText = !m ? 'volume_off' : 'volume_up';
        };
        drop.querySelector('.mark-all-btn').onclick = async () => {
            if (groupedData && groupedData[0]) {
                await setRemoteLastSeenDate(window.ApiClient.getCurrentUserId(), groupedData[0].DateCreated);
                showToast(T.markAll);
                fetchData();
            }
        };
    };

    const installBell = () => {
        if (document.querySelector('#netflix-bell')) return;
        injectStyles();
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right');
        if (!header) return;

        const bell = document.createElement('button'); bell.id = 'netflix-bell'; bell.className = 'paper-icon-button-light headerButton';
        bell.innerHTML = `<span class="material-icons notifications"></span><div class="notification-dot"></div>`;

        bell.onclick = (e) => {
            e.preventDefault(); e.stopPropagation();
            buildStructure();
            const drop = document.querySelector('#notification-dropdown');
            initDropdown(drop);
            toggleDropdown();
        };

        header.prepend(Object.assign(document.createElement('div'), { id: 'bell-container' }).appendChild(bell).parentNode);

        buildStructure(); // Init early
        fetchData(); // Initial Fetch
        setInterval(fetchData, pollInterval); // Smart Polling (uses cheap JSON fetch now)
    };

    const checkUnread = async (items) => {
        const client = window.ApiClient; if (!client) return;
        const userId = client.getCurrentUserId();
        const dot = document.querySelector('.notification-dot'); if (!dot) return;
        const lastSeenDateStr = await getRemoteLastSeenDate(userId);
        const lastSeenDate = new Date(lastSeenDateStr);
        let count = 0;
        for (let item of items) { if (new Date(item.DateCreated) > lastSeenDate) count++; else break; }
        lastCount = count;
        dot.innerText = count > 9 ? '9+' : count;
        dot.style.opacity = count > 0 ? '1' : '0';
    };

    // V4: Fetch from /NotifySync/Data
    const fetchData = async () => {
        if (isFetching) return; isFetching = true;
        try {
            // "Smart" Polling: We just fetch the JSON file. It's fast and cached.
            // Jellyfin Controller sends Cache-Control, browser handles it.
            const res = await fetch('/NotifySync/Data?t=' + Date.now()); // Adding t= prevents browser agressively caching if headers fail
            if (!res.ok) throw new Error("API Error");
            const items = await res.json();

            // Snapshot check to avoid re-render if identical
            const snapshot = JSON.stringify(items.map(i => i.Id));
            if (snapshot === localStorage.getItem(SNAPSHOT_KEY) && groupedData) {
                isFetching = false; return; // No change
            }
            localStorage.setItem(SNAPSHOT_KEY, snapshot);

            currentData = items;
            await checkUnread(currentData);
            groupedData = processGrouping(currentData);

            const drop = document.querySelector('#notification-dropdown');
            if (drop && drop.style.display === 'block') updateList(drop);
        } catch (e) { console.error("NotifySync:", e); } finally { isFetching = false; }
    };

    observer = new MutationObserver((m) => { if (!document.querySelector('#netflix-bell')) installBell(); });
    observer.observe(document.body, { childList: true, subtree: true });
    installBell();
})();