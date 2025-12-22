namespace Glimpse.Models;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Navigation property for many-to-many with Screenshots
    public ICollection<Screenshot> Screenshots { get; set; } = [];
}
