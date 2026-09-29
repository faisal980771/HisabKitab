using SQLite;

namespace HisabKitab.Models;

public class AppUser
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public string GoogleId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [Indexed]
    public string Email { get; set; } = string.Empty;

    public string? Mobile { get; set; }

    public string? ProfilePicture { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastLoginAtUtc { get; set; } = DateTime.UtcNow;
}