/* NOTIFYSYNC V17 - MOBILE UI POLISHED (Safe Swipe, Date Logic, Mobile Play Button) */
(function() {
    let currentData = null;
    let groupedData = null;
    let observer = null;
    let pluginConfig = null;
    let lastCount = 0;
    let isFetching = false;
    let retryDelay = 5000;

    const PLUGIN_ID = "95655672-2342-4321-8291-321312312312"; 
    const SOUND_KEY = 'notifysync_muted';
    const SWIPE_SAFE_ZONE = 30; // Marge de sécurité à gauche

    const STRINGS = {
        fr: { header: "Derniers ajouts", empty: "Tout est à jour !", markAll: "Tout marquer comme lu", markOne: "Marquer comme vu", recent: "À l'instant", badgeNew: "NOUVEAU", newEps: "nouveaux épisodes", badgeMovie: "FILM", badgeSeries: "SÉRIE" },
        en: { header: "Latest Media", empty: "All caught up!", markAll: "Mark all as read", markOne: "Mark as read", recent: "Just now", badgeNew: "NEW", newEps: "new episodes", badgeMovie: "MOVIE", badgeSeries: "SERIES" }
    };
    const userLang = navigator.language || navigator.userLanguage;
    const T = userLang.startsWith('fr') ? STRINGS.fr : STRINGS.en;
    const NOTIF_SOUND = new Audio("data:audio/mp3;base64,//uQRAAAAWMSLwUIYAAsYkXgoQwAEaYLWfkWgAI0wWs/ItAAAG1ineAAA0gAAAB5IneAAA0gAAABzLFwAHMwcAAedxYaMPFmPkQcnhsG8G2hxtxl0q5f77+6/v/7/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8///4/5/3/3/5/4/5/3/3+uw5MvV7M/J0/3/v///8/7///3/8//");

    const injectStyles = () => {
        if (document.getElementById('notifysync-css')) return;
        
        // CSS MODIFIÉ : Ajout de la règle @media (hover: none) pour le mobile
        const css = `
            #bell-container{display:flex!important;align-items:center;justify-content:center;z-index:1000}
            #netflix-bell{display:flex!important;visibility:visible!important;opacity:1!important;position:relative;background:0 0;border:none;cursor:pointer;padding:10px;color:inherit}
            .notification-dot{position:absolute;top:4px;right:4px;min-width:15px;height:15px;padding:0 4px;background-color:var(--theme-primary-color,#e50914);border-radius:10px;opacity:0;pointer-events:none;transition:opacity .3s;display:flex!important;align-items:center;justify-content:center;color:#fff!important;font-size:9px!important;font-weight:700;font-family:sans-serif;box-shadow:0 0 5px rgba(0,0,0,.5);z-index:2}
            #notification-dropdown{position:fixed;top:70px;right:20px;width:330px;max-width:90vw;background:rgba(25,25,25,.9);backdrop-filter:blur(15px);-webkit-backdrop-filter:blur(15px);color:#fff;border:1px solid rgba(255,255,255,.1);border-radius:12px;box-shadow:0 10px 40px rgba(0,0,0,.6);z-index:999999;display:none;overflow:hidden}
            .dropdown-bg{position:absolute;top:0;left:0;right:0;bottom:0;background-size:cover;background-position:center;opacity:.15;transition:background-image .5s ease-in-out;filter:blur(25px);z-index:-1}
            .dropdown-overlay{position:absolute;top:0;left:0;right:0;bottom:0;background:linear-gradient(to bottom,rgba(18,18,18,.85),rgba(18,18,18,.98));z-index:-1}
            .dropdown-header{padding:15px;font-weight:700;border-bottom:1px solid rgba(255,255,255,.1);font-size:14px;display:flex;justify-content:space-between;align-items:center;background:rgba(0,0,0,.2)}
            .header-actions{display:flex;align-items:center;gap:15px}
            .header-icon{cursor:pointer;opacity:.7;transition:opacity .2s;font-size:18px}.header-icon:hover{opacity:1;color:var(--theme-primary-color,#4CAF50)}
            .swipe-bg{position:absolute;top:0;left:0;bottom:0;width:0;background:var(--theme-primary-color,#4CAF50);z-index:0;display:flex;align-items:center;padding-left:20px;opacity:0;transition:opacity .2s}
            .swipe-icon{color:#fff;font-size:24px;font-weight:700}
            @keyframes slideIn{from{opacity:0;transform:translateY(-5px)}to{opacity:1;transform:translateY(0)}}
            .dropdown-item{display:flex;align-items:center;padding:12px;border-bottom:1px solid rgba(255,255,255,.05);cursor:pointer;overflow:hidden;min-height:70px;height:auto;position:relative;animation:slideIn .2s ease-out forwards;opacity:0;transition:background .2s,transform .1s;background:0 0;z-index:1}
            .dropdown-item:hover{background:rgba(255,255,255,.08)}
            .dropdown-item:nth-child(1){animation-delay:.05s}.dropdown-item:nth-child(2){animation-delay:.1s}.dropdown-item:nth-child(3){animation-delay:.15s}.dropdown-item:nth-child(4){animation-delay:.2s}.dropdown-item:nth-child(5){animation-delay:.25s}
            .item-actions{margin-left:10px;display:flex;align-items:center;opacity:.3;transition:opacity .2s}.dropdown-item:hover .item-actions{opacity:1}
            .mark-one-btn{cursor:pointer;font-size:18px;color:#fff}.mark-one-btn:hover{color:var(--theme-primary-color,#4CAF50);transform:scale(1.1);transition:transform .2s}
            .thumb-wrapper{position:relative;width:45px;height:68px;margin-right:15px;flex-shrink:0;overflow:hidden;border-radius:4px;box-shadow:0 4px 8px rgba(0,0,0,.6)}
            .dropdown-thumb{width:100%;height:100%;object-fit:cover}
            
            /* START PLAY BUTTON LOGIC */
            .play-overlay{position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,.4);display:flex;align-items:center;justify-content:center;opacity:0;transition:opacity .2s;backdrop-filter:blur(1px)}
            .thumb-wrapper:hover .play-overlay{opacity:1} /* PC: Survol */
            @media (hover: none) { .play-overlay { opacity: 1 !important; background: rgba(0,0,0,0.15) !important; } } /* MOBILE: Toujours visible, fond plus clair */
            /* END PLAY BUTTON LOGIC */

            .play-icon{font-size:28px;color:#fff;cursor:pointer;filter:drop-shadow(0 0 5px rgba(0,0,0,.8));transition:transform .2s}.play-icon:hover{transform:scale(1.2);color:var(--theme-primary-color,#e50914)}
            .progress-bg{position:absolute;bottom:0;left:0;right:0;height:3px;background:rgba(0,0,0,.8)}.progress-fill{height:100%;background:var(--theme-primary-color,#e50914)}
            .type-badge{position:absolute;bottom:0;left:0;right:0;font-size:8px;text-align:center;color:#fff;background:rgba(0,0,0,.85);padding:2px 0;text-transform:uppercase;font-weight:700;backdrop-filter:blur(2px)}.badge-movie{border-top:2px solid var(--theme-primary-color,#e50914)}.badge-series{border-top:2px solid #2196f3}
            @keyframes pulse-red{0%{box-shadow:0 0 0 0 rgba(229,9,20,.7)}70%{box-shadow:0 0 0 4px rgba(229,9,20,0)}100%{box-shadow:0 0 0 0 rgba(229,9,20,0)}}.new-badge{font-size:8px;background:#e50914;color:#fff;padding:2px 4px;border-radius:2px;font-weight:700;margin-left:6px;display:inline-block;vertical-align:middle;animation:pulse-red 2s infinite}
            .dropdown-info{flex:1;min-width:0;display:flex;flex-direction:column;justify-content:center;pointer-events:none}
            .dropdown-title{font-weight:700;font-size:13px;white-space:normal;overflow:visible;width:100%;line-height:1.3;margin-bottom:3px}.dropdown-subtitle{font-size:11px;color:#bbb;margin-bottom:3px}.dropdown-date{font-size:10px;opacity:.6;display:flex;align-items:center}
            .empty-state{padding:40px 10px;text-align:center;color:#999;font-size:13px}.empty-state .material-icons{font-size:48px;margin-bottom:15px;display:block;opacity:.5;margin:0 auto 15px auto}
            @keyframes shimmer{0%{background-position:-330px 0}100%{background-position:330px 0}}.skeleton-item{display:flex;align-items:center;padding:12px;border-bottom:1px solid rgba(255,255,255,.05)}.skeleton-thumb{width:45px;height:68px;border-radius:4px;background:#333;margin-right:15px}.skeleton-lines{flex:1;display:flex;flex-direction:column;gap:8px}.skeleton-line{height:10px;background:#333;border-radius:4px}.skeleton-line.short{width:60%}.skeleton-shimmer{background:linear-gradient(to right,#333 0,#444 50%,#333 100%);background-size:330px 100%;animation:shimmer 1.5s infinite linear}
            @keyframes fadeOutSlide{to{opacity:0;transform:translateX(100%);height:0;padding:0;margin:0;border:none}}.dropdown-item.removing{animation:fadeOutSlide .3s ease-in forwards;pointer-events:none}
            #notifysync-toast{position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:rgba(30,30,30,.95);backdrop-filter:blur(10px);color:#fff;padding:10px 20px;border-radius:30px;font-size:13px;box-shadow:0 5px 20px rgba(0,0,0,.5);z-index:10000;opacity:0;transition:opacity .3s,transform .3s;pointer-events:none;display:flex;align-items:center;gap:8px;border:1px solid rgba(255,255,255,.1)}#notifysync-toast.visible{opacity:1;transform:translateX(-50%) translateY(-10px)}
        `;
        const style = document.createElement('style'); style.id = 'notifysync-css'; style.textContent = css; document.head.appendChild(style);
        if(!document.getElementById('notifysync-toast')) { const t = document.createElement('div'); t.id = 'notifysync-toast'; document.body.appendChild(t); }
    };

    const showToast = (msg, icon = 'check') => {
        const t = document.getElementById('notifysync-toast'); if(t) { t.innerHTML = `<span class="material-icons" style="font-size:16px;">${icon}</span> ${msg}`; t.classList.add('visible'); setTimeout(() => t.classList.remove('visible'), 3000); }
    };

    const requestNotificationPermission = () => { if ("Notification" in window && Notification.permission === "default") Notification.requestPermission(); };
    const sendNativeNotification = (c) => { if ("Notification" in window && Notification.permission === "granted") new Notification("Jellyfin", { body: `${c} ${T.header} !`, icon: '/web/img/touchicon.png', silent: true }); };

    const getRemoteLastSeenDate = async (uid) => { try { const r = await fetch(`/NotifySync/LastSeen/${uid}?t=` + Date.now()); if(r.ok) return await r.text(); } catch(e) {} return "2000-01-01T00:00:00.000Z"; };
    const setRemoteLastSeenDate = async (uid, dateStr) => { try { await fetch(`/NotifySync/LastSeen/${uid}?date=${encodeURIComponent(dateStr)}`, { method: 'POST' }); } catch(e) {} };
    
    const timeSince = (d) => { const s = Math.floor((new Date()-new Date(d))/1000); let i=s/86400; if(i>1)return Math.floor(i)+" j"; i=s/3600; if(i>1)return Math.floor(i)+" h"; return T.recent; };
    const isNew = (d) => (new Date()-new Date(d))<(48*3600*1000);

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

    const buildDropdownHTML = () => {
        const drop = document.querySelector('#notification-dropdown'); if (!drop) return;
        const isMuted = localStorage.getItem(SOUND_KEY) === 'true';
        let html = `<div class="dropdown-bg"></div><div class="dropdown-overlay"></div><div class="dropdown-header"><span>${T.header}</span><div class="header-actions"><span class="material-icons header-icon mute-btn" title="Son">${isMuted?'volume_off':'volume_up'}</span><span class="material-icons header-icon mark-all-btn" title="${T.markAll}">done_all</span></div></div>`;
        if (!groupedData || groupedData.length === 0) {
            html += `<div class="empty-state"><span class="material-icons">check_circle_outline</span><div>${T.empty}</div></div>`; drop.innerHTML = html; return;
        }

        const client = window.ApiClient;
        groupedData.forEach(item => {
            let imgUrl, title, subtitle, badgeHtml, progressHtml = '', playBtnHtml = '', newBadge = '';
            let dateToMark = item.DateCreated; 

            let backdropUrl = item.BackdropImageTags && item.BackdropImageTags.length > 0 ? client.getUrl(`Items/${item.Id}/Images/Backdrop/0?quality=50&maxWidth=400`) : null;
            if (item.IsGroup && !backdropUrl && item.SeriesId) backdropUrl = client.getUrl(`Items/${item.SeriesId}/Images/Backdrop/0?quality=50&maxWidth=400`);

            if (item.UserData && item.UserData.PlaybackPositionTicks && item.RunTimeTicks) {
                const pct = (item.UserData.PlaybackPositionTicks / item.RunTimeTicks) * 100;
                if (pct > 0 && pct < 100) progressHtml = `<div class="progress-bg"><div class="progress-fill" style="width:${pct}%"></div></div>`;
            }

            if (item.IsGroup) {
                imgUrl = client.getUrl(`Items/${item.SeriesId||item.Id}/Images/Primary?fillHeight=140&fillWidth=100&quality=90`);
                title = item.SeriesName;
                subtitle = `<span style="color:var(--theme-primary-color, #4CAF50); font-weight:bold;">${item.GroupCount} ${T.newEps}</span>`;
                badgeHtml = `<div class="type-badge badge-series">${T.badgeSeries}</div>`;
                dateToMark = item.LatestDate || item.DateCreated;
            } else {
                imgUrl = client.getUrl(`Items/${item.Id}/Images/Primary?fillHeight=140&fillWidth=100&quality=90`);
                title = item.SeriesName ? `${item.SeriesName} - ${item.Name}` : item.Name;
                subtitle = item.SeriesName ? item.Name : "";
                let typeText = item.Type === 'Movie' ? T.badgeMovie : T.badgeEpisode;
                let badgeClass = item.Type === 'Movie' ? 'badge-movie' : 'badge-series';
                if(!progressHtml) badgeHtml = `<div class="type-badge ${badgeClass}">${typeText}</div>`;
                playBtnHtml = `<div class="play-overlay"><span class="material-icons play-icon" data-play-id="${item.Id}">play_circle_filled</span></div>`;
            }
            if(isNew(item.DateCreated)) newBadge = `<span class="new-badge">${T.badgeNew}</span>`;

            html += `
            <div class="dropdown-item" id="notif-item-${item.Id}" data-date="${dateToMark}" data-id="${item.Id}" data-backdrop="${backdropUrl || ''}">
                <div class="swipe-bg"><span class="material-icons swipe-icon">check</span></div>
                <div class="thumb-wrapper">
                    <img src="${imgUrl}" class="dropdown-thumb">
                    ${playBtnHtml}
                    ${badgeHtml || ''}
                    ${progressHtml}
                </div>
                <div class="dropdown-info">
                    <div class="dropdown-title">${title}</div>
                    ${subtitle ? `<div class="dropdown-subtitle">${subtitle}</div>` : ''}
                    <div class="dropdown-date">${timeSince(item.DateCreated)} ${newBadge}</div>
                </div>
                <div class="item-actions" title="${T.markOne}"><span class="material-icons mark-one-btn">visibility</span></div>
            </div>`;
        });
        drop.innerHTML = html;

        const bgLayer = drop.querySelector('.dropdown-bg');
        drop.querySelectorAll('.dropdown-item').forEach(el => {
            const dateStr = el.getAttribute('data-date');
            const itemId = el.getAttribute('data-id');

            el.addEventListener('mouseenter', () => {
                const url = el.getAttribute('data-backdrop');
                if(url && bgLayer) bgLayer.style.backgroundImage = `url('${url}')`;
            });

            // SWIPE LOGIC
            let touchStartX = 0; let currentX = 0;
            const swipeBg = el.querySelector('.swipe-bg');
            
            el.addEventListener('touchstart', (e) => { 
                if (e.changedTouches[0].screenX < SWIPE_SAFE_ZONE) { touchStartX = -1; return; }
                touchStartX = e.changedTouches[0].screenX; 
            }, {passive: true});

            el.addEventListener('touchmove', (e) => {
                if (touchStartX === -1) return;
                currentX = e.changedTouches[0].screenX;
                let diff = currentX - touchStartX;
                if(diff > 0 && diff < 150) {
                    el.style.transform = `translateX(${diff}px)`;
                    if(swipeBg) { swipeBg.style.width = `${diff}px`; swipeBg.style.opacity = Math.min(diff/80, 1); }
                }
            }, {passive: true});

            el.addEventListener('touchend', async (e) => {
                if (touchStartX === -1) return;
                let diff = currentX - touchStartX;
                if(diff > 80) {
                    el.style.transform = `translateX(100%)`;
                    await markAsReadAction(itemId, dateStr);
                } else {
                    el.style.transform = 'translateX(0)';
                    if(swipeBg) { swipeBg.style.width = '0'; swipeBg.style.opacity = '0'; }
                }
            });

            el.onclick = (e) => { 
                if(e.target.classList.contains('play-icon') || e.target.classList.contains('mark-one-btn')) return;
                window.location.hash = `#!/details?id=${itemId}`; drop.style.display = 'none'; 
            };
            const eyeBtn = el.querySelector('.mark-one-btn');
            if(eyeBtn) eyeBtn.onclick = async (e) => { e.stopPropagation(); await markAsReadAction(itemId, dateStr); };
        });

        const playBtns = drop.querySelectorAll('.play-icon');
        playBtns.forEach(btn => btn.onclick = (e) => { e.stopPropagation(); window.location.hash = `#!/play?id=${btn.getAttribute('data-play-id')}`; drop.style.display = 'none'; });

        const muteBtn = drop.querySelector('.mute-btn');
        if(muteBtn) muteBtn.onclick = (e) => { e.stopPropagation(); localStorage.setItem(SOUND_KEY, !(localStorage.getItem(SOUND_KEY) === 'true')); buildDropdownHTML(); };
        
        const markAllBtn = drop.querySelector('.mark-all-btn');
        if(markAllBtn && groupedData.length > 0) markAllBtn.onclick = async (evt) => { 
            evt.stopPropagation(); 
            if(groupedData[0]) { await setRemoteLastSeenDate(window.ApiClient.getCurrentUserId(), groupedData[0].DateCreated || groupedData[0].LatestDate); showToast(T.markAll, 'done_all'); fetchData(); } 
        };
    };

    const markAsReadAction = async (itemId, dateStr) => {
        const row = document.getElementById(`notif-item-${itemId}`);
        if(row) row.classList.add('removing');
        await setRemoteLastSeenDate(window.ApiClient.getCurrentUserId(), dateStr); 
        showToast(T.markOne);
        setTimeout(() => fetchData(), 300);
    };

    const checkUnread = async (items) => { 
        const client = window.ApiClient; if (!client) return;
        const userId = client.getCurrentUserId();
        const dot = document.querySelector('.notification-dot'); if (!dot) return;
        if (!items || items.length === 0) { dot.style.opacity = '0'; lastCount = 0; return; }
        
        const lastSeenDateStr = await getRemoteLastSeenDate(userId);
        const lastSeenDate = new Date(lastSeenDateStr);

        let count = 0;
        for (let item of items) {
            if (new Date(item.DateCreated) > lastSeenDate) count++;
            else break;
        }

        if (count > lastCount && count > 0) { 
            if(localStorage.getItem(SOUND_KEY) !== 'true') { try { NOTIF_SOUND.play(); } catch(e) {} }
            sendNativeNotification(count);
        }
        lastCount = count;
        if (count > 0) { dot.innerText = count; dot.style.opacity = '1'; } else { dot.style.opacity = '0'; }
    };

    const renderSkeleton = () => { const drop = document.querySelector('#notification-dropdown'); if(!drop) return; let h = `<div class="dropdown-bg"></div><div class="dropdown-overlay"></div><div class="dropdown-header"><span>${T.header}</span></div>`; for(let i=0; i<3; i++) h += `<div class="skeleton-item"><div class="skeleton-thumb skeleton-shimmer"></div><div class="skeleton-lines"><div class="skeleton-line skeleton-shimmer" style="width: 90%;"></div><div class="skeleton-line short skeleton-shimmer"></div></div></div>`; drop.innerHTML = h; };

    const fetchData = async () => { 
        if(isFetching) return; isFetching = true;
        try { 
            const client = window.ApiClient; if (!client) { isFetching=false; return; }
            const userId = client.getCurrentUserId(); 
            const drop = document.querySelector('#notification-dropdown');
            if(drop && (!currentData || currentData.length === 0)) renderSkeleton();

            if (!pluginConfig) { try { pluginConfig = await client.getPluginConfiguration(PLUGIN_ID); } catch(e) { pluginConfig = { MaxItems: 5 }; } }
            const userLimit = pluginConfig.MaxItems || 5;
            
            const res = await client.getItems(userId, { 
                SortBy: "DateCreated", SortOrder: "Descending", IncludeItemTypes: "Movie,Episode", 
                Filters: "IsUnplayed", Recursive: true, Limit: userLimit * 4, 
                Fields: "Name,Id,SeriesName,SeriesId,DateCreated,Type,UserData,RunTimeTicks,BackdropImageTags" 
            });
            currentData = res.Items; 
            await checkUnread(currentData);
            groupedData = processGrouping(currentData).slice(0, userLimit);
            buildDropdownHTML(); 
            retryDelay = 5000; 
        } catch (e) { setTimeout(fetchData, retryDelay); retryDelay = Math.min(retryDelay * 2, 60000); } finally { isFetching = false; }
    };

    const installBell = () => { 
        if (document.querySelector('#netflix-bell')) return; 
        injectStyles(); requestNotificationPermission();
        const header = document.querySelector('.headerRight') || document.querySelector('.headerButtons-right'); 
        if (!header) return;
        const bell = document.createElement('button'); bell.id = 'netflix-bell'; bell.className = 'paper-icon-button-light headerButton'; 
        bell.innerHTML = `<span class="material-icons notifications"></span><div class="notification-dot"></div>`; 
        header.prepend(Object.assign(document.createElement('div'), {id:'bell-container'}).appendChild(bell).parentNode);
        let drop = document.querySelector('#notification-dropdown'); 
        if (!drop) { drop = document.createElement('div'); drop.id = 'notification-dropdown'; document.body.appendChild(drop); document.addEventListener('click', e => { if(drop.style.display==='block' && !drop.contains(e.target) && !bell.contains(e.target)) drop.style.display='none'; }); } 
        const toggleMenu = () => { if (drop.style.display === 'block') { drop.style.display = 'none'; } else { if (drop.innerHTML === "") renderSkeleton(); drop.style.display = 'block'; } };
        bell.onclick = (e) => { e.preventDefault(); e.stopPropagation(); toggleMenu(); }; 
        document.addEventListener('keydown', (e) => { if((e.key === 'n' || e.key === 'N') && !['INPUT','TEXTAREA'].includes(document.activeElement.tagName)) toggleMenu(); });
        fetchData();
    };

    observer = new MutationObserver((m) => { if (!document.querySelector('#netflix-bell')) installBell(); });
    observer.observe(document.body, { childList: true, subtree: true });
    setInterval(fetchData, 60000);
    installBell();
})();