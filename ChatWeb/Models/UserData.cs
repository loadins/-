namespace ChatWeb.Models;

public record UserData(string PasswordHash, string Salt);
public record ChatMessage(string User, string Text, string Room, DateTime Time);
public record RoomInfo(string Name, HashSet<string> Users);