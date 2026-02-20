using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class Attachment
{
    public int Id { get; set; }

    public int DevRequestId { get; set; }
    public DevRequest? DevRequest { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Stored file path relative to the uploads root.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string StoredPath { get; set; } = string.Empty;

    [MaxLength(200)]
    public string UploadedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
