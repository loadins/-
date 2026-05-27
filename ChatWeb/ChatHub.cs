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

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        string? user = null;
        lock (_lock) Connections.Remove(Context.ConnectionId, out user);
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
        lock (_lock) Connections[Context.ConnectionId] = user;
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
}