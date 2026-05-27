using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Spectre.Console;

int Port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8888;
const string StoragePath = "Storage";
const string UsersFile = "users_db.json";
const string OfflineFile = "offline_msgs.json";

Dictionary<string, TcpClient> activeClients = [];
Dictionary<string, string> userDb = LoadUsers();
Dictionary<string, List<string>> offlineDb = LoadOfflineMsgs();
Dictionary<string, HashSet<string>> rooms = new() { { "General", [] } };
Dictionary<string, string> userCurrentRoom = [];

System.Threading.Lock globalLock = new();

if (!Directory.Exists(StoragePath)) Directory.CreateDirectory(StoragePath);

AnsiConsole.Write(new FigletText("NEXUS CORE").Color(Color.Gold1));
var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();
AnsiConsole.MarkupLine($"[bold yellow]СЕРВЕР ЗАПУЩЕН[/] на порту {Port}");

while (true) {
    try {
        var client = await listener.AcceptTcpClientAsync();
        _ = Task.Run(() => HandleClient(client));
    } catch { }
}

async Task HandleClient(TcpClient client) {
    string? currentUser = null;
    var stream = client.GetStream();
    try {
        while (currentUser == null) {
            var authData = await ReceiveStringAsync(stream);
            if (authData == null) return;
            var parts = authData.Split('|');
            if (parts.Length < 3) continue;
            string cmd = parts[0], user = parts[1].Trim(), pass = parts[2];
            string hash = GetHash(pass);

            lock (globalLock) {
                if (cmd == "REG") {
                    if (userDb.ContainsKey(user)) _ = SendStringAsync(stream, "AUTH_ERR|Логин занят");
                    else { userDb[user] = hash; SaveUsers(); currentUser = user; }
                } else if (cmd == "LOGIN") {
                    if (userDb.TryGetValue(user, out var h) && h == hash) {
                        if (activeClients.ContainsKey(user)) _ = SendStringAsync(stream, "AUTH_ERR|Уже в сети");
                        else currentUser = user;
                    } else _ = SendStringAsync(stream, "AUTH_ERR|Неверный пароль");
                }
            }
            if (currentUser != null) {
                await SendStringAsync(stream, "AUTH_OK|OK");
                lock (globalLock) {
                    activeClients[currentUser] = client;
                    userCurrentRoom[currentUser] = "General";
                    rooms["General"].Add(currentUser);
                }
                await SendStringAsync(stream, "ROOM_UPDATE|General");
                lock (globalLock) {
                    if (offlineDb.TryGetValue(currentUser, out var msgs)) {
                        foreach (var m in msgs) _ = SendStringAsync(stream, m);
                        offlineDb.Remove(currentUser);
                        SaveOfflineMsgs();
                    }
                }
                await BroadcastToRoomAsync("General", "SERVER", $"{currentUser} вошел в чат.");
            }
        }

        while (true) {
            var msg = await ReceiveStringAsync(stream);
            if (msg == null) break;

            // --- ЛОГИКА КОМАНД ---
            if (msg.StartsWith("/w ")) { // Личное сообщение: /w ник текст
                var p = msg.Split(' ', 3);
                if (p.Length == 3) {
                    await SendToClientAsync(p[1], "WHISPER", $"{currentUser}|{p[2]}");
                    await SendToClientAsync(currentUser, "SERVER", $"[grey]Шепот для {p[1]} отправлен.[/]");
                }
            }
            else if (msg.StartsWith("/r ")) { // Ответ: /r текст (всем в комнате, но выделено)
                await BroadcastToRoomAsync(userCurrentRoom[currentUser], "REPLY", $"{currentUser}|{msg[3..]}");
            }
            else if (msg.StartsWith("/delay ")) {
                var p = msg.Split(' ', 3);
                if (p.Length == 3 && int.TryParse(p[1], out int sec)) {
                    _ = Task.Run(async () => {
                        await Task.Delay(sec * 1000);
                        await BroadcastToRoomAsync(userCurrentRoom[currentUser], "PLAYER", $"{currentUser}|[grey][[DELAYED]][/] {p[2]}");
                    });
                }
            }
            else if (msg.StartsWith("/create ")) {
                string rName = msg[8..].Trim();
                lock(globalLock) rooms[rName] = [];
                await SendToClientAsync(currentUser, "SERVER", $"Комната '{rName}' создана.");
            }
            else if (msg.StartsWith("/join ") || msg == "/leave") {
                string targetRoom = (msg == "/leave") ? "General" : msg[6..].Trim();
                lock(globalLock) {
                    if (rooms.ContainsKey(targetRoom)) {
                        string old = userCurrentRoom[currentUser];
                        rooms[old].Remove(currentUser);
                        userCurrentRoom[currentUser] = targetRoom;
                        rooms[targetRoom].Add(currentUser);
                        _ = BroadcastToRoomAsync(old, "SERVER", $"{currentUser} ушел.");
                        _ = BroadcastToRoomAsync(targetRoom, "SERVER", $"{currentUser} зашел.");
                        _ = SendStringAsync(stream, $"ROOM_UPDATE|{targetRoom}");
                    }
                }
            }
            else if (msg == "/rooms") {
                string list = string.Join(", ", rooms.Keys);
                await SendToClientAsync(currentUser, "SERVER", $"Комнаты: {list}");
            }
            else if (msg == "/simg") {
                var userDir = Path.Combine(StoragePath, currentUser);
                var files = Directory.Exists(userDir) ? Directory.GetFiles(userDir).Select(Path.GetFileName).ToArray() : [];
                await SendToClientAsync(currentUser, "FILES", string.Join("|", files));
            }
            else if (msg.StartsWith("UPLOAD|")) {
                var parts = msg.Split('|');
                var userDir = Path.Combine(StoragePath, currentUser);
                Directory.CreateDirectory(userDir);
                await File.WriteAllBytesAsync(Path.Combine(userDir, parts[1]), Convert.FromBase64String(parts[2]));
                await SendToClientAsync(currentUser, "SERVER", "Файл загружен.");
            }
            else if (msg.StartsWith("/i ")) {
                var p = msg.Split(' ', 3);
                if (p.Length == 3) {
                    var path = Path.Combine(StoragePath, currentUser, p[2]);
                    if (File.Exists(path)) {
                        var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
                        await SendToClientAsync(p[1], "RECEIVE_IMG", $"{currentUser}|{p[2]}|{b64}");
                    }
                }
            }
            else {
                await BroadcastToRoomAsync(userCurrentRoom[currentUser], "PLAYER", $"{currentUser}|{msg}");
            }
        }
    } catch { }
    finally {
        if (currentUser != null) {
            lock (globalLock) {
                activeClients.Remove(currentUser);
                if (userCurrentRoom.TryGetValue(currentUser, out var r)) rooms[r].Remove(currentUser);
            }
        }
        client.Close();
    }
}

// Хелперы сервера
async Task BroadcastToRoomAsync(string room, string type, string content) {
    List<Task> ts = [];
    lock(globalLock) {
        if (!rooms.ContainsKey(room)) return;
        foreach(var m in rooms[room]) if(activeClients.TryGetValue(m, out var c)) ts.Add(SendStringAsync(c.GetStream(), $"{type}|{content}"));
    }
    await Task.WhenAll(ts);
}

async Task SendToClientAsync(string u, string t, string m) {
    TcpClient? c; lock(globalLock) activeClients.TryGetValue(u, out c);
    if(c != null) await SendStringAsync(c.GetStream(), $"{t}|{m}");
    else if (t == "WHISPER") { // Если в офлайне, сохраняем шепот
        lock(globalLock) {
            if(!offlineDb.ContainsKey(u)) offlineDb[u] = [];
            offlineDb[u].Add($"WHISPER|{m}");
            SaveOfflineMsgs();
        }
    }
}

async Task SendStringAsync(NetworkStream s, string v) {
    try {
        var d = Encoding.UTF8.GetBytes(v);
        await s.WriteAsync(BitConverter.GetBytes(d.Length));
        await s.WriteAsync(d);
    } catch {}
}

async Task<string?> ReceiveStringAsync(NetworkStream s) {
    try {
        var h = new byte[4]; if (await s.ReadAtLeastAsync(h, 4, false) < 4) return null;
        var b = new byte[BitConverter.ToInt32(h)]; await s.ReadAtLeastAsync(b, b.Length);
        return Encoding.UTF8.GetString(b);
    } catch { return null; }
}

string GetHash(string p) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p)));
void SaveUsers() => File.WriteAllText(UsersFile, JsonSerializer.Serialize(userDb));
Dictionary<string, string> LoadUsers() => File.Exists(UsersFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(UsersFile))! : [];
void SaveOfflineMsgs() => File.WriteAllText(OfflineFile, JsonSerializer.Serialize(offlineDb));
Dictionary<string, List<string>> LoadOfflineMsgs() => File.Exists(OfflineFile) ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(OfflineFile))! : [];