using System.ComponentModel.DataAnnotations;

namespace VideoHostingService.Models;

public class CreateComment
{
    [Required(ErrorMessage = "A comment can't be empty.")]
    [MaxLength(512)]
    public string Comment { get; set; } = string.Empty;
}
