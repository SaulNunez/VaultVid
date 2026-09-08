using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components.Forms;

namespace VideoHostingService.Models;

public class VideoUpload
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional; a video without a thumbnail is allowed.</summary>
    public IBrowserFile? Thumbnail { get; set; }

    [Required(ErrorMessage = "Pick a video file to upload.")]
    public IBrowserFile? VideoFile { get; set; }
}
