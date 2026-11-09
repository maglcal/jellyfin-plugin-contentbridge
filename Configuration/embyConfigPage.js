/*
 * Emby configuration-page controller (shared by two boot channels).
 *
 * Channel A — modern Emby web (4.8.x / 4.10.x): the page root declares
 *   data-controller="__plugin/contentbridgejs"; the dashboard loads THIS file as an AMD
 *   module and instantiates the exported View. (Inline <script> blocks in plugin pages
 *   never execute there — the page HTML is inserted via innerHTML.)
 *
 * Channel B — older dashboards that do execute inline scripts: embyConfigPage.html's
 *   inline bootstrap loads THIS file as a plain <script> with window.define hidden, then
 *   calls window.__cbCore.init(root).
 *
 * Robustness rules learned from two rounds of real-host testing:
 *   - the module declares NO AMD dependencies (a single unresolvable dependency makes the
 *     view activation hang forever → blank page);
 *   - Emby custom-element modules are loaded best-effort AFTER bind (pure styling; plain
 *     inputs still work if they never load);
 *   - every failure path ends in a visible #cbStatus message, never a silent spinner.
 *
 * (Jellyfin uses a different convention — see configPage.html, which carries the same
 * field logic as an inline script. Keep both in sync.)
 */
(function () {
    'use strict';

    var pluginId = 'b42fcd11-31f8-40fb-b605-77fc7b182edc';

    // ---- host-diff helper layer (Compat seam) ------------------------------
    // Every host call goes through normalize/apiGet/apiPost so host drift stays in one place.

    // normalize ApiClient.ajax results across apiclient versions
    function normalize(r) {
        if (r == null) return null;
        if (typeof r === 'string') { try { return JSON.parse(r); } catch (e) { return null; } }
        if (typeof r.json === 'function') return r.json().then(function (j) { return j; });
        if (r.response != null && typeof r.response === 'object') return r.response;
        return r;
    }

    function apiGet(path, params) {
        // ApiClient.getJSON is the documented Emby JSON GET and handles auth.
        return Promise.resolve(ApiClient.getJSON(ApiClient.getUrl(path, params))).then(normalize);
    }

    function apiPost(path, body) {
        var url = ApiClient.getUrl(path);
        var payload = body ? JSON.stringify(body) : null;

        function request() {
            if (typeof ApiClient.ajax === 'function') {
                return Promise.resolve(ApiClient.ajax({
                    type: 'POST',
                    url: url,
                    data: payload,
                    contentType: 'application/json',
                    dataType: 'json'
                })).then(normalize);
            }
            // Fallback: plain fetch with the session token.
            var token = '';
            try {
                token = (typeof ApiClient.accessToken === 'function') ? ApiClient.accessToken() : (ApiClient.accessToken || '');
            } catch (e) { /* leave empty */ }
            return fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Emby-Token': token },
                body: payload
            }).then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text().then(function (t) { try { return JSON.parse(t); } catch (e) { return null; } });
            });
        }

        return request().catch(function (e) {
            // Turn opaque rejections into a readable message for the status line.
            var msg = (e && (e.message || e.statusText)) || '请求出错';
            if (e && e.status) msg = 'HTTP ' + e.status;
            throw new Error(msg);
        });
    }

    function status(view, text) {
        var el = view.querySelector('#cbStatus');
        if (el) el.textContent = text || '';
    }

    // ---- form rendering / collection --------------------------------------

    function sourceRow(view, s) {
        var div = document.createElement('div');
        div.className = 'verticalSection cbRowBox';
        div.innerHTML =
            '<div class="inputContainer"><select is="emby-select" class="cbSrcProtocol" label="数据源类型"><option value="maccms">标准接口（苹果CMS/海螺）</option><option value="feed">自定义 Feed（FEED-SPEC.md 规范）</option></select></div>' +
            '<div class="inputContainer"><input is="emby-input" type="text" class="cbSrcName" label="名称" /></div>' +
            '<div class="inputContainer"><input is="emby-input" type="text" class="cbSrcUrl" label="接口地址 / Feed 文件·目录·URL" /></div>' +
            '<div class="inputContainer"><input is="emby-input" type="number" class="cbSrcType" label="分类 ID（0 = 全部）" /></div>' +
            '<div class="inputContainer"><input is="emby-input" type="number" class="cbSrcMax" label="每次同步条数" /></div>' +
            '<div class="checkboxContainer checkboxContainer-withDescription"><label><input type="checkbox" is="emby-checkbox" class="cbSrcUseProxy" /><span>走本地代理</span></label><div class="fieldDescription">播放地址需要校验请求头（Referer/UA）时开启，.strm 会写代理链接</div></div>' +
            '<div class="inputContainer"><input is="emby-input" type="text" class="cbSrcUa" label="User-Agent（代理模式）" /></div>' +
            '<div class="inputContainer"><input is="emby-input" type="text" class="cbSrcRef" label="Referer（代理模式）" /></div>' +
            '<div class="checkboxContainer"><label><input type="checkbox" is="emby-checkbox" class="cbSrcEnabled" /><span>启用</span></label></div>' +
            '<button is="emby-button" type="button" class="raised cbSrcRemove"><span>删除</span></button>';
        div.querySelector('.cbSrcProtocol').value = s.Type === 'feed' ? 'feed' : 'maccms';
        div.querySelector('.cbSrcName').value = s.Name || '';
        div.querySelector('.cbSrcUrl').value = s.ApiUrl || '';
        div.querySelector('.cbSrcType').value = s.TypeId != null ? s.TypeId : 0;
        div.querySelector('.cbSrcMax').value = s.MaxItems != null ? s.MaxItems : 20;
        div.querySelector('.cbSrcUseProxy').checked = s.UseProxy === true;
        div.querySelector('.cbSrcUa').value = s.UserAgent || '';
        div.querySelector('.cbSrcRef').value = s.Referer || '';
        div.querySelector('.cbSrcEnabled').checked = s.Enabled !== false;
        div.querySelector('.cbSrcRemove').addEventListener('click', function () {
            div.parentNode.removeChild(div);
        });
        return div;
    }

    function renderSources(view, sources) {
        var container = view.querySelector('#cbSources');
        container.innerHTML = '';
        (sources || []).forEach(function (s) {
            container.appendChild(sourceRow(view, s));
        });
        refreshSourceSelect(view);
    }

    function collectSources(view) {
        var sources = [];
        var rows = view.querySelectorAll('#cbSources > div');
        for (var i = 0; i < rows.length; i++) {
            var div = rows[i];
            sources.push({
                Type: div.querySelector('.cbSrcProtocol').value === 'feed' ? 'feed' : 'maccms',
                Name: div.querySelector('.cbSrcName').value.trim(),
                ApiUrl: div.querySelector('.cbSrcUrl').value.trim(),
                TypeId: parseInt(div.querySelector('.cbSrcType').value, 10) || 0,
                MaxItems: parseInt(div.querySelector('.cbSrcMax').value, 10) || 20,
                UseProxy: div.querySelector('.cbSrcUseProxy').checked,
                UserAgent: div.querySelector('.cbSrcUa').value.trim(),
                Referer: div.querySelector('.cbSrcRef').value.trim(),
                Enabled: div.querySelector('.cbSrcEnabled').checked
            });
        }
        return sources;
    }

    function refreshSourceSelect(view) {
        var select = view.querySelector('#cbSearchSource');
        var current = select.value;
        select.innerHTML = '';
        collectSources(view).forEach(function (s) {
            if (!s.Name) return;
            var opt = document.createElement('option');
            opt.value = s.Name;
            opt.textContent = s.Name;
            select.appendChild(opt);
        });
        if (current) select.value = current;
    }

    function fillForm(view, cfg) {
        view.querySelector('#cbLibraryRoot').value = cfg.LibraryRoot || '';
        view.querySelector('#cbPreferredQuality').value = cfg.PreferredQuality || 'm3u8';
        view.querySelector('#cbProxyBaseUrl').value = cfg.ProxyBaseUrl || '';
        view.querySelector('#cbWriteNfo').checked = cfg.WriteNfo !== false;
        view.querySelector('#cbDownloadImages').checked = cfg.DownloadImages !== false;
        view.querySelector('#cbCleanupRemoved').checked = cfg.CleanupRemoved === true;
        view.querySelector('#cbUpdatePrimaryUrl').value = cfg.UpdatePrimaryUrl || '';
        renderSources(view, cfg.Sources || []);
    }

    var schedulePresets = [
        { h: 0, label: '仅手动（不自动同步）' },
        { h: 1, label: '每小时' },
        { h: 6, label: '每 6 小时' },
        { h: 12, label: '每 12 小时' },
        { h: 24, label: '每天' },
        { h: 168, label: '每周' }
    ];

    function fillSchedule(view, sch) {
        var sel = view.querySelector('#cbSyncInterval');
        sel.innerHTML = '';
        var current = (sch && sch.Enabled && sch.IntervalHours) ? sch.IntervalHours : 0;

        // Non-interval trigger configured directly in 计划任务: keep it untouched by default.
        var custom = !!(sch && sch.TriggerType && !sch.IntervalHours);

        var options = schedulePresets.slice();
        var matched = options.some(function (o) { return o.h === current; });
        if (current > 0 && !matched) {
            options.push({ h: current, label: '每 ' + current + ' 小时（自定义）' });
        }
        if (custom) {
            options.push({ h: -1, label: '自定义触发器（在计划任务中配置，保存此处不会改动它）' });
            current = -1;
        }

        options.forEach(function (o) {
            var opt = document.createElement('option');
            opt.value = String(o.h);
            opt.textContent = o.label;
            sel.appendChild(opt);
        });
        sel.value = String(current);
    }

    function saveSchedule(view) {
        var v = view.querySelector('#cbSyncInterval').value;
        if (v === '-1') {
            // custom trigger owned by 计划任务; leave it alone
            return Promise.resolve();
        }
        var hours = parseFloat(v) || 0;
        return apiPost('ContentBridge/schedule', { IntervalHours: hours > 0 ? hours : null })
            .catch(function () { return Promise.reject(new Error('自动同步计划保存失败')); });
    }

    function loadConfig(view) {
        Promise.all([
            ApiClient.getPluginConfiguration(pluginId),
            apiGet('ContentBridge/schedule').catch(function () { return null; })
        ]).then(function (results) {
            fillForm(view, results[0] || {});
            fillSchedule(view, results[1]);
            status(view, '');
        }, function () {
            status(view, '配置加载失败，请刷新重试');
        });
    }

    // ---- page binding (single entry used by both boot channels) -----------

    function bind(view) {
        // Best-effort: upgrade Emby custom elements (styling only; plain inputs work
        // without them). Fire-and-forget so a missing module can never hang the page.
        if (typeof require === 'function') {
            try {
                require(['emby-input', 'emby-button', 'emby-checkbox', 'emby-select', 'emby-scroller'], function () { }, function () { });
            } catch (e) { /* ignore */ }
        }

        view.querySelector('.contentbridgeConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            status(view, '保存中…');
            ApiClient.getPluginConfiguration(pluginId).then(function (config) {
                config = config || {};
                config.LibraryRoot = view.querySelector('#cbLibraryRoot').value.trim();
                config.PreferredQuality = view.querySelector('#cbPreferredQuality').value.trim();
                config.ProxyBaseUrl = view.querySelector('#cbProxyBaseUrl').value.trim();
                config.WriteNfo = view.querySelector('#cbWriteNfo').checked;
                config.DownloadImages = view.querySelector('#cbDownloadImages').checked;
                config.CleanupRemoved = view.querySelector('#cbCleanupRemoved').checked;
                config.UpdatePrimaryUrl = view.querySelector('#cbUpdatePrimaryUrl').value.trim();
                config.Sources = collectSources(view);
                return Promise.all([
                    ApiClient.updatePluginConfiguration(pluginId, config),
                    saveSchedule(view)
                ]);
            }).then(function () {
                status(view, '已保存');
                refreshSourceSelect(view);
            }, function (err) {
                status(view, '保存失败：' + ((err && err.message) || '请检查网络与服务端日志'));
            });
            return false;
        });

        view.querySelector('#cbAddSource').addEventListener('click', function () {
            view.querySelector('#cbSources').appendChild(sourceRow(view, {
                Name: '', ApiUrl: '', TypeId: 0, MaxItems: 20, Enabled: true
            }));
        });

        view.querySelector('#cbSyncNow').addEventListener('click', function () {
            status(view, '同步中，请稍候…');
            apiPost('ContentBridge/sync').then(function (res) {
                var errors = (res && res.Errors) || [];
                if (res && res.Failed > 0) {
                    status(view, '同步结束：成功 ' + res.Succeeded + ' 个数据源，失败 ' + res.Failed + ' 个——'
                        + errors.join('；'));
                } else if (errors.length) {
                    status(view, '同步结束：' + errors.join('；'));
                } else {
                    status(view, '同步完成：成功 ' + ((res && res.Succeeded) || 0) + ' 个数据源');
                }
            }, function (err) {
                status(view, '同步失败：' + ((err && err.message) || '请求出错，详见服务端日志'));
            });
        });

        view.querySelector('#cbCheckUpdate').addEventListener('click', function () {
            var out = view.querySelector('#cbUpdateResult');
            out.textContent = '检查中…';
            apiGet('ContentBridge/update-check', { primary: view.querySelector('#cbUpdatePrimaryUrl').value.trim() }).then(function (res) {
                if (!res || !res.Success) {
                    out.textContent = '检查失败：' + ((res && res.Error) || '两个渠道都不可用');
                    return;
                }
                out.textContent = '当前 ' + (res.CurrentVersion || '?') + ' · 最新 ' + (res.LatestVersion || '?')
                    + ' · ' + (res.Degraded ? '备用渠道（主渠道不可用，已自动降级）' : (res.Channel === 'primary' ? '主渠道' : 'GitHub 渠道'));
                if (res.UpdateAvailable) {
                    var line = document.createElement('div');
                    line.textContent = '有新版本，下载地址：';
                    var a = document.createElement('a');
                    a.textContent = res.DownloadUrl || '(清单未提供下载地址)';
                    if (/^https?:\/\//i.test(res.DownloadUrl || '')) {
                        a.href = res.DownloadUrl;
                        a.target = '_blank';
                        a.rel = 'noopener';
                    }
                    line.appendChild(a);
                    out.appendChild(line);
                } else {
                    out.textContent += ' · 已是最新';
                }
            }, function () {
                out.textContent = '检查失败，网络不可达';
            });
        });

        view.querySelector('#cbSearchBtn').addEventListener('click', function () {
            var source = view.querySelector('#cbSearchSource').value;
            var keyword = view.querySelector('#cbSearchKeyword').value.trim();
            var results = view.querySelector('#cbSearchResults');
            results.innerHTML = '';
            if (!source || !keyword) {
                status(view, '请选择数据源并输入关键词');
                return;
            }
            status(view, '搜索中…');
            apiGet('ContentBridge/search', { source: source, keyword: keyword }).then(function (list) {
                status(view, '');
                (list || []).slice(0, 30).forEach(function (item) {
                    var row = document.createElement('div');
                    row.className = 'cbSearchRow';
                    var label = document.createElement('span');
                    label.style.flex = '1';
                    label.textContent = (item.Name || '') + (item.Remarks ? ' · ' + item.Remarks : '') + (item.Year ? ' (' + item.Year + ')' : '');
                    var btn = document.createElement('button', { is: 'emby-button' });
                    btn.type = 'button';
                    btn.className = 'raised';
                    btn.innerHTML = '<span>入库</span>';
                    btn.addEventListener('click', function () {
                        status(view, '入库中…');
                        apiPost('ContentBridge/add', { Source: source, VodId: item.Id }).then(function (res) {
                            status(view, '已入库：' + ((res && res.Files && res.Files.length) || 0) + ' 个文件，稍后自动刮削');
                        }, function (err) {
                            status(view, '入库失败：' + ((err && err.message) || '请检查数据源与网络'));
                        });
                    });
                    row.appendChild(label);
                    row.appendChild(btn);
                    results.appendChild(row);
                });
                if (!results.childNodes.length) {
                    results.textContent = '无结果';
                }
            }, function () {
                status(view, '搜索失败，检查接口地址与网络');
            });
        });

        loadConfig(view);
    }

    function createViewClass() {
        function View(view) {
            if (!view || view.__cbInited) {
                return;
            }
            view.__cbInited = true;
            try {
                bind(view);
            } catch (e) {
                status(view, '页面初始化失败：' + ((e && e.message) || e));
            }
        }

        // Lifecycle stubs: 4.8 calls these unconditionally on navigation when present;
        // 4.10 auto-wraps bare controllers with BaseView only when BOTH are missing, so
        // defining them here keeps behavior identical on every web build.
        View.prototype.onResume = function () { };
        View.prototype.onPause = function () { };

        return View;
    }

    if (typeof define === 'function' && define.amd) {
        // Channel A: loaded as the page's data-controller AMD module.
        define([], createViewClass);
    } else {
        // Channel B: loaded as a plain script by the inline bootstrap.
        window.__cbCore = {
            init: function (root) {
                new (createViewClass())(root);
            }
        };
    }
})();
