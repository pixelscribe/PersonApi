using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PersonApi.Models;

[Table("person")]
public class Person
{
    [Column("id")]
    public ulong Id { get; set; }

    [Column("first_name")]
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Column("last_name")]
    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Column("email")]
    [Required, MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
