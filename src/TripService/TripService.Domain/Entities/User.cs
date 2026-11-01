namespace TripService.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
