using System.ComponentModel.DataAnnotations;

namespace Velo.SQL.Models;

public abstract class AuditableEntity
{
    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    public DateTimeOffset? ModifiedDate { get; set; }

    public bool IsDeleted { get; set; }
}
