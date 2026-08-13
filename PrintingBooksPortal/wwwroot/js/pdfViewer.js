(function () {
    'use strict';

    document.addEventListener('dragstart', function (e) {
        if (e.target && typeof e.target.closest === 'function' && e.target.closest('.pdf-container')) e.preventDefault();
    });
    document.addEventListener('selectstart', function (e) {
        if (e.target && typeof e.target.closest === 'function' && e.target.closest('.pdf-container')) e.preventDefault();
    });

    var pdfDoc = null;
    var container = null;
    var loadingEl = null;
    var renderedPages = {};
    var pendingRender = {};
    var observer = null;
    var currentScale = 1.5;
    var outsideClickHandler = null;

    // Print settings state
    var settings = {
        printerName: '',
        paperSize: 'A4',
        duplex: 'off',
        scalingMode: 'actual',
        customScale: 100,
        marginPreset: 'narrow',
        marginUnit: 'mm',
        marginTop: 12.7,
        marginBottom: 12.7,
        marginLeft: 12.7,
        marginRight: 12.7,
        customPaperWidth: null,
        customPaperHeight: null,
        customPaperUnit: 'in'
    };

    // Display settings
    var display = {
        zoom: 100,
        showPageStyle: 'single'
    };

    window.initPdfViewer = function (bookId) {
        container = document.getElementById('pdfViewer');
        loadingEl = document.getElementById('pdfLoading');
        if (!container) return;

        wireCopiesControls();
        wireSettingsWidget();

        loadPdfJs(function () {
            loadSecurePdf(bookId);
        });
    };

    function wireCopiesControls() {
        var input = document.getElementById('copiesInput');
        var inc = document.getElementById('copiesInc');
        var dec = document.getElementById('copiesDec');
        if (!input || !inc || !dec) return;

        inc.addEventListener('click', function () {
            var v = parseInt(input.value, 10) || 1;
            if (v < 100) input.value = v + 1;
        });
        dec.addEventListener('click', function () {
            var v = parseInt(input.value, 10) || 1;
            if (v > 1) input.value = v - 1;
        });
        input.addEventListener('change', function () {
            var v = parseInt(input.value, 10) || 1;
            if (v < 1) v = 1;
            if (v > 100) v = 100;
            input.value = v;
        });
    }

    function wireSettingsWidget() {
        // ─── Toggle ───
        var toggle = document.getElementById('settingsToggle');
        var panel = document.getElementById('settingsPanel');
        if (toggle && panel) {
            toggle.addEventListener('click', function (e) {
                e.stopPropagation();
                panel.classList.toggle('open');
            });
            if (outsideClickHandler) document.removeEventListener('click', outsideClickHandler);
            outsideClickHandler = function (e) {
                // The settings group may no longer be in the DOM (e.g. after a
                // print job is accepted and the page re-renders) — guard against null.
                var group = document.getElementById('printSettingsGroup');
                if (!group || !group.contains(e.target)) {
                    panel.classList.remove('open');
                }
            };
            document.addEventListener('click', outsideClickHandler);
        }

        // ─── Printer ───
        wirePrinterControls();

        // ─── Paper Size ───
        wirePaperSizeControls();

        // ─── Duplex ───
        wireDuplexControls();

        // ─── Pages ───
        wirePagesControls();

        // ─── Scaling ───
        wireScalingControls();

        // ─── Margins ───
        wireMarginControls();

        // ─── Display Zoom ───
        wireZoomControls();

        // Initial printer detect + auto-refresh every 10 seconds
        detectPrinters();
        setInterval(function () { detectPrinters(); }, 10000);
    }

    // ══════════════════════════════════════
    //  PAPER SIZE CONTROLS
    // ══════════════════════════════════════

    function wirePaperSizeControls() {
        var paperSizeSelect = document.getElementById('paperSizeSelect');
        var customGrid = document.getElementById('customPaperGrid');

        if (paperSizeSelect) {
            paperSizeSelect.addEventListener('change', function () {
                settings.paperSize = this.value;
                if (customGrid) {
                    customGrid.style.display = this.value === 'custom' ? 'grid' : 'none';
                }
            });
        }

        // Custom paper size inputs
        var widthInput = document.getElementById('paperWidth');
        var heightInput = document.getElementById('paperHeight');
        var unitSelect = document.getElementById('paperUnit');

        if (widthInput) {
            widthInput.addEventListener('change', function () {
                settings.customPaperWidth = parseFloat(this.value) || 8.5;
            });
        }
        if (heightInput) {
            heightInput.addEventListener('change', function () {
                settings.customPaperHeight = parseFloat(this.value) || 11;
            });
        }
        if (unitSelect) {
            unitSelect.addEventListener('change', function () {
                settings.customPaperUnit = this.value;
            });
        }
    }

    // ══════════════════════════════════════
    //  DUPLEX CONTROLS
    // ══════════════════════════════════════

    function wireDuplexControls() {
        var radios = document.querySelectorAll('input[name="duplex"]');
        radios.forEach(function (radio) {
            radio.addEventListener('change', function () {
                settings.duplex = this.value;
            });
        });
    }

    // ══════════════════════════════════════
    //  PAGES CONTROLS
    // ══════════════════════════════════════

    var pageTotal = 0;

    function wirePagesControls() {
        var input = document.getElementById('pageRangeInput');
        if (!input) return;

        document.querySelectorAll('input[name="pageRange"]').forEach(function (radio) {
            radio.addEventListener('change', function () {
                input.disabled = radio.value !== 'custom';
                if (radio.value === 'all') input.value = '';
                getPagesSelection();
            });
        });

        input.addEventListener('input', function () {
            getPagesSelection();
        });

        input.addEventListener('keydown', function (e) {
            e.stopPropagation();
        });

        getPagesSelection();
    }

    function setPageTotal(total) {
        pageTotal = total;
        getPagesSelection();
    }

    function setPagesError(message) {
        var errorEl = document.getElementById('pageRangeError');
        var input = document.getElementById('pageRangeInput');
        var summaryEl = document.getElementById('pagesSummary');
        if (errorEl) errorEl.textContent = message;
        if (input) input.classList.add('invalid');
        if (summaryEl) summaryEl.textContent = '';
    }

    function clearPagesError() {
        var errorEl = document.getElementById('pageRangeError');
        var input = document.getElementById('pageRangeInput');
        if (errorEl) errorEl.textContent = '';
        if (input) input.classList.remove('invalid');
    }

    function parsePageSelection(raw, total) {
        var tokens = raw.split(',');
        var seen = {};

        for (var i = 0; i < tokens.length; i++) {
            var t = tokens[i].trim();
            if (!/^\d+(-\d+)?$/.test(t)) {
                return { valid: false, error: "Invalid page selection: '" + t + "'. Use numbers and ranges, e.g. 1-5, 8, 11-13." };
            }
            var parts = t.split('-');
            var s = parseInt(parts[0], 10);
            var e = parts.length === 2 ? parseInt(parts[1], 10) : s;
            if (!(s >= 1 && e >= 1)) {
                return { valid: false, error: 'Page numbers must be 1 or greater.' };
            }
            if (s > e) {
                return { valid: false, error: "Invalid range '" + t + "': start must not be greater than end." };
            }
            if (total > 0 && e > total) {
                return { valid: false, error: 'Page ' + e + ' is out of range: this book has ' + total + ' pages.' };
            }
            for (var n = s; n <= e; n++) seen[n] = true;
        }

        var sorted = Object.keys(seen).map(Number).sort(function (a, b) { return a - b; });
        if (sorted.length === 0) {
            return { valid: false, error: 'Please enter at least one page to print.' };
        }

        var canonical = [];
        var count = 0;
        var rs = sorted[0];
        var re = sorted[0];
        for (var k = 1; k < sorted.length; k++) {
            if (sorted[k] === re + 1) {
                re = sorted[k];
                continue;
            }
            canonical.push(rs === re ? String(rs) : rs + '-' + re);
            count += re - rs + 1;
            rs = sorted[k];
            re = sorted[k];
        }
        canonical.push(rs === re ? String(rs) : rs + '-' + re);
        count += re - rs + 1;

        return { valid: true, canonical: canonical.join(', '), count: count };
    }

    // Returns { valid, value } — value is '' for "All pages" (server default),
    // otherwise the raw selection string as typed (server re-parses and canonicalizes).
    function getPagesSelection() {
        var radio = document.querySelector('input[name="pageRange"]:checked');
        var input = document.getElementById('pageRangeInput');
        var summaryEl = document.getElementById('pagesSummary');
        if (!radio || !input) return { valid: true, value: '' };

        if (radio.value !== 'custom') {
            clearPagesError();
            if (summaryEl) {
                summaryEl.textContent = pageTotal > 0 ? 'Printing all ' + pageTotal + ' pages.' : '';
            }
            return { valid: true, value: '' };
        }

        var raw = (input.value || '').trim();
        if (!raw) {
            var emptyError = 'Enter the pages to print, e.g. 1-5, 8.';
            setPagesError(emptyError);
            return { valid: false, value: raw, error: emptyError };
        }

        var parsed = parsePageSelection(raw, pageTotal);
        if (!parsed.valid) {
            setPagesError(parsed.error);
            return { valid: false, value: raw, error: parsed.error };
        }

        clearPagesError();
        if (summaryEl) {
            summaryEl.textContent = pageTotal > 0
                ? 'Printing ' + parsed.count + ' of ' + pageTotal + ' pages (' + parsed.canonical + ').'
                : 'Printing ' + parsed.count + ' pages (' + parsed.canonical + ').';
        }
        return { valid: true, value: raw };
    }

    // ══════════════════════════════════════
    //  PRINTER CONTROLS
    // ══════════════════════════════════════

    function wirePrinterControls() {
        var refreshBtn = document.getElementById('printerRefresh');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                var svg = this.querySelector('svg');
                if (svg) svg.classList.add('spinning');
                detectPrinters(function () {
                    if (svg) svg.classList.remove('spinning');
                });
            });
        }

        var select = document.getElementById('printerSelect');
        if (select) {
            select.addEventListener('change', function () {
                settings.printerName = this.value;
            });
        }
    }

    var lastKnownPrinters = [];

    function detectPrinters(callback) {
        callback = callback || function () { };
        var select = document.getElementById('printerSelect');
        var statusDot = document.getElementById('printerStatusDot');
        var statusText = document.getElementById('printerStatusText');
        if (!select) { callback(); return; }

        function setOffline(msg) {
            var hasKnown = lastKnownPrinters.length > 0;
            // Keep the last known printer list visible when the agent simply went
            // away for a moment — clearing it is what made printers "disappear".
            if (!hasKnown) {
                select.innerHTML = '<option value="">Select a printer…</option>';
            }
            if (statusDot) statusDot.className = 'printer-status-dot printer-status-offline';
            if (statusText) {
                statusText.textContent = msg || 'Agent not running';
                statusText.className = 'printer-status-text offline';
            }
            callback();
        }

        function timeAgo(iso) {
            if (!iso) return 'unknown time';
            var then = new Date(iso).getTime();
            if (isNaN(then)) return 'unknown time';
            var secs = Math.floor((Date.now() - then) / 1000);
            if (secs < 5) return 'just now';
            if (secs < 60) return secs + 's ago';
            return Math.floor(secs / 60) + 'm ' + (secs % 60) + 's ago';
        }

        function onPrintersFetched(printers, connected, stale, lastSeen) {
            lastKnownPrinters = printers;

            select.innerHTML = '<option value="">Select a printer…</option>';

            printers.forEach(function (p) {
                if (!p.name) return;
                var isOnline = p.isOnline !== false;

                var opt = document.createElement('option');
                opt.value = p.name;
                var badge = '';
                switch ((p.connectionType || '').toLowerCase()) {
                    case 'network': badge = '\uD83C\uDF10'; break;
                    case 'usb': badge = '\uD83D\uDD0C'; break;
                    case 'bluetooth': badge = '\uD83D\uDCF6'; break;
                    case 'wifi': badge = '\uD83D\uDCE1'; break;
                    default: badge = '\uD83D\uDDA8\uFE0F'; break;
                }
                opt.textContent = p.name + '  ' + badge + ' ' + (p.connectionType || 'Local') + (isOnline ? '' : '  (offline)');
                if (!isOnline) opt.disabled = true; // cannot select an offline printer
                select.appendChild(opt);
            });

            if (statusDot) {
                statusDot.className = connected
                    ? 'printer-status-dot printer-status-online'
                    : 'printer-status-dot printer-status-stale';
            }
            if (statusText) {
                var n = printers.length;
                var countText = n + ' printer' + (n !== 1 ? 's' : '');
                if (connected) {
                    statusText.textContent = 'Online \u2014 ' + countText + ' found';
                    statusText.className = 'printer-status-text online';
                } else if (stale) {
                    statusText.textContent = 'Agent stale \u2014 last seen ' + timeAgo(lastSeen) + ' (showing last known printer list)';
                    statusText.className = 'printer-status-text stale';
                } else {
                    statusText.textContent = 'Agent offline \u2014 last seen ' + timeAgo(lastSeen) + ' (showing last known printer list)';
                    statusText.className = 'printer-status-text offline';
                }
            }
            callback();
        }

        // Single health check via server (avoids Chrome blocking localhost from HTTP pages)
        fetch('/api/pdf/print-agent/status', { cache: 'no-cache' }).then(function (r) {
            if (!r.ok) throw new Error();
            return r.json();
        }).then(function (data) {
            var printers = data.printers || [];
            var connected = !!data.connected;
            var stale = !!data.stale;

            if (printers.length === 0) {
                if (connected) {
                    setOffline('Agent detected \u2014 printers endpoint missing (old version)');
                } else if (lastKnownPrinters.length > 0) {
                    onPrintersFetched(lastKnownPrinters, false, false, data.lastSeen);
                } else {
                    setOffline('Agent not running \u2014 double-click the desktop shortcut');
                }
                return;
            }

            onPrintersFetched(printers, connected, stale, data.lastSeen);
        }).catch(function () {
            if (lastKnownPrinters.length > 0) {
                onPrintersFetched(lastKnownPrinters, false, false, null);
            } else {
                setOffline('Agent not running \u2014 double-click the desktop shortcut');
            }
        });
    }

    // ══════════════════════════════════════
    //  SCALING CONTROLS
    // ══════════════════════════════════════

    function wireScalingControls() {
        var radios = document.querySelectorAll('input[name="scaling"]');
        var customRow = document.getElementById('customScaleRow');
        var slider = document.getElementById('customScaleSlider');
        var input = document.getElementById('customScaleInput');

        function toggleCustom(show) {
            if (customRow) customRow.style.display = show ? 'flex' : 'none';
        }

        radios.forEach(function (radio) {
            radio.addEventListener('change', function () {
                settings.scalingMode = this.value;
                toggleCustom(this.value === 'custom');
                if (this.value !== 'custom' && slider && input) {
                    settings.customScale = 100;
                    slider.value = 100;
                    if (input) input.value = 100;
                }
            });
        });

        if (slider && input) {
            slider.addEventListener('input', function () {
                var v = parseInt(this.value, 10);
                input.value = v;
                settings.customScale = v;
            });
            input.addEventListener('change', function () {
                var v = parseInt(this.value, 10) || 100;
                if (v < 10) v = 10;
                if (v > 200) v = 200;
                this.value = v;
                slider.value = v;
                settings.customScale = v;
            });
        }

        toggleCustom(false);
    }

    // ══════════════════════════════════════
    //  MARGIN CONTROLS
    // ══════════════════════════════════════

    function wireMarginControls() {
        var marginBtns = document.querySelectorAll('.margin-btn');
        var customGrid = document.getElementById('customMarginGrid');

        function updateMarginPreset(preset) {
            settings.marginPreset = preset;
            switch (preset) {
                case 'normal':
                    settings.marginTop = 25.4;
                    settings.marginBottom = 25.4;
                    settings.marginLeft = 25.4;
                    settings.marginRight = 25.4;
                    break;
                case 'narrow':
                    settings.marginTop = 12.7;
                    settings.marginBottom = 12.7;
                    settings.marginLeft = 12.7;
                    settings.marginRight = 12.7;
                    break;
                case 'moderate':
                    settings.marginTop = 25.4;
                    settings.marginBottom = 25.4;
                    settings.marginLeft = 19.05;
                    settings.marginRight = 19.05;
                    break;
                case 'wide':
                    settings.marginTop = 25.4;
                    settings.marginBottom = 25.4;
                    settings.marginLeft = 50.8;
                    settings.marginRight = 50.8;
                    break;
                case 'mirrored':
                    settings.marginTop = 25.4;
                    settings.marginBottom = 25.4;
                    settings.marginLeft = 31.75;
                    settings.marginRight = 25.4;
                    break;
                case 'office2003':
                    settings.marginTop = 25.4;
                    settings.marginBottom = 25.4;
                    settings.marginLeft = 31.75;
                    settings.marginRight = 31.75;
                    break;
                case 'custom':
                    break;
            }
            if (preset !== 'custom') {
                document.getElementById('marginTop').value = settings.marginTop;
                document.getElementById('marginBottom').value = settings.marginBottom;
                document.getElementById('marginLeft').value = settings.marginLeft;
                document.getElementById('marginRight').value = settings.marginRight;
            }
            if (customGrid) customGrid.style.display = preset === 'custom' ? 'grid' : 'none';
            updateCanvasMargins();
        }

        marginBtns.forEach(function (btn) {
            btn.addEventListener('click', function () {
                marginBtns.forEach(function (b) { b.classList.remove('active'); });
                this.classList.add('active');
                updateMarginPreset(this.dataset.margin);
            });
        });

        // Custom margin inputs
        var marginIds = ['marginTop', 'marginBottom', 'marginLeft', 'marginRight'];
        marginIds.forEach(function (id) {
            var el = document.getElementById(id);
            if (el) {
                el.addEventListener('change', function () {
                    settings[id] = parseFloat(this.value) || 0;
                    if (settings.marginPreset !== 'custom') {
                        settings.marginPreset = 'custom';
                        marginBtns.forEach(function (b) { b.classList.remove('active'); });
                        document.querySelector('.margin-btn[data-margin="custom"]').classList.add('active');
                        if (customGrid) customGrid.style.display = 'grid';
                    }
                    updateCanvasMargins();
                });
            }
        });

        // Unit toggle
        var unitSelect = document.getElementById('marginUnitSelect');
        if (unitSelect) {
            unitSelect.addEventListener('change', function () {
                settings.marginUnit = this.value;
            });
        }

        // Start with Narrow preset
        updateMarginPreset('narrow');
    }

    // ══════════════════════════════════════
    //  ZOOM / DISPLAY CONTROLS
    // ══════════════════════════════════════

    function wireZoomControls() {
        var zoomSlider = document.getElementById('zoomSlider');
        var zoomValue = document.getElementById('zoomValue');
        if (zoomSlider && zoomValue) {
            zoomSlider.addEventListener('input', function () {
                var pct = parseInt(this.value, 10);
                zoomValue.textContent = pct + '%';
                display.zoom = pct;
                currentScale = (pct / 100) * 1.5;
                reRenderAllPages();
            });
        }
    }

    // ══════════════════════════════════════
    //  PDF LOADING & RENDERING
    // ══════════════════════════════════════

    function loadPdfJs(callback) {
        if (window.pdfjsLib) {
            callback();
            return;
        }
        var script = document.createElement('script');
        script.src = '/js/pdf.min.js';
        script.onload = function () {
            pdfjsLib.GlobalWorkerOptions.workerSrc = '/js/pdf.worker.min.js';
            if (pdfjsLib.VerbosityLevel) pdfjsLib.verbosity = pdfjsLib.VerbosityLevel.ERRORS;
            callback();
        };
        script.onerror = function () {
            if (loadingEl) loadingEl.innerHTML = '<span style="color:red">Failed to load PDF viewer. Please refresh the page.</span>';
        };
        document.head.appendChild(script);
    }

    async function loadSecurePdf(bookId) {
        loadingEl.style.display = 'flex';

        try {
            var response = await fetch('/api/pdf/view-secure/' + bookId, {
                method: 'GET',
                credentials: 'include'
            });

            if (response.status === 401 || response.status === 403) {
                throw new Error('Access Denied: You are not authorized to view this book.');
            }

            if (!response.ok) {
                throw new Error('HTTP Error: ' + response.status);
            }

            var data = await response.json();

            var binaryString = atob(data.pdfData);
            var len = binaryString.length;
            var bytes = new Uint8Array(len);
            for (var i = 0; i < len; i++) {
                bytes[i] = binaryString.charCodeAt(i);
            }

            var loadingTask = pdfjsLib.getDocument({ data: bytes });

            loadingTask.onProgress = function (progress) {
                var pct = Math.min(Math.round((progress.loaded / progress.total) * 100), 100);
                var text = loadingEl.querySelector('.pdf-loading-text');
                var bar = loadingEl.querySelector('.pdf-loading-bar span');
                if (text) text.textContent = 'Loading... ' + pct + '%';
                if (bar) bar.style.width = pct + '%';
            };

            pdfDoc = await loadingTask.promise;
            setPageTotal(pdfDoc.numPages);

            loadingEl.style.display = 'none';
            container.innerHTML = '';
            container.style.textAlign = 'center';

            for (var i = 1; i <= pdfDoc.numPages; i++) {
                var placeholder = document.createElement('div');
                placeholder.className = 'pdf-page-placeholder';
                placeholder.dataset.pageNum = i;
                placeholder.style.height = '10px';
                container.appendChild(placeholder);
            }

            observer = new IntersectionObserver(onPageVisible, {
                root: container.parentElement || container,
                rootMargin: '200px 0px',
                threshold: 0.01
            });

            document.querySelectorAll('.pdf-page-placeholder').forEach(function (el) {
                observer.observe(el);
            });

        } catch (error) {
            console.error(error);
            if (loadingEl) loadingEl.innerText = error.message;
        }
    }

    function onPageVisible(entries) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;
            var placeholder = entry.target;
            var pageNum = parseInt(placeholder.dataset.pageNum, 10);

            observer.unobserve(placeholder);

            if (renderedPages[pageNum]) return;
            if (pendingRender[pageNum]) return;
            pendingRender[pageNum] = true;

            renderPageAsync(pageNum, placeholder);
        });
    }

    function renderPageAsync(pageNum, placeholder) {
        pdfDoc.getPage(pageNum).then(function (page) {
            var viewport = page.getViewport({ scale: currentScale });

            var canvas = document.createElement('canvas');
            canvas.className = 'pdf-page-canvas';
            canvas.height = viewport.height;
            canvas.width = viewport.width;
            canvas.dataset.pageNum = pageNum;

            var m = getMarginPixels();
            canvas.style.margin = m.top + 'px ' + m.right + 'px ' + m.bottom + 'px ' + m.left + 'px';

            placeholder.parentNode.replaceChild(canvas, placeholder);

            var ctx = canvas.getContext('2d');
            return page.render({ canvasContext: ctx, viewport: viewport }).promise;
        }).then(function () {
            renderedPages[pageNum] = true;
            delete pendingRender[pageNum];
        }).catch(function (err) {
            delete pendingRender[pageNum];
        });
    }

    function reRenderAllPages() {
        renderedPages = {};
        pendingRender = {};
        if (observer) observer.disconnect();
        container.innerHTML = '';
        container.style.textAlign = 'center';

        for (var i = 1; i <= pdfDoc.numPages; i++) {
            var placeholder = document.createElement('div');
            placeholder.className = 'pdf-page-placeholder';
            placeholder.dataset.pageNum = i;
            placeholder.style.height = '10px';
            container.appendChild(placeholder);
        }

        observer = new IntersectionObserver(onPageVisible, {
            root: container.parentElement || container,
            rootMargin: '200px 0px',
            threshold: 0.01
        });

        document.querySelectorAll('.pdf-page-placeholder').forEach(function (el) {
            observer.observe(el);
        });
    }

    function updateCanvasMargins() {
        var m = getMarginPixels();
        document.querySelectorAll('.pdf-page-canvas').forEach(function (canvas) {
            canvas.style.margin = m.top + 'px ' + m.right + 'px ' + m.bottom + 'px ' + m.left + 'px';
        });
    }

    function getMarginPixels() {
        var mmToPx = 3.7795275591;
        var inToPx = 96;
        var factor = settings.marginUnit === 'in' ? inToPx : mmToPx;
        var top = (settings.marginTop || 0) * factor;
        var bottom = (settings.marginBottom || 0) * factor;
        var left = (settings.marginLeft || 0) * factor;
        var right = (settings.marginRight || 0) * factor;
        return { top: top, bottom: bottom, left: left, right: right };
    }

    // ══════════════════════════════════════
    //  PRINT FLOW
    // ══════════════════════════════════════

    function showPrintModal(success, message, reason) {
        var progress = document.getElementById('printModalStateProgress');
        var successState = document.getElementById('printModalStateSuccess');
        var errorState = document.getElementById('printModalStateError');

        function show(state) {
            [progress, successState, errorState].forEach(function (el) {
                if (el) el.classList.add('d-none');
            });
            if (state) state.classList.remove('d-none');
        }

        if (success) {
            show(successState);
        } else {
            var reasonEl = document.getElementById('printErrorReason');
            if (reasonEl) reasonEl.innerHTML = (message || reason || 'Unknown error').replace(/\n/g, '<br>');
            show(errorState);
        }
    }

    function showAgentRequiredModal() {
        var modalEl = document.getElementById('printResultModal');
        if (modalEl) {
            var p = document.getElementById('printModalStateProgress');
            var s = document.getElementById('printModalStateSuccess');
            var e = document.getElementById('printModalStateError');
            if (p) p.classList.add('d-none');
            if (s) s.classList.add('d-none');
            if (e) e.classList.remove('d-none');
        }
        var reasonEl = document.getElementById('printErrorReason');
        if (reasonEl) reasonEl.innerHTML = 'The local printer agent is not running.';
        var helpList = document.getElementById('printHelpList');
        if (helpList) {
            helpList.innerHTML = '' +
                '<li>Double-click the <strong>DR Bahig Books Portal</strong> shortcut on your desktop to start the agent.</li>' +
                '<li>Wait a few seconds until the system tray icon appears and shows "Agent Running".</li>' +
                '<li>If you don\'t have the shortcut, run <strong>DR_Bahig_Books_Portal_Setup.exe</strong> to install the agent.</li>' +
                '<li>Once the agent is running, click <strong>Try Again</strong> below.</li>';
        }
        if (modalEl && window.bootstrap) {
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }
    }

    window.handlePrint = async function (event, bookId) {
        console.log('[Print] Starting print job for book', bookId);

        var modalEl = document.getElementById('printResultModal');
        if (modalEl) {
            var p = document.getElementById('printModalStateProgress');
            var s = document.getElementById('printModalStateSuccess');
            var e = document.getElementById('printModalStateError');
            if (p) p.classList.remove('d-none');
            if (s) s.classList.add('d-none');
            if (e) e.classList.add('d-none');
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }

        var printBtn = document.getElementById('printBtn');
        if (printBtn) printBtn.disabled = true;

        try {
            if (!pdfDoc) {
                throw new Error('The document is still loading. Please wait a moment and try again.');
            }

            var copiesInput = document.getElementById('copiesInput');
            var copies = copiesInput ? parseInt(copiesInput.value, 10) || 1 : 1;

            var printerSelect = document.getElementById('printerSelect');
            var printerName = printerSelect ? printerSelect.value : settings.printerName;

            if (!printerName) {
                throw new Error('Please select a printer from the list, then click Print again.');
            }

            var paperSizeSelect = document.getElementById('paperSizeSelect');
            var paperSize = paperSizeSelect ? paperSizeSelect.value : settings.paperSize;

            var duplexRadio = document.querySelector('input[name="duplex"]:checked');
            var duplex = duplexRadio ? duplexRadio.value : settings.duplex;

            var scalingRadio = document.querySelector('input[name="scaling"]:checked');
            var scalingMode = scalingRadio ? scalingRadio.value : settings.scalingMode;

            var customScaleInput = document.getElementById('customScaleInput');
            var customScale = customScaleInput ? parseInt(customScaleInput.value, 10) || 100 : settings.customScale;

            var marginTop = parseFloat(document.getElementById('marginTop').value) || settings.marginTop;
            var marginBottom = parseFloat(document.getElementById('marginBottom').value) || settings.marginBottom;
            var marginLeft = parseFloat(document.getElementById('marginLeft').value) || settings.marginLeft;
            var marginRight = parseFloat(document.getElementById('marginRight').value) || settings.marginRight;
            var marginUnit = document.getElementById('marginUnitSelect') ? document.getElementById('marginUnitSelect').value : settings.marginUnit;

            var pageSelection = getPagesSelection();
            if (!pageSelection.valid) {
                throw new Error(pageSelection.error || 'Invalid page selection.');
            }

            var payload = {
                bookId: bookId,
                copies: copies,
                pages: pageSelection.value || undefined,
                printerName: printerName || undefined,
                paperSize: paperSize,
                duplex: duplex,
                scalingMode: scalingMode,
                customScale: customScale,
                marginUnit: marginUnit,
                marginTop: marginTop,
                marginBottom: marginBottom,
                marginLeft: marginLeft,
                marginRight: marginRight
            };

            // If custom paper size, override paperSize with dimensions
            if (paperSize === 'custom') {
                var pw = parseFloat(document.getElementById('paperWidth').value) || 8.5;
                var ph = parseFloat(document.getElementById('paperHeight').value) || 11;
                var pu = document.getElementById('paperUnit') ? document.getElementById('paperUnit').value : 'in';
                payload.paperSize = 'custom';
                payload.customPaperWidth = pw;
                payload.customPaperHeight = ph;
                payload.customPaperUnit = pu;
            }

            console.log('[Print] Sending to server:', payload);

            var serverResponse = await fetch('/api/pdf/process-print', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify(payload)
            });

            console.log('[Print] Server response status:', serverResponse.status);

            var serverData = await serverResponse.json().catch(function () {
                return { success: false, error: 'HTTP ' + serverResponse.status };
            });

            console.log('[Print] Server data:', serverData);

            if (!serverResponse.ok || !serverData.success) {
                throw new Error(serverData.error || serverData.message || 'Server returned ' + serverResponse.status);
            }

            var jobId = serverData.jobId;
            var wasQueued = serverData.added === true;
            console.log('[Print] Job created:', jobId, '| wasQueued:', wasQueued, '| queueCount:', serverData.queueCount);

            if (!wasQueued) {
                throw new Error('The server could not add the job to the print queue.\n\nPlease refresh the page and try again.\nIf the problem persists, contact support.');
            }

            var agentClaimed = false;
            var jobSeenInQueue = false;
            for (var i = 0; i < 10; i++) {
                await new Promise(function (r) { setTimeout(r, 2000); });
                try {
                    var check = await fetch('/api/pdf/print-agent/pending', { credentials: 'include' });
                    var checkData = await check.json();
                    console.log('[Print] Poll attempt', (i + 1), '- pending jobs:', checkData.jobs);
                    if (checkData.jobs) {
                        if (checkData.jobs.includes(jobId)) {
                            jobSeenInQueue = true;
                            console.log('[Print] Job is still in queue, waiting for agent...');
                        } else {
                            if (jobSeenInQueue || i === 0) {
                                agentClaimed = true;
                                console.log('[Print] Agent claimed job!');
                                break;
                            }
                        }
                    }
                } catch (err) {
                    console.warn('[Print] Poll error:', err);
                }
            }

            if (agentClaimed) {
                showPrintModal(true, 'Sending to printer...');
            } else if (jobSeenInQueue) {
                showPrintModal(false, 'The agent did not claim the job within 20 seconds.\n\nMake sure the agent is running and the printer is online, then try again.');
            } else {
                showPrintModal(true, 'The print job was sent to the agent.\nCheck your printer for output.');
            }

        } catch (error) {
            console.error('[Print] Error:', error);
            showPrintModal(false, error.message, error.message);
        } finally {
            if (printBtn) printBtn.disabled = false;
        }
    };

    var retryBtn = document.getElementById('printRetryBtn');
    if (retryBtn) {
        retryBtn.addEventListener('click', function () {
            var modalEl = document.getElementById('printResultModal');
            if (modalEl) bootstrap.Modal.getOrCreateInstance(modalEl).hide();
        });
    }

    var modalEl = document.getElementById('printResultModal');
    if (modalEl) {
        modalEl.addEventListener('hidden.bs.modal', function () {
            var printBtn = document.getElementById('printBtn');
            if (printBtn) printBtn.focus({ preventScroll: false });
        });
    }
})();
