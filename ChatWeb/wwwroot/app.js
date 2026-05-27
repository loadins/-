let connection = null;
let currentRoom = "General";
let currentUser = "";

// Auth
async function login() { await auth("/api/login"); }
async function register() { await auth("/api/register"); }
async function auth(url) {
    const user = document.getElementById("loginUser").value.trim();
    const pass = document.getElementById("loginPass").value;
    const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ user, pass })
    });
    const data = await res.json();
    if (data.ok) { currentUser = user; showChat(); }
    else document.getElementById("loginError").textContent = data.error;
}

async function logout() {
    await fetch("/api/logout", { method: "POST" });
    if (connection) connection.stop();
    document.getElementById("loginPage").classList.remove("hidden");
    document.getElementById("chatPage").classList.add("hidden");
}

// Chat UI
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
    connection.on("NewMessage", msg => addMessage(msg, false));
    connection.on("UserJoined", user => {
        const el = document.getElementById("messages");
        el.innerHTML += `<div class="msg system"><span class="user">${esc(user)} зашел</span></div>`;
        el.scrollTop = el.scrollHeight;
    });
    connection.start().then(() => connection.invoke("Join", currentUser, currentRoom));
    loadMessages(currentRoom);
}

async function send() {
    const input = document.getElementById("msgInput");
    const text = input.value.trim();
    if (!text) return;
    input.value = "";
    const msgs = document.getElementById("messages");
    msgs.innerHTML += `<div class="msg"><span class="user">${esc(currentUser)}</span><span class="text">${esc(text)}</span></div>`;
    msgs.scrollTop = msgs.scrollHeight;
    await connection.invoke("SendMessage", currentUser, currentRoom, text);
    await fetch("/api/messages/" + currentRoom, { method: "GET" });
}

function addMessage(msg, prepend) {
    const el = document.getElementById("messages");
    if (prepend) el.innerHTML = "";
    el.innerHTML += `<div class="msg"><span class="user">${esc(msg.user)}</span><span class="text">${esc(msg.text)}</span><span class="time">${new Date(msg.time).toLocaleTimeString()}</span></div>`;
    el.scrollTop = el.scrollHeight;
}

async function loadMessages(room) {
    const res = await fetch("/api/messages/" + room);
    const msgs = await res.json();
    const el = document.getElementById("messages");
    el.innerHTML = "";
    msgs.forEach(m => addMessage(m, false));
}

async function loadRooms() {
    const res = await fetch("/api/rooms");
    const rooms = await res.json();
    const el = document.getElementById("roomList");
    el.innerHTML = rooms.map(r =>
        `<div class="sidebar-item ${r.name === currentRoom ? 'active' : ''}" onclick="switchRoom('${esc(r.name)}')">${esc(r.name)} (${r.users.length})</div>`
    ).join("");
}

async function switchRoom(room) {
    if (connection) {
        await connection.stop();
    }
    currentRoom = room;
    document.getElementById("roomName").textContent = room;
    connectSignalR();
    loadRooms();
}

async function loadFiles() {
    const res = await fetch("/api/files");
    const files = await res.json();
    const el = document.getElementById("fileList");
    el.innerHTML = files.map(f =>
        `<div><a href="/api/download/${currentUser}/${f}" target="_blank">${esc(f)}</a></div>`
    ).join("");
}

document.getElementById("uploadForm").addEventListener("submit", async e => {
    e.preventDefault();
    const form = new FormData();
    const fileInput = document.getElementById("fileInput");
    form.append("file", fileInput.files[0]);
    const res = await fetch("/api/upload", { method: "POST", body: form });
    const data = await res.json();
    if (data.ok) {
        loadFiles();
        const msgs = document.getElementById("messages");
        msgs.innerHTML += `<div class="msg file-msg">📎 <a href="/api/download/${currentUser}/${data.name}" target="_blank">${esc(data.name)}</a></div>`;
        msgs.scrollTop = msgs.scrollHeight;
    }
    fileInput.value = "";
});

document.getElementById("msgInput").addEventListener("keydown", e => {
    if (e.key === "Enter") send();
});

function esc(s) {
    const d = document.createElement("div");
    d.textContent = s;
    return d.innerHTML;
}

// Check if already logged in
fetch("/api/me").then(r => r.json()).then(d => {
    if (d.user) { currentUser = d.user; showChat(); }
});
