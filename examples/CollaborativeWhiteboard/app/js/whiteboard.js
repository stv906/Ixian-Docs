// ===== CANVAS SETUP =====
const canvas = document.getElementById('whiteboard');
const ctx = canvas.getContext('2d');
canvas.width = window.innerWidth;
canvas.height = window.innerHeight - 60;

let drawing = false;
let currentColor = '#000000';
let currentSize = 3;
let lastX = 0;
let lastY = 0;

// Optional local tracking (SDK only gives initial list on init)
let remoteUsers = [];

// ===== SDK LIFECYCLE =====
SpixiAppSdk.onInit = function(sessionId, userAddresses) {
    console.log('Session started:', sessionId, 'Users:', userAddresses);
    remoteUsers = userAddresses;
    initControls();
    initCanvasEvents();
};

SpixiAppSdk.onNetworkData = function(senderAddress, raw) {
    try {
        const msg = JSON.parse(raw);
        if (msg.type === 'clear') {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        } else if (msg.type === 'draw') {
            drawLine(msg.x1, msg.y1, msg.x2, msg.y2, msg.color, msg.size);
        }
    } catch (e) {
        console.error('Bad network payload', e);
    }
};

window.onload = SpixiAppSdk.fireOnLoad; // MUST be set so Spixi can trigger onInit

// ===== UI & EVENTS =====
function initControls() {
    document.getElementById('colorPicker').addEventListener('change', e => currentColor = e.target.value);
    document.getElementById('brushSize').addEventListener('change', e => currentSize = e.target.value);
    document.getElementById('clearBtn').addEventListener('click', () => {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        broadcast({ type: 'clear' });
    });
}

function initCanvasEvents() {
    canvas.addEventListener('mousedown', startDrawing);
    canvas.addEventListener('mousemove', draw);
    canvas.addEventListener('mouseup', stopDrawing);
    canvas.addEventListener('mouseout', stopDrawing);
    canvas.addEventListener('touchstart', handleTouch, { passive: false });
    canvas.addEventListener('touchmove', handleTouch, { passive: false });
    canvas.addEventListener('touchend', stopDrawing);
}

function startDrawing(e) {
    drawing = true;
    [lastX, lastY] = [e.offsetX, e.offsetY];
}

function draw(e) {
    if (!drawing) return;
    const x = e.offsetX;
    const y = e.offsetY;
    drawLine(lastX, lastY, x, y, currentColor, currentSize); // local
    broadcast({ // remote
        type: 'draw',
        x1: lastX,
        y1: lastY,
        x2: x,
        y2: y,
        color: currentColor,
        size: currentSize
    });
    [lastX, lastY] = [x, y];
}

function stopDrawing() { drawing = false; }

function drawLine(x1, y1, x2, y2, color, size) {
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.strokeStyle = color;
    ctx.lineWidth = size;
    ctx.lineCap = 'round';
    ctx.stroke();
}

function handleTouch(e) {
    e.preventDefault();
    const t = e.touches[0];
    const simulated = new MouseEvent(e.type === 'touchstart' ? 'mousedown' : 'mousemove', {
        clientX: t.clientX,
        clientY: t.clientY
    });
    canvas.dispatchEvent(simulated);
}

// ===== NETWORK WRAPPER =====
function broadcast(obj) {
    // SDK handles encryption, routing, reliability.
    SpixiAppSdk.sendNetworkData(JSON.stringify(obj));
}

// ===== RESIZE PRESERVATION =====
window.addEventListener('resize', () => {
    const tmp = document.createElement('canvas');
    tmp.width = canvas.width; tmp.height = canvas.height;
    tmp.getContext('2d').drawImage(canvas, 0, 0);
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight - 60;
    ctx.drawImage(tmp, 0, 0);
});
