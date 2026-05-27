using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using Spectre.Console;

// Отключаем предупреждения о платформозависимости для Beep
#pragma warning disable CA1416 

if (OperatingSystem.IsWindows()) Console.Title = "NEXUS TERMINAL";

var client = new TcpClient();
NetworkStream? stream = null;
List<string> chatHistory = []; 
List<string> receivedFiles = []; 
string myNick = "", currentRoom = "General";
bool isMuted = false;
StringBuilder inputBuffer = new();

string[] cmdTemplates = ["/mute", "/rooms", "/leave", "/simg", "/create ", "/join ", "/w ", "/r ", "/i ", "/upload ", "/delay ", "/view "];
int cmdIdx = -1;

// --- 1. ВХОД (ИСПРАВЛЕНО) ---
AnsiConsole.Write(new FigletText("NEXUS CHAT").Color(Color.Lime));

string GetRadminIp() {
    try {
        return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && a.ToString().StartsWith("26."))?.ToString() ?? "127.0.0.1";
    } catch { return "127.0.0.1"; }
}

while (true) {
    try {
        if (!client.Connected) {
            string defIp = GetRadminIp();
            string ip = AnsiConsole.Ask<string>($"IP сервера (Enter для {defIp}):", defIp);
            int port = AnsiConsole.Ask<int>("Порт (8888):", 8888);
            
            AnsiConsole.MarkupLine("[grey]Подключение к серверу...[/]");
            client.Connect(ip, port);
        }
        stream = client.GetStream();

        var mode = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Вход/Рег:").AddChoices("Вход", "Регистрация"));
        myNick = AnsiConsole.Ask<string>("Логин:").Trim();
        var pass = AnsiConsole.Prompt(new TextPrompt<string>("Пароль:").Secret());

        // ТУТ БЫЛА ОПЕЧАТКА (ИСПРАВЛЕНО: LOGIN вместо LOGIN運)
        string authCmd = mode == "Вход" ? "LOGIN" : "REG";
        await SendStringAsync(stream, $"{authCmd}|{myNick}|{pass}");
        
        AnsiConsole.MarkupLine("[grey]Ожидание ответа от сервера...[/]");
        var r = await ReceiveStringAsync(stream);
        
        if (r?.StartsWith("AUTH_OK") == true) {
            AnsiConsole.MarkupLine("[bold green]Авторизация успешна![/]");
            break; 
        } else {
            AnsiConsole.MarkupLine($"[red]Ошибка:[/] {r?.Split('|')[1].EscapeMarkup()}");
        }
    } catch (Exception ex) { 
        AnsiConsole.MarkupLine($"[red]Критическая ошибка:[/] {ex.Message}"); 
        client = new TcpClient();
        Thread.Sleep(2000); 
    }
}

// --- 2. ПОТОК ЧТЕНИЯ ---
AnsiConsole.Clear();
_ = Task.Run(async () => {
    while (true) {
        var raw = await ReceiveStringAsync(stream);
        if (raw == null) break;
        var p = raw.Split('|');
        if (p.Length < 2) continue;
        lock (chatHistory) {
            switch (p[0]) {
                case "PLAYER":
                    string sender = p[1], msg = p[2];
                    if (sender == myNick) chatHistory.Add($"[lime]{sender.EscapeMarkup()}: {msg.EscapeMarkup()}[/]");
                    else {
                        chatHistory.Add($"[white]{sender.EscapeMarkup()}:[/] {msg.EscapeMarkup()}");
                        if (!isMuted && OperatingSystem.IsWindows()) Task.Run(() => Console.Beep(800, 100));
                    }
                    break;
                case "WHISPER":
                    chatHistory.Add($"[bold magenta][[ ШЕПОТ от {p[1].EscapeMarkup()} ]]:[/] [italic magenta]{p[2].EscapeMarkup()}[/]");
                    if (!isMuted && OperatingSystem.IsWindows()) Task.Run(() => Console.Beep(1200, 200));
                    break;
                case "REPLY":
                    chatHistory.Add($"[italic cyan]{p[1].EscapeMarkup()} ответил:[/] [cyan]{p[2].EscapeMarkup()}[/]");
                    break;
                case "SERVER": 
                    chatHistory.Add($"[yellow]![/] [grey]{p[1].EscapeMarkup()}[/]"); 
                    break;
                case "ROOM_UPDATE": 
                    currentRoom = p[1]; chatHistory.Clear(); chatHistory.Add($"[bold green]Вы вошли в {p[1].EscapeMarkup()}[/]"); 
                    break;
                case "RECEIVE_IMG":
                    var fileName = p[2];
                    var path = Path.Combine("Downloads", fileName);
                    Directory.CreateDirectory("Downloads");
                    File.WriteAllBytes(path, Convert.FromBase64String(p[3]));
                    receivedFiles.Add(path);
                    chatHistory.Add($"[magenta]>>>[/] Файл от {p[1].EscapeMarkup()}. [white]/view {receivedFiles.Count - 1}[/]");
                    break;
            }
            if (chatHistory.Count > 100) chatHistory.RemoveAt(0);
        }
    }
});

// --- 3. ИНТЕРФЕЙС ---
var layout = new Layout("Root")
    .SplitRows(
        new Layout("Upper").SplitColumns(
            new Layout("Chat").Ratio(3),
            new Layout("Commands").Ratio(1)
        ),
        new Layout("Input").Size(5) 
    );

var commandsList = new Table().Border(TableBorder.None).HideHeaders().Expand();
commandsList.AddColumn("Cmd");
commandsList.AddRow("[lime]/mute[/] - Звук");
commandsList.AddRow("[lime]/rooms[/] - Комнаты");
commandsList.AddRow("[lime]/leave[/] - В General");
commandsList.AddRow("[lime]/simg[/] - Файлы");
commandsList.AddRow("[lime]/create[/] [grey]имя[/]");
commandsList.AddRow("[lime]/join[/] [grey]имя[/]");
commandsList.AddRow("[lime]/w[/] [grey]ник соо[/]");
commandsList.AddRow("[lime]/r[/] [grey]текст[/]");
commandsList.AddRow("[lime]/i[/] [grey]ник №[/]");
commandsList.AddRow("[lime]/upload[/] [grey]путь[/]");
commandsList.AddRow("[lime]/view[/] [grey]№[/]");
commandsList.AddRow("");
commandsList.AddRow("[grey]Выбор: ↑/↓[/]");

await AnsiConsole.Live(layout).StartAsync(async ctx => {
    while (true) {
        int maxVisibleLines = Math.Max(1, Console.WindowHeight - 10);
        var table = new Table().Border(TableBorder.None).HideHeaders().Expand().AddColumn("M");
        
        lock(chatHistory) { 
            var visibleMessages = chatHistory.TakeLast(maxVisibleLines);
            foreach(var l in visibleMessages) table.AddRow(l); 
        }
        
        layout["Chat"].Update(new Panel(table)
            .Header($" [white]Room: {currentRoom.EscapeMarkup()} | {myNick.EscapeMarkup()}[/] ")
            .Expand().BorderColor(Color.DeepSkyBlue1));

        layout["Commands"].Update(new Panel(commandsList).Header(" [yellow]Команды[/] ").Expand().BorderColor(Color.Gold1));

        string currentCmdHint = cmdIdx == -1 ? "" : $" [bold yellow]Выбрано: {cmdTemplates[cmdIdx].Trim()}[/]";
        layout["Input"].Update(new Panel(new Text(inputBuffer.ToString() + "█", new Style(Color.Lime)))
            .Header($" [white]Сообщение{currentCmdHint}[/] ").Padding(1, 0, 1, 0).Expand().BorderColor(Color.Lime));

        ctx.Refresh();

        if (Console.KeyAvailable) {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) {
                string s = inputBuffer.ToString(); inputBuffer.Clear(); cmdIdx = -1;
                if (!string.IsNullOrWhiteSpace(s)) await ProcessInput(s);
            }
            else if (key.Key == ConsoleKey.UpArrow) {
                cmdIdx = (cmdIdx <= 0) ? cmdTemplates.Length - 1 : cmdIdx - 1;
                inputBuffer.Clear().Append(cmdTemplates[cmdIdx]);
            }
            else if (key.Key == ConsoleKey.DownArrow) {
                cmdIdx = (cmdIdx >= cmdTemplates.Length - 1) ? 0 : cmdIdx + 1;
                inputBuffer.Clear().Append(cmdTemplates[cmdIdx]);
            }
            else if (key.Key == ConsoleKey.Backspace && inputBuffer.Length > 0) {
                inputBuffer.Remove(inputBuffer.Length - 1, 1); cmdIdx = -1;
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar)) {
                inputBuffer.Append(key.KeyChar); cmdIdx = -1;
            }
        }
        await Task.Delay(20);
    }
});

async Task ProcessInput(string input) {
    if (input == "/mute") isMuted = !isMuted;
    else if (input.StartsWith("/view ")) {
        if (int.TryParse(input[6..], out int idx) && idx >= 0 && idx < receivedFiles.Count) {
            try { Process.Start(new ProcessStartInfo(receivedFiles[idx]) { UseShellExecute = true }); } catch {}
        }
    }
    else if (input.StartsWith("/upload ")) {
        var path = input[8..].Trim('"').Trim();
        if (File.Exists(path)) {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(path));
            await SendStringAsync(stream, $"UPLOAD|{Path.GetFileName(path)}|{b64}");
        }
    }
    else await SendStringAsync(stream, input);
}

async Task SendStringAsync(NetworkStream s, string v) {
    try {
        var d = Encoding.UTF8.GetBytes(v);
        await s.WriteAsync(BitConverter.GetBytes(d.Length));
        await s.WriteAsync(d);
        await s.FlushAsync();
    } catch {}
}

async Task<string?> ReceiveStringAsync(NetworkStream s) {
    try {
        var h = new byte[4]; 
        if (await s.ReadAtLeastAsync(h, 4, false) < 4) return null;
        var b = new byte[BitConverter.ToInt32(h)]; 
        await s.ReadAtLeastAsync(b, b.Length);
        return Encoding.UTF8.GetString(b);
    } catch { return null; }
}