using System.ComponentModel.DataAnnotations;

namespace backend;

public class DataToDatabase
{
    [Key]
    public string? Id { get; set; }
    public string DeviceId { get; set; }
    public string LinkToPicture { get; set; }
    public DateTime Date { get; set; }
    
}