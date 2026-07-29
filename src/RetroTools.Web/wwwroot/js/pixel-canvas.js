// Ο πυρήνας του editor.
//
// Γιατί ζει εδώ και όχι στον Blazor: ένα round-trip στον server ανά κίνηση ποντικιού
// θα έκανε τη σχεδίαση άχρηστη. Το JS κατέχει το buffer και ζωγραφίζει άμεσα και
// τοπικά· ο Blazor παραμένει η αυθεντική πηγή και μαθαίνει τις αλλαγές σε παρτίδες,
// στο τέλος κάθε πινελιάς.

const editors = new Map();

class PixelCanvas {
    constructor(canvas, dotNetRef, options) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d', { alpha: false });
        this.dotNet = dotNetRef;

        this.width = options.width;
        this.height = options.height;
        this.aspectWidth = options.aspectWidth || 1;
        this.aspectHeight = options.aspectHeight || 1;
        this.cellWidth = options.cellWidth || 0;
        this.cellHeight = options.cellHeight || 0;

        this.zoom = options.zoom || 12;
        this.tool = 'pencil';
        this.colorIndex = 1;
        this.showPixelGrid = true;
        this.showCellGrid = true;

        this.pixels = new Uint8Array(this.width * this.height);
        this.palette = options.palette || ['#000000'];

        // Χρησιμοποιείται από τα εργαλεία σχήματος: κρατά την εικόνα πριν την πινελιά
        // ώστε το preview να μπορεί να «σβήνεται» σε κάθε κίνηση.
        this.snapshot = null;
        this.strokeChanges = null;
        this.strokeStart = null;
        this.lastPoint = null;
        this.drawing = false;

        this.offscreen = document.createElement('canvas');
        this.offscreen.width = this.width;
        this.offscreen.height = this.height;
        this.offscreenCtx = this.offscreen.getContext('2d');
        this.imageData = this.offscreenCtx.createImageData(this.width, this.height);

        this.rgbaPalette = this.buildPalette();
        this.attachEvents();
        this.resize();
    }

    // --- Παλέτα ------------------------------------------------------------

    buildPalette() {
        return this.palette.map(hex => {
            const value = hex.startsWith('#') ? hex.slice(1) : hex;
            return [
                parseInt(value.slice(0, 2), 16),
                parseInt(value.slice(2, 4), 16),
                parseInt(value.slice(4, 6), 16)
            ];
        });
    }

    setPalette(palette) {
        this.palette = palette;
        this.rgbaPalette = this.buildPalette();
        this.render();
    }

    // --- Γεωμετρία ---------------------------------------------------------

    // Το πλάτος ενός pixel στην οθόνη διαφέρει από το ύψος του: ένα CPC Mode 0
    // pixel είναι διπλάσιου πλάτους. Χωρίς αυτό κάθε sprite φαίνεται παραμορφωμένο.
    get pixelWidth() {
        return this.zoom * this.aspectWidth;
    }

    get pixelHeight() {
        return this.zoom * this.aspectHeight;
    }

    resize() {
        const ratio = window.devicePixelRatio || 1;
        const displayWidth = this.width * this.pixelWidth;
        const displayHeight = this.height * this.pixelHeight;

        this.canvas.width = Math.round(displayWidth * ratio);
        this.canvas.height = Math.round(displayHeight * ratio);
        this.canvas.style.width = displayWidth + 'px';
        this.canvas.style.height = displayHeight + 'px';

        this.ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        this.ctx.imageSmoothingEnabled = false;

        this.render();
    }

    setZoom(zoom) {
        this.zoom = Math.max(1, Math.min(48, zoom));
        this.resize();
    }

    toPixel(event) {
        const rect = this.canvas.getBoundingClientRect();
        const x = Math.floor((event.clientX - rect.left) / this.pixelWidth);
        const y = Math.floor((event.clientY - rect.top) / this.pixelHeight);

        if (x < 0 || y < 0 || x >= this.width || y >= this.height) {
            return null;
        }

        return { x, y };
    }

    // --- Σχεδίαση ----------------------------------------------------------

    render() {
        const data = this.imageData.data;

        for (let i = 0; i < this.pixels.length; i++) {
            const color = this.rgbaPalette[this.pixels[i]] || this.rgbaPalette[0];
            const offset = i * 4;
            data[offset] = color[0];
            data[offset + 1] = color[1];
            data[offset + 2] = color[2];
            data[offset + 3] = 255;
        }

        this.offscreenCtx.putImageData(this.imageData, 0, 0);

        this.ctx.imageSmoothingEnabled = false;
        this.ctx.drawImage(
            this.offscreen,
            0, 0, this.width, this.height,
            0, 0, this.width * this.pixelWidth, this.height * this.pixelHeight);

        this.drawGrid();
    }

    drawGrid() {
        const pw = this.pixelWidth;
        const ph = this.pixelHeight;

        // Το πλέγμα pixel γίνεται θόρυβος σε μικρό ζουμ.
        if (this.showPixelGrid && this.zoom >= 6) {
            this.ctx.strokeStyle = 'rgba(255,255,255,0.12)';
            this.ctx.lineWidth = 1;
            this.ctx.beginPath();

            for (let x = 1; x < this.width; x++) {
                this.ctx.moveTo(x * pw + 0.5, 0);
                this.ctx.lineTo(x * pw + 0.5, this.height * ph);
            }

            for (let y = 1; y < this.height; y++) {
                this.ctx.moveTo(0, y * ph + 0.5);
                this.ctx.lineTo(this.width * pw, y * ph + 0.5);
            }

            this.ctx.stroke();
        }

        // Το πλέγμα κελιών δείχνει τα όρια όπου χτυπάει το attribute clash.
        if (this.showCellGrid && this.cellWidth > 0 && this.cellHeight > 0) {
            this.ctx.strokeStyle = 'rgba(255,64,64,0.55)';
            this.ctx.lineWidth = 1;
            this.ctx.beginPath();

            for (let x = this.cellWidth; x < this.width; x += this.cellWidth) {
                this.ctx.moveTo(x * pw + 0.5, 0);
                this.ctx.lineTo(x * pw + 0.5, this.height * ph);
            }

            for (let y = this.cellHeight; y < this.height; y += this.cellHeight) {
                this.ctx.moveTo(0, y * ph + 0.5);
                this.ctx.lineTo(this.width * pw, y * ph + 0.5);
            }

            this.ctx.stroke();
        }
    }

    // --- Πρωτογενείς πράξεις -----------------------------------------------

    setPixel(x, y, value) {
        if (x < 0 || y < 0 || x >= this.width || y >= this.height) {
            return;
        }

        const index = y * this.width + x;

        if (this.pixels[index] === value) {
            return;
        }

        this.pixels[index] = value;

        if (this.strokeChanges) {
            this.strokeChanges.set(index, value);
        }
    }

    // Bresenham: χωρίς αυτό, μια γρήγορη κίνηση ποντικιού αφήνει κενά ανάμεσα
    // στα δείγματα του pointer event.
    drawLine(x0, y0, x1, y1, value) {
        let dx = Math.abs(x1 - x0);
        let dy = -Math.abs(y1 - y0);
        const sx = x0 < x1 ? 1 : -1;
        const sy = y0 < y1 ? 1 : -1;
        let error = dx + dy;

        for (;;) {
            this.setPixel(x0, y0, value);

            if (x0 === x1 && y0 === y1) {
                break;
            }

            const doubled = 2 * error;

            if (doubled >= dy) {
                error += dy;
                x0 += sx;
            }

            if (doubled <= dx) {
                error += dx;
                y0 += sy;
            }
        }
    }

    drawRectangle(x0, y0, x1, y1, value, filled) {
        const left = Math.min(x0, x1);
        const right = Math.max(x0, x1);
        const top = Math.min(y0, y1);
        const bottom = Math.max(y0, y1);

        if (filled) {
            for (let y = top; y <= bottom; y++) {
                for (let x = left; x <= right; x++) {
                    this.setPixel(x, y, value);
                }
            }
            return;
        }

        for (let x = left; x <= right; x++) {
            this.setPixel(x, top, value);
            this.setPixel(x, bottom, value);
        }

        for (let y = top; y <= bottom; y++) {
            this.setPixel(left, y, value);
            this.setPixel(right, y, value);
        }
    }

    floodFill(x, y, value) {
        const target = this.pixels[y * this.width + x];

        if (target === value) {
            return;
        }

        // Ρητή στοίβα αντί για αναδρομή: ένα 128×128 γέμισμα θα υπερχείλιζε το call stack.
        const stack = [[x, y]];

        while (stack.length > 0) {
            const [cx, cy] = stack.pop();

            if (cx < 0 || cy < 0 || cx >= this.width || cy >= this.height) {
                continue;
            }

            const index = cy * this.width + cx;

            if (this.pixels[index] !== target) {
                continue;
            }

            this.setPixel(cx, cy, value);

            stack.push([cx + 1, cy], [cx - 1, cy], [cx, cy + 1], [cx, cy - 1]);
        }
    }

    // --- Χειρισμός ποντικιού -----------------------------------------------

    attachEvents() {
        this.onPointerDown = e => this.handleDown(e);
        this.onPointerMove = e => this.handleMove(e);
        this.onPointerUp = e => this.handleUp(e);

        this.canvas.addEventListener('pointerdown', this.onPointerDown);
        this.canvas.addEventListener('pointermove', this.onPointerMove);
        window.addEventListener('pointerup', this.onPointerUp);
        this.canvas.addEventListener('contextmenu', e => e.preventDefault());
    }

    beginStroke(point) {
        this.drawing = true;
        this.strokeChanges = new Map();
        this.strokeStart = point;
        this.lastPoint = point;
        this.snapshot = this.pixels.slice();
    }

    // Το δεξί πλήκτρο σβήνει (γράφει slot 0) — καθιερωμένη σύμβαση σε editors pixel art.
    valueFor(event) {
        return event.button === 2 || this.tool === 'eraser' ? 0 : this.colorIndex;
    }

    handleDown(event) {
        const point = this.toPixel(event);

        if (!point) {
            return;
        }

        event.preventDefault();
        this.canvas.setPointerCapture(event.pointerId);

        if (this.tool === 'picker') {
            const picked = this.pixels[point.y * this.width + point.x];
            this.dotNet.invokeMethodAsync('OnColorPicked', picked);
            return;
        }

        this.beginStroke(point);
        const value = this.valueFor(event);
        this.activeValue = value;

        if (this.tool === 'fill') {
            this.floodFill(point.x, point.y, value);
        } else if (this.tool === 'pencil' || this.tool === 'eraser') {
            this.setPixel(point.x, point.y, value);
        }

        this.render();
    }

    handleMove(event) {
        const point = this.toPixel(event);

        if (point) {
            this.dotNet.invokeMethodAsync('OnCursorMoved', point.x, point.y);
        }

        if (!this.drawing || !point) {
            return;
        }

        if (this.tool === 'pencil' || this.tool === 'eraser') {
            this.drawLine(this.lastPoint.x, this.lastPoint.y, point.x, point.y, this.activeValue);
            this.lastPoint = point;
            this.render();
            return;
        }

        if (this.tool === 'line' || this.tool === 'rect' || this.tool === 'rectFilled') {
            // Επαναφορά και επανασχεδίαση: το preview δεν πρέπει να αφήνει ίχνη.
            this.pixels.set(this.snapshot);
            this.strokeChanges.clear();

            if (this.tool === 'line') {
                this.drawLine(this.strokeStart.x, this.strokeStart.y, point.x, point.y, this.activeValue);
            } else {
                this.drawRectangle(
                    this.strokeStart.x, this.strokeStart.y, point.x, point.y,
                    this.activeValue, this.tool === 'rectFilled');
            }

            this.lastPoint = point;
            this.render();
        }
    }

    handleUp(event) {
        if (!this.drawing) {
            return;
        }

        this.drawing = false;

        if (this.canvas.hasPointerCapture && event.pointerId !== undefined) {
            try {
                this.canvas.releasePointerCapture(event.pointerId);
            } catch {
                // Ο pointer μπορεί να έχει ήδη απελευθερωθεί — δεν πειράζει.
            }
        }

        this.commitStroke();
    }

    // Μία κλήση στον server ανά πινελιά, όχι ανά pixel.
    commitStroke() {
        if (!this.strokeChanges || this.strokeChanges.size === 0) {
            this.strokeChanges = null;
            return;
        }

        const indices = new Array(this.strokeChanges.size);
        const values = new Array(this.strokeChanges.size);
        let i = 0;

        for (const [index, value] of this.strokeChanges) {
            indices[i] = index;
            values[i] = value;
            i++;
        }

        const previous = new Array(indices.length);

        for (let k = 0; k < indices.length; k++) {
            previous[k] = this.snapshot[indices[k]];
        }

        this.strokeChanges = null;
        this.snapshot = null;

        this.dotNet.invokeMethodAsync('OnStrokeCommitted', indices, values, previous);
    }

    // --- API προς τον Blazor ------------------------------------------------

    setPixels(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);

        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }

        this.pixels = bytes;
        this.render();
    }

    getPixels() {
        let binary = '';

        for (let i = 0; i < this.pixels.length; i++) {
            binary += String.fromCharCode(this.pixels[i]);
        }

        return btoa(binary);
    }

    dispose() {
        this.canvas.removeEventListener('pointerdown', this.onPointerDown);
        this.canvas.removeEventListener('pointermove', this.onPointerMove);
        window.removeEventListener('pointerup', this.onPointerUp);
    }
}

// --- Δημόσιο API του module ------------------------------------------------

export function create(id, canvas, dotNetRef, options) {
    destroy(id);
    editors.set(id, new PixelCanvas(canvas, dotNetRef, options));
}

export function destroy(id) {
    const editor = editors.get(id);

    if (editor) {
        editor.dispose();
        editors.delete(id);
    }
}

export function setPixels(id, base64) {
    editors.get(id)?.setPixels(base64);
}

export function getPixels(id) {
    return editors.get(id)?.getPixels() ?? '';
}

export function setPalette(id, palette) {
    editors.get(id)?.setPalette(palette);
}

export function setTool(id, tool) {
    const editor = editors.get(id);

    if (editor) {
        editor.tool = tool;
    }
}

export function setColorIndex(id, index) {
    const editor = editors.get(id);

    if (editor) {
        editor.colorIndex = index;
    }
}

export function setZoom(id, zoom) {
    editors.get(id)?.setZoom(zoom);
}

export function setGrid(id, pixelGrid, cellGrid) {
    const editor = editors.get(id);

    if (editor) {
        editor.showPixelGrid = pixelGrid;
        editor.showCellGrid = cellGrid;
        editor.render();
    }
}
