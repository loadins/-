using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ChatWeb.Models;

namespace ChatWeb.Data;

public class Store
{
    private readonly string _usersFile = "Data/users_db.json";
    private readonly string _msgsDir = "Data/messages";
    private readonly Lock _lock = new();

    public Dictionary<string, UserData> Users { get; private set; }
    public Dictionary<string, HashSet<string>> Rooms { get; private set; } = new() { { "General", [] } };
    public Dictionary<string, string> UserRoom { get; private set; } = [];

    public Store()
    {
        Directory.CreateDirectory(_msgsDir);
        Users = File.Exists(_usersFile)
            ? JsonSerializer.Deserialize<Dictionary<string, UserData>>(File.ReadAllText(_usersFile))!
            : [];
    }

    public bool TryRegister(string user, string pass)
    {
        lock (_lock)
        {
            if (Users.ContainsKey(user)) return false;
            var salt = RandomNumberGenerator.GetHexString(32);
            Users[user] = new UserData(Hash(pass, salt), salt);
            SaveUsers();
            return true;
        }
    }

    public bool TryLogin(string user, string pass)
    {
        lock (_lock)
        {
            if (!Users.TryGetValue(user, out var ud)) return false;
            return Hash(pass, ud.Salt) == ud.PasswordHash;
        }
    }

    public void JoinRoom(string user, string room)
    {
        lock (_lock)
        {
            if (UserRoom.TryGetValue(user, out var old)) Rooms[old].Remove(user);
            if (!Rooms.ContainsKey(room)) Rooms[room] = [];
            Rooms[room].Add(user);
            UserRoom[user] = room;
        }
    }

    public string GetUserRoom(string user)
    {
        lock (_lock) { return UserRoom.GetValueOrDefault(user, "General"); }
    }

    public HashSet<string> GetRoomUsers(string room)
    {
        lock (_lock) { return Rooms.GetValueOrDefault(room, []); }
    }

    public List<RoomInfo> GetRoomList()
    {
        lock (_lock)
        {
            return Rooms.Select(r => new RoomInfo(r.Key, r.Value)).ToList();
        }
    }

    public void SaveMessage(ChatMessage msg)
    {
        var file = Path.Combine(_msgsDir, $"{msg.Room}.json");
        var msgs = File.Exists(file)
            ? JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(file))!
            : [];
        msgs.Add(msg);
        if (msgs.Count > 200) msgs = msgs.TakeLast(200).ToList();
        File.WriteAllText(file, JsonSerializer.Serialize(msgs));
    }

    public List<ChatMessage> GetRecentMessages(string room, int count = 50)
    {
        var file = Path.Combine(_msgsDir, $"{room}.json");
        if (!File.Exists(file)) return [];
        var msgs = JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(file))!;
        return msgs.TakeLast(count).ToList();
    }

    static string Hash(string p, string salt) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + p)));

    void SaveUsers() => File.WriteAllText(_usersFile, JsonSerializer.Serialize(Users));
}