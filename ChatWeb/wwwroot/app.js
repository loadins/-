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
        if (text.startsWith("/")) { execCmd(text); return; }
        await connection.invoke("SendMessage", currentUser, currentRoom, text);
    } finally { sending = false; }
}

function execCmd(cmd) {
    const el = document.getElementById("messages");
    if (cmd === "/help") { showGuide(); return; }
    if (cmd === "/clear") { el.innerHTML = ""; return; }
    if (cmd === "/users") {
        el.innerHTML += `<div class="msg system">Участники: ${document.getElementById("userList").innerText || "никого"}</div>`;
        el.scrollTop = el.scrollHeight;
        return;
    }
    if (cmd === "/files") {
        const files = document.getElementById("fileList");
        if (files.children.length === 0) {
            el.innerHTML += `<div class="msg system">Файлов нет</div>`;
        } else {
            el.innerHTML += `<div class="msg system">Файлы: ${files.innerText.replace(/\s+/g, ' ')}</div>`;
        }
        el.scrollTop = el.scrollHeight;
        return;
    }
    el.innerHTML += `<div class="msg system">Неизвестная команда: ${esc(cmd)}</div>`;
    el.scrollTop = el.scrollHeight;
}

function addMessage(msg) {
    const el = document.getElementById("messages");
    const isWhisper = msg.room === "WHISPER";
    const cls = isWhisper ? " msg-whisper" : "";
    el.innerHTML += `<div class="msg${cls}"><span class="user">${esc(msg.user)}</span><span class="text">${esc(msg.text)}</span><span class="time">${new Date(msg.time).toLocaleTimeString()}</span></div>`;
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

// --- COMMAND BUTTONS ---
function showCmdModal(title, html) {
    document.getElementById("cmdModalTitle").textContent = title;
    document.getElementById("cmdModalBody").innerHTML = html;
    document.getElementById("cmdModal").classList.remove("hidden");
    const firstInput = document.querySelector("#cmdModalBody input");
    if (firstInput) setTimeout(() => firstInput.focus(), 100);
}
function hideCmdModal() {
    document.getElementById("cmdModal").classList.add("hidden");
}

function cmdWhisper() {
    const users = [...document.querySelectorAll("#userList .sidebar-item")].map(e => e.textContent).filter(u => u !== currentUser);
    if (users.length === 0) { addSystemMsg("Нет других пользователей"); return; }
    showCmdModal("Шёпот", `
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Кому:</label>
            <select id="wTarget" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
                ${users.map(u => `<option value="${escAttr(u)}">${esc(u)}</option>`).join("")}
            </select>
        </div>
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Сообщение:</label>
            <input id="wText" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
        </div>
        <button class="btn" onclick="doWhisper()" style="margin-top:.5rem">Отправить</button>
    `);
}
async function doWhisper() {
    const target = document.getElementById("wTarget").value;
    const text = document.getElementById("wText").value.trim();
    if (!text) return;
    hideCmdModal();
    try { await connection.invoke("Whisper", currentUser, target, text); } catch {}
}

function cmdReply() {
    showCmdModal("Ответ", `
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Сообщение:</label>
            <input id="rText" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
        </div>
        <button class="btn" onclick="doReply()" style="margin-top:.5rem">Отправить</button>
    `);
}
async function doReply() {
    const text = document.getElementById("rText").value.trim();
    if (!text) return;
    hideCmdModal();
    const msg = `[REPLY] ${text}`;
    try { await connection.invoke("SendMessage", currentUser, currentRoom, msg); } catch {}
}

function cmdCreate() {
    showCmdModal("Создать комнату", `
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Название:</label>
            <input id="cName" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
        </div>
        <button class="btn" onclick="doCreate()" style="margin-top:.5rem">Создать</button>
    `);
}
async function doCreate() {
    const name = document.getElementById("cName").value.trim();
    if (!name) return;
    hideCmdModal();
    try {
        await connection.invoke("CreateRoom", currentUser, name);
        await loadRooms();
    } catch {}
}

function cmdRooms() {
    hideCmdModal();
    get("/api/rooms").then(rooms => {
        const names = rooms.map(r => r.name).join(", ");
        addSystemMsg(`Комнаты: ${names}`);
    });
}

function cmdLeave() {
    if (currentRoom !== "General") switchRoom("General");
}

function cmdFiles() {
    hideCmdModal();
    get("/api/files").then(files => {
        if (files.length === 0) addSystemMsg("Файлов нет");
        else addSystemMsg(`Файлы: ${files.join(", ")}`);
    });
}

function cmdDelay() {
    showCmdModal("Задержка", `
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Секунд:</label>
            <input id="dSec" type="number" min="1" max="3600" value="5" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
        </div>
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Сообщение:</label>
            <input id="dText" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
        </div>
        <button class="btn" onclick="doDelay()" style="margin-top:.5rem">Отправить</button>
    `);
}
async function doDelay() {
    const sec = parseInt(document.getElementById("dSec").value) || 5;
    const text = document.getElementById("dText").value.trim();
    if (!text) return;
    hideCmdModal();
    addSystemMsg(`Сообщение будет отправлено через ${sec}с`);
    try { await connection.invoke("DelayedMessage", currentUser, currentRoom, sec, text); } catch {}
}

function cmdForward() {
    const files = [...document.querySelectorAll("#fileList a")].map(a => a.textContent);
    const users = [...document.querySelectorAll("#userList .sidebar-item")].map(e => e.textContent).filter(u => u !== currentUser);
    if (files.length === 0) { addSystemMsg("Нет файлов для пересылки"); return; }
    if (users.length === 0) { addSystemMsg("Нет других пользователей"); return; }
    showCmdModal("Переслать файл", `
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Кому:</label>
            <select id="fTarget" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
                ${users.map(u => `<option value="${escAttr(u)}">${esc(u)}</option>`).join("")}
            </select>
        </div>
        <div style="margin-bottom:.8rem">
            <label style="color:#888;font-size:.8rem">Файл:</label>
            <select id="fFile" style="width:100%;padding:.6rem;background:#0d0d0d;border:1px solid #333;border-radius:4px;color:#e0e0e0">
                ${files.map(f => `<option value="${escAttr(f)}">${esc(f)}</option>`).join("")}
            </select>
        </div>
        <button class="btn" onclick="doForward()" style="margin-top:.5rem">Отправить</button>
    `);
}
async function doForward() {
    const target = document.getElementById("fTarget").value;
    const file = document.getElementById("fFile").value;
    hideCmdModal();
    addSystemMsg(`Файл '${file}' отправлен ${target}`);
    const msg = `📎 ${file} (отправлен ${target})`;
    try { await connection.invoke("SendMessage", currentUser, currentRoom, msg); } catch {}
}

function addSystemMsg(text) {
    const el = document.getElementById("messages");
    el.innerHTML += `<div class="msg system">${esc(text)}</div>`;
    el.scrollTop = el.scrollHeight;
}

// --- UPLOAD ---
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