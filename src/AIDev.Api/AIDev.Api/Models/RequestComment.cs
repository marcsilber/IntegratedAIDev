using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class RequestComment
{
    public int Id { get; set; }

    public int DevRequestId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DevRequest? DevRequest { get; set; }
}
