using Microsoft.AspNetCore.SignalR;
using ChatWeb.Models;
using ChatWeb.Data;

namespace ChatWeb;

public class ChatHub : Hub
{
    readonly Store _store;
    static readonly Dictionary<string, string> Connections = [];
    static readonly Dictionary<string, string> UserConns = [];
    static readonly Lock _lock = new();

    public ChatHub(Store store) { _store = store; }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        string? user = null;
        lock (_lock)
        {
            if (Connections.Remove(Context.ConnectionId, out user))
                UserConns.Remove(user);
        }
        if (user != null)
        {
            var room = _store.GetUserRoom(user);
            _store.LeaveRoom(user);
            await Clients.Group(room).SendAsync("UserLeft", user);
        }
        await base.OnDisconnectedAsync(ex);
    }

    public async Task Join(string user, string room)
    {
        lock (_lock)
        {
            Connections[Context.ConnectionId] = user;
            UserConns[user] = Context.ConnectionId;
        }
        _store.JoinRoom(user, room);
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        await Clients.OthersInGroup(room).SendAsync("UserJoined", user);
        await Clients.Caller.SendAsync("RoomUsers", _store.GetRoomUsers(room).ToList());
    }

    public async Task SendMessage(string user, string room, string text)
    {
        var msg = new ChatMessage(user, text, room, DateTime.UtcNow);
        _store.SaveMessage(msg);
        await Clients.Group(room).SendAsync("NewMessage", msg);
    }

    public async Task Whisper(string user, string target, string text)
    {
        string? cid;
        lock (_lock) UserConns.TryGetValue(target, out cid);
        if (cid == null)
        {
            await Clients.Caller.SendAsync("NewMessage", new ChatMessage("SERVER", $"Пользователь {target} не в сети", _store.GetUserRoom(user), DateTime.UtcNow));
            return;
        }
        var msg = new ChatMessage(user, text, "WHISPER", DateTime.UtcNow);
        await Clients.Client(cid).SendAsync("NewMessage", msg);
        await Clients.Caller.SendAsync("NewMessage", new ChatMessage(user, $"→ {target}: {text}", "WHISPER", DateTime.UtcNow));
    }

    public async Task CreateRoom(string user, string room)
    {
        _store.CreateRoom(room);
        await Clients.Caller.SendAsync("NewMessage", new ChatMessage("SERVER", $"Комната '{room}' создана", _store.GetUserRoom(user), DateTime.UtcNow));
    }

    public async Task DelayedMessage(string user, string room, int seconds, string text)
    {
        await Task.Delay(seconds * 1000);
        var msg = new ChatMessage(user, $"[DELAYED {seconds}s] {text}", room, DateTime.UtcNow);
        _store.SaveMessage(msg);
        await Clients.Group(room).SendAsync("NewMessage", msg);
    }
}