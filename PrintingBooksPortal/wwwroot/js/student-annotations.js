window.studentAnnotations = {
    pdfDoc: null,
    pageNum: 1,
    pageRendering: false,
    pageNumPending: null,
    scale: 1.5,
    canvas: null,
    ctx: null,
    annotCanvas: null,
    annotCtx: null,
    
    currentTool: null, // 'highlight-yellow', 'pen', 'text', 'eraser'
    isDrawing: false,
    
    storageKey: '',
    annotations: {}, // { "page_1": [ { type: 'path', color: '#...', points: [{x,y}, ...] } ] }
    
    currentPath: null,
    
    // Add touch tracking variables
    touchStartX: 0,
    touchEndX: 0,
    
    dotNetRef: null,
    
    init: function(pdfUrl, storageKey, lastPageRead, dotNetRef, serverAnnotationsJson) {
        this.storageKey = storageKey;
        this.dotNetRef = dotNetRef;
        if (lastPageRead && lastPageRead > 0) {
            this.pageNum = lastPageRead;
        }
        
        // Responsive scale based on screen width
        if (window.innerWidth <= 480) {
            this.scale = 0.8;
        } else if (window.innerWidth <= 768) {
            this.scale = 1.0;
        } else if (window.innerWidth <= 992) {
            this.scale = 1.2;
        } else {
            this.scale = 1.5;
        }
        this.canvas = document.getElementById('pdf-canvas');
        this.ctx = this.canvas.getContext('2d');
        
        this.annotCanvas = document.getElementById('annotation-canvas');
        this.annotCtx = this.annotCanvas.getContext('2d');
        
        this.loadAnnotations(serverAnnotationsJson);
        this.setupEventListeners();
        
        // Asynchronous download of PDF with credentials (to send cookies for [Authorize] endpoint)
        pdfjsLib.getDocument({ url: pdfUrl, withCredentials: true }).promise.then(function(pdfDoc_) {
            window.studentAnnotations.pdfDoc = pdfDoc_;
            
            var countTop = document.getElementById('page-count-top');
            if (countTop) countTop.textContent = window.studentAnnotations.pdfDoc.numPages;
            
            var countBottom = document.getElementById('page-count');
            if (countBottom) countBottom.textContent = window.studentAnnotations.pdfDoc.numPages;
            
            // Initial/first page rendering
            window.studentAnnotations.renderPage(window.studentAnnotations.pageNum);
        }).catch(function(error) {
            console.error("Error loading PDF: ", error);
            if (window.showToast) {
                window.showToast("Error loading the document. Your session may have expired or you lost access.", "error");
            } else {
                alert("Error loading the document. It may have been removed or you lost access.");
            }
        });
    },
    
    renderPage: function(num) {
        this.pageRendering = true;
        
        this.pdfDoc.getPage(num).then(function(page) {
            var viewport = page.getViewport({scale: window.studentAnnotations.scale});
            
            // Set dimensions for both canvases
            window.studentAnnotations.canvas.height = viewport.height;
            window.studentAnnotations.canvas.width = viewport.width;
            
            window.studentAnnotations.annotCanvas.height = viewport.height;
            window.studentAnnotations.annotCanvas.width = viewport.width;
            
            var renderContext = {
                canvasContext: window.studentAnnotations.ctx,
                viewport: viewport
            };
            
            var renderTask = page.render(renderContext);
            
            renderTask.promise.then(function() {
                window.studentAnnotations.pageRendering = false;
                
                // Hide loading spinner if it exists
                var spinner = document.getElementById('pdf-loading-spinner');
                if (spinner) spinner.style.display = 'none';
                
                // Draw annotations for this page
                window.studentAnnotations.redrawAnnotations();
                
                if (window.studentAnnotations.pageNumPending !== null) {
                    window.studentAnnotations.renderPage(window.studentAnnotations.pageNumPending);
                    window.studentAnnotations.pageNumPending = null;
                }
            });
        });
        
        var pNumTop = document.getElementById('page-num-top');
        if (pNumTop) pNumTop.textContent = num;
        
        var pNumBottom = document.getElementById('page-num');
        if (pNumBottom) pNumBottom.textContent = num;
    },
    
    queueRenderPage: function(num) {
        if (this.pageRendering) {
            this.pageNumPending = num;
        } else {
            this.renderPage(num);
        }
    },
    
    prevPage: function() {
        if (this.pageNum <= 1) return;
        this.pageNum--;
        this.queueRenderPage(this.pageNum);
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnPageChanged', this.pageNum);
        }
    },
    
    nextPage: function() {
        if (this.pageNum >= this.pdfDoc.numPages) return;
        this.pageNum++;
        this.queueRenderPage(this.pageNum);
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnPageChanged', this.pageNum);
        }
    },
    
    zoomIn: function() {
        if (this.scale >= 3.0) return;
        this.scale += 0.2;
        this.queueRenderPage(this.pageNum);
    },
    
    zoomOut: function() {
        if (this.scale <= 0.5) return;
        this.scale -= 0.2;
        this.queueRenderPage(this.pageNum);
    },
    
    setTool: function(tool) {
        this.currentTool = tool;
        if (tool === 'text') {
            this.annotCanvas.style.cursor = 'text';
        } else if (tool === 'eraser') {
            this.annotCanvas.style.cursor = 'cell';
        } else if (tool) {
            this.annotCanvas.style.cursor = 'crosshair';
        } else {
            this.annotCanvas.style.cursor = 'default';
        }
        
        // Update active class on toolbar buttons
        document.querySelectorAll('.tool-btn').forEach(btn => {
            btn.classList.remove('active');
            if (btn.getAttribute('onclick') && btn.getAttribute('onclick').includes("'" + tool + "'")) {
                btn.classList.add('active');
            }
        });
    },
    
    getPointerPos: function(e) {
        var rect = this.annotCanvas.getBoundingClientRect();
        return {
            x: e.clientX - rect.left,
            y: e.clientY - rect.top
        };
    },
    
    setupEventListeners: function() {
        var self = this;
        
        // Touch events for drawing & swiping
        this.annotCanvas.addEventListener('touchstart', function(e) {
            if (e.touches.length === 1) {
                self.touchStartX = e.touches[0].clientX;
            }
            if (self.currentTool) {
                e.preventDefault(); // Prevent scrolling while drawing
                var touch = e.touches[0];
                var mouseEvent = new MouseEvent("mousedown", {
                    clientX: touch.clientX,
                    clientY: touch.clientY
                });
                self.annotCanvas.dispatchEvent(mouseEvent);
            }
        }, { passive: false });
        
        this.annotCanvas.addEventListener('touchmove', function(e) {
            if (self.currentTool && self.isDrawing) {
                e.preventDefault();
                var touch = e.touches[0];
                var mouseEvent = new MouseEvent("mousemove", {
                    clientX: touch.clientX,
                    clientY: touch.clientY
                });
                self.annotCanvas.dispatchEvent(mouseEvent);
            }
        }, { passive: false });
        
        this.annotCanvas.addEventListener('touchend', function(e) {
            if (self.currentTool && self.isDrawing) {
                e.preventDefault();
                var mouseEvent = new MouseEvent("mouseup", {});
                self.annotCanvas.dispatchEvent(mouseEvent);
            } else if (!self.currentTool && e.changedTouches.length === 1) {
                // Handle swipe
                self.touchEndX = e.changedTouches[0].clientX;
                var swipeDist = self.touchStartX - self.touchEndX;
                if (swipeDist > 50) {
                    self.nextPage(); // Swipe left -> next
                } else if (swipeDist < -50) {
                    self.prevPage(); // Swipe right -> prev
                }
            }
        });

        this.annotCanvas.addEventListener('mousedown', function(e) {
            if (!self.currentTool) return;
            
            var pos = self.getPointerPos(e);
            
            if (self.currentTool === 'text') {
                var textModalEl = document.getElementById('textNoteModal');
                if (!textModalEl) return;
                
                var textModal = bootstrap.Modal.getInstance(textModalEl) || new bootstrap.Modal(textModalEl);
                var textInput = document.getElementById('textNoteInput');
                var saveBtn = document.getElementById('saveTextNoteBtn');
                
                // Remove old event listeners to avoid multiple fires
                var newSaveBtn = saveBtn.cloneNode(true);
                saveBtn.parentNode.replaceChild(newSaveBtn, saveBtn);
                saveBtn = newSaveBtn;
                
                textInput.value = '';
                textModal.show();
                
                // Focus input after modal is shown
                textModalEl.addEventListener('shown.bs.modal', function () {
                    textInput.focus();
                }, { once: true });
                
                var handleSave = function() {
                    var text = textInput.value.trim();
                    if (text) {
                        self.addAnnotation({
                            type: 'text',
                            text: text,
                            x: pos.x,
                            y: pos.y,
                            color: '#ef4444' // red notes
                        });
                        self.redrawAnnotations();
                        self.saveAnnotations(false); // background save
                    }
                    textModal.hide();
                };
                
                saveBtn.addEventListener('click', handleSave);
                
                // Handle Enter key
                textInput.onkeypress = function(e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        handleSave();
                    }
                };
                
                return;
            }
            
            if (self.currentTool === 'eraser') {
                self.eraseAt(pos);
                return;
            }
            
            self.isDrawing = true;
            
            var color = '#000000';
            var width = 2;
            var isHighlight = false;
            
            if (self.currentTool === 'highlight-yellow') { color = 'rgba(251, 191, 36, 0.4)'; width = 20; isHighlight = true; }
            if (self.currentTool === 'highlight-green') { color = 'rgba(52, 211, 153, 0.4)'; width = 20; isHighlight = true; }
            if (self.currentTool === 'highlight-pink') { color = 'rgba(244, 114, 182, 0.4)'; width = 20; isHighlight = true; }
            if (self.currentTool === 'pen') { color = '#3b82f6'; width = 3; }
            
            self.currentPath = {
                type: 'path',
                color: color,
                width: width,
                isHighlight: isHighlight,
                points: [pos]
            };
            
            self.annotCtx.beginPath();
            self.annotCtx.moveTo(pos.x, pos.y);
            self.annotCtx.strokeStyle = color;
            self.annotCtx.lineWidth = width;
            self.annotCtx.lineCap = 'round';
            self.annotCtx.lineJoin = 'round';
        });
        
        this.annotCanvas.addEventListener('mousemove', function(e) {
            if (!self.isDrawing || !self.currentPath) return;
            
            var pos = self.getPointerPos(e);
            self.currentPath.points.push(pos);
            
            self.annotCtx.lineTo(pos.x, pos.y);
            self.annotCtx.stroke();
        });
        
        this.annotCanvas.addEventListener('mouseup', function(e) {
            if (!self.isDrawing) return;
            self.isDrawing = false;
            
            if (self.currentPath && self.currentPath.points.length > 1) {
                self.addAnnotation(self.currentPath);
                self.saveAnnotations(false); // background save
            }
            self.currentPath = null;
        });
        
        this.annotCanvas.addEventListener('mouseleave', function(e) {
            if (self.isDrawing) {
                self.annotCanvas.dispatchEvent(new Event('mouseup'));
            }
        });
    },
    
    addAnnotation: function(ann) {
        var pageKey = 'page_' + this.pageNum;
        if (!this.annotations[pageKey]) this.annotations[pageKey] = [];
        this.annotations[pageKey].push(ann);
    },
    
    eraseAt: function(pos) {
        var pageKey = 'page_' + this.pageNum;
        var pageAnns = this.annotations[pageKey];
        if (!pageAnns || pageAnns.length === 0) return;
        
        // Simple hit detection: remove last annotation close to the click
        var erased = false;
        for (var i = pageAnns.length - 1; i >= 0; i--) {
            var ann = pageAnns[i];
            
            if (ann.type === 'text') {
                var dx = ann.x - pos.x;
                var dy = ann.y - pos.y;
                if (Math.sqrt(dx*dx + dy*dy) < 30) {
                    pageAnns.splice(i, 1);
                    erased = true;
                    break;
                }
            } else if (ann.type === 'path') {
                // Check points
                for (var j = 0; j < ann.points.length; j++) {
                    var p = ann.points[j];
                    var dx = p.x - pos.x;
                    var dy = p.y - pos.y;
                    if (Math.sqrt(dx*dx + dy*dy) < 20) {
                        pageAnns.splice(i, 1);
                        erased = true;
                        break;
                    }
                }
                if (erased) break;
            }
        }
        
        if (erased) {
            this.redrawAnnotations();
            this.saveAnnotations(false); // background save
        }
    },
    
    redrawAnnotations: function() {
        this.annotCtx.clearRect(0, 0, this.annotCanvas.width, this.annotCanvas.height);
        
        var pageKey = 'page_' + this.pageNum;
        var pageAnns = this.annotations[pageKey] || [];
        
        for (var i = 0; i < pageAnns.length; i++) {
            var ann = pageAnns[i];
            
            if (ann.type === 'path') {
                this.annotCtx.beginPath();
                this.annotCtx.moveTo(ann.points[0].x, ann.points[0].y);
                for (var j = 1; j < ann.points.length; j++) {
                    this.annotCtx.lineTo(ann.points[j].x, ann.points[j].y);
                }
                this.annotCtx.strokeStyle = ann.color;
                this.annotCtx.lineWidth = ann.width;
                this.annotCtx.lineCap = 'round';
                this.annotCtx.lineJoin = 'round';
                this.annotCtx.stroke();
            } else if (ann.type === 'text') {
                this.annotCtx.font = '16px Arial';
                this.annotCtx.fillStyle = ann.color;
                this.annotCtx.fillText(ann.text, ann.x, ann.y);
            }
        }
    },
    
    loadAnnotations: function(serverAnnotationsJson) {
        // Start with empty
        this.annotations = {};
        
        // Load from local storage
        if (this.storageKey) {
            var saved = localStorage.getItem(this.storageKey);
            if (saved) {
                try {
                    this.annotations = JSON.parse(saved);
                } catch (e) {
                    console.error("Could not parse local annotations", e);
                }
            }
        }
        
        // Merge with server annotations (server wins on conflict if we want, but simple replace for now since we sync JSON string)
        if (serverAnnotationsJson) {
            try {
                var serverData = JSON.parse(serverAnnotationsJson);
                if (serverData && Object.keys(serverData).length > 0) {
                    this.annotations = serverData;
                    // also update local storage to match server
                    if (this.storageKey) {
                        localStorage.setItem(this.storageKey, serverAnnotationsJson);
                    }
                }
            } catch (e) {
                console.error("Could not parse server annotations", e);
            }
        }
    },
    
    saveAnnotations: function(showSuccess = true) {
        var jsonData = JSON.stringify(this.annotations);
        if (this.storageKey) {
            localStorage.setItem(this.storageKey, jsonData);
        }
        
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('SyncAnnotations', jsonData).catch(err => {
                console.error("Error syncing annotations to server: ", err);
            });
        }
        
        // If called manually via the Save button, show feedback
        if (showSuccess === true) {
            if (window.showToast) {
                window.showToast("Annotations saved successfully", "success");
            } else {
                alert("Annotations saved successfully!");
            }
        }
    },
    
    clearAll: function() {
        if (confirm("Are you sure you want to clear all annotations from all pages of this book? This cannot be undone.")) {
            this.annotations = {};
            this.saveAnnotations(false);
            this.redrawAnnotations();
        }
    }
};
