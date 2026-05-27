using Microsoft.AspNetCore.SignalR;
using ChatWeb.Models;
using ChatWeb.Data;

namespace ChatWeb;

public class ChatHub : Hub
{
    readonly Store _store;
    static readonly Dictionary<string, string> Connections = [];
    static readonly Lock _lock = new();

    public ChatHub(Store store) { _store = store; }

    public override Task OnDisconnectedAsync(Exception? ex)
    {
        lock (_lock) Connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(ex);
    }

    public async Task Join(string user, string room)
    {
        lock (_lock) Connections[Context.ConnectionId] = user;
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        await Clients.OthersInGroup(room).SendAsync("UserJoined", user);
    }

    public async Task SendMessage(string user, string room, string text)
    {
        var msg = new ChatMessage(user, text, room, DateTime.UtcNow);
        _store.SaveMessage(msg);
        await Clients.Group(room).SendAsync("NewMessage", msg);
    }
}