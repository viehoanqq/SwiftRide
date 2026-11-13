namespace IdentityService.Domain.Entities;

public class Role
{
    public const int RiderId = 1;
    public const int DriverId = 2;

    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
