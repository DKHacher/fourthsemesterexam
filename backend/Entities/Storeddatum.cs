using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Entities;

public partial class Storeddatum
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] 
    public string Id { get; set; } = null!;

    public string? Deviceid { get; set; }

    public string? Linktopicture { get; set; }

    public DateTime? Date { get; set; }
}
