using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components.Forms;

namespace VideoHostingService.Models;

public class VideoUpload
{
    [Required]
    public string Title { get; set; }
    [Required]
    public string Description { get; set; }
    public IBrowserFile Thumbnail { get; set; }
    public IBrowserFile VideoFile { get; set; }
}