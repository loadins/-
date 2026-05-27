let connection = null;
let currentRoom = "General";
let currentUser = "";
let sending = false;

function get(endpoint) {
    return fetch(endpoint, { credentials: "same-origin" }).then(r => r.json());
}
function post(url, body) {
    return fetch(url, {
        method: "POST",
        credentials: "same-origin",
        headers: body instanceof FormData ? {} : { "Content-Type": "application/json" },
        body
    }).then(r => r.json());
}

async function login() { await auth("/api/login"); }
async function register() { await auth("/api/register"); }
async function auth(url) {
    const user = document.getElementById("loginUser").value.trim();
    const pass = document.getElementById("loginPass").value;
    const data = await post(url, JSON.stringify({ user, pass }));
    if (data.ok) { currentUser = user; showChat(); }
    else document.getElementById("loginError").textContent = data.error;
}

async function logout() {
    await post("/api/logout");
    if (connection) connection.stop();
    document.getElementById("loginPage").classList.remove("hidden");
    document.getElementById("chatPage").classList.add("hidden");
}

async function showChat() {
    document.getElementById("loginPage").classList.add("hidden");
    document.getElementById("chatPage").classList.remove("hidden");
    document.getElementById("userName").textContent = currentUser;
    await loadRooms();
    await loadFiles();
    connectSignalR();
}

function connectSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/chat")
        .build();
    connection.on("NewMessage", msg => addMessage(msg));
    connection.on("UserJoined", user => {
        const el = document.getElementById("messages");
        el.innerHTML += `<div class="msg system">${esc(user)} зашел</div>`;
        el.scrollTop = el.scrollHeight;
    });
    connection.on("UserLeft", user => {
        const el = document.getElementById("messages");
        el.innerHTML += `<div class="msg system">${esc(user)} вышел</div>`;
        el.scrollTop = el.scrollHeight;
    });
    connection.on("RoomUsers", users => {
        const el = document.getElementById("userList");
        el.innerHTML = users.map(u => `<div class="sidebar-item">${esc(u)}</div>`).join("");
    });
    connection.start().then(() => connection.invoke("Join", currentUser, currentRoom));
    loadMessages(currentRoom);
}

async function send() {
    if (sending) return;
    const input = document.getElementById("msgInput");
    const text = input.value.trim();
    if (!text) return;
    sending = true;
    input.value = "";
    try {
        await connection.invoke("SendMessage", currentUser, currentRoom, text);
    } finally { sending = false; }
}

function addMessage(msg) {
    const el = document.getElementById("messages");
    el.innerHTML += `<div class="msg"><span class="user">${esc(msg.user)}</span><span class="text">${esc(msg.text)}</span><span class="time">${new Date(msg.time).toLocaleTimeString()}</span></div>`;
    el.scrollTop = el.scrollHeight;
}

async function loadMessages(room) {
    const msgs = await get("/api/messages/" + room);
    const el = document.getElementById("messages");
    el.innerHTML = "";
    msgs.forEach(m => addMessage(m));
}

async function loadRooms() {
    const rooms = await get("/api/rooms");
    const el = document.getElementById("roomList");
    el.innerHTML = rooms.map(r =>
        `<div class="sidebar-item ${r.name === currentRoom ? 'active' : ''}" onclick="switchRoom('${escAttr(r.name)}')">${esc(r.name)} (${r.users.length})</div>`
    ).join("");
}

async function switchRoom(room) {
    if (connection) { await connection.stop(); }
    currentRoom = room;
    document.getElementById("roomName").textContent = room;
    connectSignalR();
    loadRooms();
}

async function loadFiles() {
    const files = await get("/api/files");
    const el = document.getElementById("fileList");
    el.innerHTML = files.map(f =>
        `<div><a href="/api/download/${currentUser}/${f}" target="_blank">${esc(f)}</a></div>`
    ).join("");
}

document.getElementById("uploadForm").addEventListener("submit", async e => {
    e.preventDefault();
    const btn = document.querySelector("#uploadForm button");
    btn.disabled = true;
    const form = new FormData(document.getElementById("uploadForm"));
    const data = await post("/api/upload", form);
    if (data.ok) {
        loadFiles();
        const msg = `📎 ${data.name}`;
        try { await connection.invoke("SendMessage", currentUser, currentRoom, msg); } catch {}
    } else {
        alert(data.error);
    }
    document.getElementById("fileInput").value = "";
    btn.disabled = false;
});

document.getElementById("msgInput").addEventListener("keydown", e => {
    if (e.key === "Enter") send();
});

function showGuide() {
    document.getElementById("guideModal").classList.remove("hidden");
}
function hideGuide() {
    document.getElementById("guideModal").classList.add("hidden");
}

function esc(s) {
    const d = document.createElement("div");
    d.textContent = s;
    return d.innerHTML;
}
function escAttr(s) {
    return esc(s).replace(/'/g, "&#39;").replace(/"/g, "&quot;");
}

get("/api/me").then(d => {
    if (d.user) { currentUser = d.user; showChat(); }
});