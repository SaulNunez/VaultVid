using System.ComponentModel.DataAnnotations;

namespace VideoHostingService.Models;

public class CreateComment
{
    [MaxLength(512)]
    public string Comment { get; set; }
}