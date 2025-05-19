using System.ComponentModel.DataAnnotations;

namespace backend;

public class ReceivedData
{
    [Key]
    public string? Id { get; set; }
    public string DeviceId { get; set; }
    public string Data { get; set; }
}