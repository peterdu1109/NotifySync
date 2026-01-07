/* NOTIFYSYNC V2.0 - NETFLIX EDITION (Hero, Filters, Pills, Hover Cards) */
(function () {
    let currentData = null;
    let groupedData = null;
    let observer = null;
    let pluginConfig = null;
    let lastCount = 0;
    let isFetching = false;
    let retryDelay = 5000;
    let activeFilter = 'All'; // All, Movie, Episode

    const PLUGIN_ID = "95655672-2342-4321-8291-321312312312";
    const SOUND_KEY = 'notifysync_muted';
    const SWIPE_SAFE_ZONE = 30;

    const STRINGS = {
        fr: { header: "Récemment ajoutés", empty: "Tout est calme...", markAll: "Tout marquer comme vu", markOne: "Marquer comme vu", recent: "À l'instant", badgeNew: "NOUVEAU", newEps: "nouveaux épisodes. Saison", badgeMovie: "FILM", badgeSeries: "SÉRIE", filterAll: "Tout", filterMovies: "Films", filterSeries: "Séries", play: "Lecture" },
        en: { header: "Recently Added", empty: "All caught up!", markAll: "Mark all as read", markOne: "Mark as read", recent: "Just now", badgeNew: "NEW", newEps: "new episodes. Season", badgeMovie: "MOVIE", badgeSeries: "SERIES", filterAll: "All", filterMovies: "Movies", filterSeries: "Series", play: "Play" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;
    const NOTIF_SOUND = new Audio("data:audio/mp3;base64,//uQRAAAAWMSLwUIYAAsYkXgoQwAEaYLWfkWgAI0wWs/ItAAAG1ineAAA0gAAAB5IneAAA0gAAABzLFwAHMwcAAedxYaMPFmPkQcnhsG8G2hxtxl0q5f77+6/v/7/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8//");

    let hoverAudio = null;
    let hoverTimeout = null;

    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;

        const css = `
            :root { --ns-red: #e50914; --ns-dark: #141414; --ns-gray: #e5e5e5; }
            #bell-container{display:flex!important;align-items:center;justify-content:center;z-index:1000}
            #netflix-bell{display:flex!important;visibility:visible!important;opacity:1!important;position:relative;background:0 0;border:none;cursor:pointer;padding:10px;color:inherit}
            .notification-dot{position:absolute;top:4px;right:4px;min-width:15px;height:15px;padding:0 4px;background-color:var(--ns-red);border-radius:10px;opacity:0;pointer-events:none;transition:opacity .3s;display:flex!important;align-items:center;justify-content:center;color:#fff!important;font-size:9px!important;font-weight:700;box-shadow:0 0 5px rgba(0,0,0,.5);z-index:2}
            
            /* DROPDOWN CONTAINER */
            #notification-dropdown{position:fixed;top:70px;right:20px;width:380px;max-width:90vw;background:#181818;color:#fff;border:1px solid rgba(255,255,255,.1);border-radius:8px;box-shadow:0 0 50px rgba(0,0,0,.8);z-index:999999;display:none;overflow:hidden; font-family: 'Netflix Sans', 'Helvetica Neue', Helvetica, Arial, sans-serif;}
            
            /* FILTER PILLS */
            .filter-bar { padding: 12px 15px; display: flex; gap: 10px; border-bottom: 1px solid rgba(255,255,255,0.1); background: rgba(0,0,0,0.5); backdrop-filter: blur(10px); position: sticky; top: 0; z-index: 10; }
            .filter-pill { font-size: 11px; padding: 5px 12px; border-radius: 20px; border: 1px solid rgba(255,255,255,0.3); cursor: pointer; transition: all 0.2s; opacity: 0.7; user-select: none; }
            .filter-pill:hover { border-color: #fff; opacity: 1; }
            .filter-pill.active { background: #fff; color: #000; border-color: #fff; opacity: 1; font-weight: bold; }

            /* HERO SECTION */
            .hero-section { position: relative; height: 220px; width: 100%; overflow: hidden; display: flex; flex-direction: column; justify-content: flex-end; cursor: pointer; }
            .hero-bg { position: absolute; top: 0; left: 0; right: 0; bottom: 0; background-size: cover; background-position: center; transition: transform 6s ease; }
            .hero-section:hover .hero-bg { transform: scale(1.05); }
            .hero-overlay { position: absolute; top:0; left:0; right:0; bottom:0; background: linear-gradient(to top, #181818 0%, rgba(20,20,20,0.6) 50%, rgba(0,0,0,0.3) 100%); }
            .hero-content { position: relative; z-index: 2; padding: 15px; width: 100%; box-sizing: border-box; }
            .hero-new-badge { background: var(--ns-red); color: white; padding: 2px 6px; font-size: 9px; font-weight: bold; border-radius: 2px; display: inline-block; margin-bottom: 5px; box-shadow: 0 1px 3px rgba(0,0,0,0.4); }
            .hero-title { font-size: 18px; font-weight: 800; text-shadow: 0 1px 2px rgba(0,0,0,0.8); margin-bottom: 5px; line-height: 1.2; }
            .hero-meta { font-size: 11px; color: #a3a3a3; display: flex; gap: 8px; align-items: center; margin-bottom: 10px; }
            .hero-overview { font-size: 11px; color: #dedede; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; margin-bottom: 12px; line-height: 1.4; opacity: 0.9; }
            .hero-play-btn { background: #fff; color: #000; border: none; padding: 6px 16px; font-size: 13px; font-weight: bold; border-radius: 4px; display: inline-flex; align-items: center; gap: 5px; transition: background 0.2s; }
            .hero-play-btn:hover { background: #c1c1c1; }
            
            /* STANDARD LIST ITEM */
            .list-container { max-height: 400px; overflow-y: auto; overflow-x: hidden; }
            .list-container::-webkit-scrollbar { width: 6px; }
            .list-container::-webkit-scrollbar-thumb { background: #444; border-radius: 3px; }
            .dropdown-item{display:flex;align-items:flex-start;padding:12px;border-bottom:1px solid rgba(255,255,255,.05);cursor:pointer;overflow:hidden;position:relative;opacity:0;transform:translateY(10px);transition: background .2s, transform 0.2s cubic-bezier(0.175, 0.885, 0.32, 1.275); background:0 0; z-index:1}
            .dropdown-item.animate-in { opacity: 1; transform: translateY(0); }
            .dropdown-item:hover{background:#252525; z-index:10;}
            
            /* HOVER CARD EFFECT (Desktop only) */
            @media (hover: hover) {
                .dropdown-item:hover .thumb-wrapper { transform: scale(1.05); box-shadow: 0 8px 16px rgba(0,0,0,0.8); }
                .dropdown-item:hover .play-overlay { opacity: 1; }
            }

            .thumb-wrapper{position:relative;width:90px;height:56px;margin-right:12px;flex-shrink:0;overflow:hidden;border-radius:4px;transition: transform 0.3s ease, box-shadow 0.3s ease; background: #222; }
            .dropdown-thumb{width:100%;height:100%;object-fit:cover}
            
            .dropdown-info{flex:1;min-width:0;display:flex;flex-direction:column;}
            .dropdown-title{font-weight:700;font-size:13px;margin-bottom:2px; line-height:1.2;}
            .dropdown-subtitle{font-size:11px;color:#bbb;margin-bottom:4px;}
            .metadata-line { display: flex; gap: 8px; font-size: 10px; color: #888; align-items: center; }
            .rating-badge { border: 1px solid #666; padding: 0 3px; border-radius: 2px; color: #ddd; font-size: 9px; }
            .match-score { color: #46d369; font-weight: bold; }
            
            .item-actions{margin-left:10px;display:flex;flex-direction:column;align-items:center; opacity:0; transition:opacity .2s; justify-content: center; height: 100%;}
            .dropdown-item:hover .item-actions{opacity:1}
            .mark-one-btn{cursor:pointer;font-size:16px;color:#aaa; padding: 5px;}.mark-one-btn:hover{color:#fff;}

            /* SWIPE */
            .swipe-bg{position:absolute;top:0;left:0;bottom:0;width:0;background:var(--ns-red);z-index:0;display:flex;align-items:center;padding-left:20px;opacity:0;transition:opacity .2s}
            
            /* UTILS */
            .play-overlay{position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,.3);display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity .2s;}
            .progress-bg{position:absolute;bottom:0;left:0;right:0;height:2px;background:rgba(255,255,255,.3)}.progress-fill{height:100%;background:var(--ns-red)}
            
            @keyframes shimmer{0%{background-position:-330px 0}100%{background-position:330px 0}}.skeleton-item{display:flex;padding:12px;border-bottom:1px solid rgba(255,255,255,.05)}.skeleton-thumb{width:90px;height:56px;border-radius:4px;background:#333;margin-right:12px}.skeleton-lines{flex:1;display:flex;flex-direction:column;gap:8px}.skeleton-line{height:10px;background:#333;border-radius:4px}.skeleton-shimmer{background:linear-gradient(to right,#333 0,#444 50,#333 100%);background-size:330px 100%;animation:shimmer 1.5s infinite linear}
            
            #notifysync-toast{position:fixed;bottom:30px;left:50%;transform:translateX(-50%);background:#fff;color:#000;padding:10px 24px;border-radius:4px;font-weight:bold;box-shadow:0 5px 20px rgba(0,0,0,.5);z-index:10000;opacity:0;transition:opacity .3s,transform .3s;pointer-events:none;}#notifysync-toast.visible{opacity:1;transform:translateX(-50%) translateY(-10px)}
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
        if (!document.getElementById('notifysync-toast')) { const t = document.createElement('div'); t.id = 'notifysync-toast'; document.body.appendChild(t); }
    };

    const playHoverSound = (itemId) => {
        if (localStorage.getItem(SOUND_KEY) === 'true') return; // Muted
        if (hoverTimeout) clearTimeout(hoverTimeout);
        if (hoverAudio) { hoverAudio.pause(); hoverAudio = null; }

        hoverTimeout = setTimeout(async () => {
            const client = window.ApiClient;
            try {
                // Check for theme songs
                const songs = await client.getThemeSongs(client.getCurrentUserId(), itemId);
                if (songs && songs.Items.length > 0) {
                    const songId = songs.Items[0].Id;
                    const url = client.getUrl(`Audio/${songId}/stream`);
                    hoverAudio = new Audio(url);
                    hoverAudio.volume = 0.0;
                    await hoverAudio.play();
                    // Fade in
                    let vol = 0;
                    const fade = setInterval(() => { vol += 0.05; if (vol >= 0.3) { vol = 0.3; clearInterval(fade); } hoverAudio.volume = vol; }, 200);
                }
            } catch (e) { }
        }, 800); // 800ms delay before playing
    };

    const stopHoverSound = () => {
        if (hoverTimeout) clearTimeout(hoverTimeout);
        if (hoverAudio) {
            const a = hoverAudio;
            hoverAudio = null;
            // Fade out
            let vol = a.volume;
            const fade = setInterval(() => { vol -= 0.05; if (vol <= 0) { a.pause(); clearInterval(fade); } else a.volume = vol; }, 100);
        }
    };

    const showToast = (msg) => {
        const t = document.getElementById('notifysync-toast'); if (t) { t.innerHTML = msg; t.classList.add('visible'); setTimeout(() => t.classList.remove('visible'), 3000); }
    };

    const getRemoteLastSeenDate = async (uid) => { try { const r = await fetch(`/NotifySync/LastSeen/${uid}?t=` + Date.now()); if (r.ok) return await r.text(); } catch (e) { } return "2000-01-01T00:00:00.000Z"; };
    const setRemoteLastSeenDate = async (uid, dateStr) => { try { await fetch(`/NotifySync/LastSeen/${uid}?date=${encodeURIComponent(dateStr)}`, { method: 'POST' }); } catch (e) { } };

    // Logic: Is New if created < 48 hours ago
    const isNew = (d) => (new Date() - new Date(d)) < (48 * 3600 * 1000);

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

    const renderHeader = (drop) => {
        const isMuted = localStorage.getItem(SOUND_KEY) === 'true';
        let h = `<div class="dropdown-header" style="display:flex;justify-content:space-between;padding:15px;background:#000;">
                    <span style="font-weight:bold;font-size:16px;">Notifications</span>
                    <div style="display:flex;gap:15px;">
                        <span class="material-icons mute-btn" style="cursor:pointer;opacity:0.7;">${isMuted ? 'volume_off' : 'volume_up'}</span>
                        <span class="material-icons mark-all-btn" style="cursor:pointer;opacity:0.7;" title="${T.markAll}">done_all</span>
                    </div>
                 </div>
                 <div class="filter-bar">
                    <div class="filter-pill ${activeFilter === 'All' ? 'active' : ''}" data-f="All">${T.filterAll}</div>
                    <div class="filter-pill ${activeFilter === 'Movie' ? 'active' : ''}" data-f="Movie">${T.filterMovies}</div>
                    <div class="filter-pill ${activeFilter === 'Episode' ? 'active' : ''}" data-f="Episode">${T.filterSeries}</div>
                 </div>
                 <div class="list-container"></div>`;
        drop.innerHTML = h;
    };

    const buildDropdownHTML = () => {
        const drop = document.querySelector('#notification-dropdown'); if (!drop) return;
        renderHeader(drop);
        const container = drop.querySelector('.list-container');

        if (!groupedData || groupedData.length === 0) {
            container.innerHTML = `<div style="padding:40px;text-align:center;color:#666;">${T.empty}</div>`; return;
        }

        // FILTER LOGIC
        let filtered = groupedData;
        if (activeFilter !== 'All') {
            filtered = groupedData.filter(i => (activeFilter === 'Movie' ? i.Type === 'Movie' : (i.Type === 'Episode' || i.IsGroup)));
        }

        if (filtered.length === 0) {
            container.innerHTML = `<div style="padding:40px;text-align:center;color:#666;">${T.empty}</div>`; return;
        }

        const client = window.ApiClient;
        let html = '';

        // HERO ITEM (First item of filtered list)
        const hero = filtered[0];
        if (hero) {
            let heroImg = hero.BackdropImageTags && hero.BackdropImageTags.length > 0 ? client.getUrl(`Items/${hero.Id}/Images/Backdrop/0?quality=60&maxWidth=600`) : client.getUrl(`Items/${hero.SeriesId || hero.Id}/Images/Primary?quality=60&maxWidth=400`);
            // If group, use Series ID for backdrop
            if (hero.IsGroup && hero.SeriesId) {
                // Try to get series backdrop
                heroImg = client.getUrl(`Items/${hero.SeriesId}/Images/Backdrop/0?quality=60&maxWidth=600`);
            }

            let heroTitle = hero.SeriesName || hero.Name;
            if (hero.IsGroup) heroTitle = hero.SeriesName;

            let heroMeta = '';
            if (hero.ProductionYear) heroMeta += `<span>${hero.ProductionYear}</span>`;
            if (hero.CommunityRating) heroMeta += `<span class="match-score">${Math.round(hero.CommunityRating * 10)}% Match</span>`;
            if (hero.Type === 'Movie' && hero.RunTimeTicks) heroMeta += `<span>${Math.round(hero.RunTimeTicks / 600000000)} min</span>`;

            html += `
            <div class="hero-section" onclick="window.location.hash='#!/details?id=${hero.IsGroup ? hero.SeriesId : hero.Id}'">
                <div class="hero-bg" style="background-image:url('${heroImg}')"></div>
                <div class="hero-overlay"></div>
                <div class="hero-content">
                    ${isNew(hero.DateCreated) ? `<div class="hero-new-badge">${T.badgeNew}</div>` : ''}
                    <div class="hero-title">${heroTitle}</div>
                    <div class="hero-meta">${heroMeta}</div>
                    <button class="hero-play-btn" onclick="event.stopPropagation();window.location.hash='#!/play?id=${hero.Id}'">
                        <span class="material-icons">play_arrow</span> ${T.play}
                    </button>
                </div>
            </div>`;
        }

        // REST OF ITEMS
        filtered.slice(1).forEach((item, index) => {
            let imgUrl = client.getUrl(`Items/${item.Id}/Images/Primary?fillHeight=112&fillWidth=180&quality=90`);
            if (item.IsGroup) imgUrl = client.getUrl(`Items/${item.SeriesId}/Images/Primary?fillHeight=112&fillWidth=180&quality=90`);

            let title = item.IsGroup ? item.SeriesName : (item.SeriesName ? item.SeriesName : item.Name);
            let subtitle = item.IsGroup ? `${item.GroupCount} ${T.newEps}` : (item.SeriesName ? item.Name : '');

            // Metadata
            let year = item.ProductionYear || '';
            let rating = item.CommunityRating ? `<span class="match-score">${Math.round(item.CommunityRating * 10)}%</span>` : '';
            if (item.IsGroup) subtitle += (item.IndexNumber ? `. S${item.ParentIndexNumber || 1}` : '');

            let progressHtml = '';
            if (item.UserData && item.UserData.PlaybackPositionTicks && item.RunTimeTicks) {
                const pct = (item.UserData.PlaybackPositionTicks / item.RunTimeTicks) * 100;
                progressHtml = `<div class="progress-bg"><div class="progress-fill" style="width:${pct}%"></div></div>`;
            }

            html += `
            <div class="dropdown-item" id="notif-item-${item.Id}" style="animation-delay:${index * 0.05}s">
                <div class="thumb-wrapper">
                    <img src="${imgUrl}" class="dropdown-thumb">
                    <div class="play-overlay"><span class="material-icons" style="color:#fff;">play_circle</span></div>
                    ${progressHtml}
                </div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    <div class="metadata-line">
                        ${rating}
                        <span>${year}</span>
                        ${item.IsGroup ? '<span class="rating-badge">TV-MA</span>' : ''}
                    </div>
                    ${subtitle ? `<div class="dropdown-subtitle">${subtitle}</div>` : ''}
                </div>
                <div class="item-actions">
                    <span class="material-icons mark-one-btn">visibility</span>
                </div>
            </div>`;
        });

        container.insertAdjacentHTML('beforeend', html);

        // Events
        container.querySelectorAll('.dropdown-item').forEach(el => {
            // Animation class trigger
            requestAnimationFrame(() => el.classList.add('animate-in'));

            // Sound
            el.addEventListener('mouseenter', () => playHoverSound(el.id.replace('notif-item-', '')));
            el.addEventListener('mouseleave', () => stopHoverSound());

            // Click
            el.onclick = (e) => {
                if (e.target.closest('.mark-one-btn')) {
                    // Mark as read
                    // Implementation of mark one...
                    // For now simply notify
                    return;
                }
                const id = el.id.replace('notif-item-', '');
                window.location.hash = `#!/details?id=${id}`;
                drop.style.display = 'none';
            };
        });

        // Setup Headers events
        drop.querySelectorAll('.filter-pill').forEach(pill => {
            pill.onclick = (e) => {
                e.stopPropagation();
                activeFilter = e.target.getAttribute('data-f');
                buildDropdownHTML();
            };
        });

        const muteBtn = drop.querySelector('.mute-btn');
        if (muteBtn) muteBtn.onclick = (e) => {
            e.stopPropagation();
            const m = localStorage.getItem(SOUND_KEY) === 'true';
            localStorage.setItem(SOUND_KEY, !m);
            renderHeader(drop);
            buildDropdownHTML(); // re-render to update icon
        };

        const markAllBtn = drop.querySelector('.mark-all-btn');
        if (markAllBtn) markAllBtn.onclick = async (e) => {
            e.stopPropagation();
            if (groupedData[0]) { await setRemoteLastSeenDate(window.ApiClient.getCurrentUserId(), groupedData[0].DateCreated); showToast(T.markAll); fetchData(); }
        }
    };

    const checkUnread = async (items) => {
        const client = window.ApiClient; if (!client) return;
        const userId = client.getCurrentUserId();
        const dot = document.querySelector('.notification-dot'); if (!dot) return;

        const lastSeenDateStr = await getRemoteLastSeenDate(userId);
        const lastSeenDate = new Date(lastSeenDateStr);

        let count = 0;
        for (let item of items) { if (new Date(item.DateCreated) > lastSeenDate) count++; else break; }

        if (count > lastCount && count > 0 && localStorage.getItem(SOUND_KEY) !== 'true') {
            try { NOTIF_SOUND.play(); } catch (e) { }
        }
        lastCount = count;
        dot.innerText = count > 9 ? '9+' : count;
        dot.style.opacity = count > 0 ? '1' : '0';
    };

    const fetchData = async () => {
        if (isFetching) return; isFetching = true;
        try {
            const client = window.ApiClient; if (!client) { isFetching = false; return; }
            const userId = client.getCurrentUserId();

            if (!pluginConfig) { try { pluginConfig = await client.getPluginConfiguration(PLUGIN_ID); } catch (e) { pluginConfig = { MaxItems: 10 }; } }

            const res = await client.getItems(userId, {
                SortBy: "DateCreated", SortOrder: "Descending", IncludeItemTypes: "Movie,Episode",
                Filters: "IsUnplayed", Recursive: true, Limit: 20,
                Fields: "Name,Id,SeriesName,SeriesId,DateCreated,Type,UserData,RunTimeTicks,BackdropImageTags,Overview,CommunityRating,ProductionYear,ParentIndexNumber,IndexNumber"
            });
            currentData = res.Items;
            await checkUnread(currentData);
            groupedData = processGrouping(currentData);
            // Don't auto rebuild if closed, only if open? 
            // For now, if open, rebuild
            if (document.querySelector('#notification-dropdown').style.display === 'block') buildDropdownHTML();
        } catch (e) { } finally { isFetching = false; }
    };

    const installBell = () => {
        if (document.querySelector('#netflix-bell')) return;
        injectStyles();
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right');
        if (!header) return;
        const bell = document.createElement('button'); bell.id = 'netflix-bell'; bell.className = 'paper-icon-button-light headerButton';
        bell.innerHTML = `<span class="material-icons notifications"></span><div class="notification-dot"></div>`;
        header.prepend(Object.assign(document.createElement('div'), { id: 'bell-container' }).appendChild(bell).parentNode);

        let drop = document.querySelector('#notification-dropdown');
        if (!drop) {
            drop = document.createElement('div'); drop.id = 'notification-dropdown'; document.body.appendChild(drop);
            document.addEventListener('click', e => { if (drop.style.display === 'block' && !drop.contains(e.target) && !bell.contains(e.target)) drop.style.display = 'none'; });
        }

        bell.onclick = (e) => {
            e.preventDefault(); e.stopPropagation();
            if (drop.style.display === 'block') { drop.style.display = 'none'; }
            else { drop.style.display = 'block'; buildDropdownHTML(); }
        };
        fetchData();
        setInterval(fetchData, 60000);
    };

    observer = new MutationObserver((m) => { if (!document.querySelector('#netflix-bell')) installBell(); });
    observer.observe(document.body, { childList: true, subtree: true });
    installBell();
})();