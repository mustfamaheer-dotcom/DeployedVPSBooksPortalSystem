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
    
    init: function(pdfUrl, storageKey) {
        this.storageKey = storageKey;
        this.canvas = document.getElementById('pdf-canvas');
        this.ctx = this.canvas.getContext('2d');
        
        this.annotCanvas = document.getElementById('annotation-canvas');
        this.annotCtx = this.annotCanvas.getContext('2d');
        
        this.loadAnnotations();
        this.setupEventListeners();
        
        // Asynchronous download of PDF with credentials (to send cookies for [Authorize] endpoint)
        pdfjsLib.getDocument({ url: pdfUrl, withCredentials: true }).promise.then(function(pdfDoc_) {
            window.studentAnnotations.pdfDoc = pdfDoc_;
            document.getElementById('page-count').textContent = window.studentAnnotations.pdfDoc.numPages;
            
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
                
                // Draw annotations for this page
                window.studentAnnotations.redrawAnnotations();
                
                if (window.studentAnnotations.pageNumPending !== null) {
                    window.studentAnnotations.renderPage(window.studentAnnotations.pageNumPending);
                    window.studentAnnotations.pageNumPending = null;
                }
            });
        });
        
        document.getElementById('page-num').textContent = num;
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
    },
    
    nextPage: function() {
        if (this.pageNum >= this.pdfDoc.numPages) return;
        this.pageNum++;
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
        
        this.annotCanvas.addEventListener('mousedown', function(e) {
            if (!self.currentTool) return;
            
            var pos = self.getPointerPos(e);
            
            if (self.currentTool === 'text') {
                var text = prompt("Enter note text:");
                if (text) {
                    self.addAnnotation({
                        type: 'text',
                        text: text,
                        x: pos.x,
                        y: pos.y,
                        color: '#ef4444' // red notes
                    });
                    self.redrawAnnotations();
                    self.saveAnnotations();
                }
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
                self.saveAnnotations();
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
            this.saveAnnotations();
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
    
    loadAnnotations: function() {
        if (!this.storageKey) return;
        var saved = localStorage.getItem(this.storageKey);
        if (saved) {
            try {
                this.annotations = JSON.parse(saved);
            } catch (e) {
                console.error("Could not parse annotations", e);
                this.annotations = {};
            }
        }
    },
    
    saveAnnotations: function() {
        if (!this.storageKey) return;
        localStorage.setItem(this.storageKey, JSON.stringify(this.annotations));
    },
    
    clearAll: function() {
        if (confirm("Are you sure you want to clear all annotations from all pages of this book? This cannot be undone.")) {
            this.annotations = {};
            this.saveAnnotations();
            this.redrawAnnotations();
        }
    }
};
