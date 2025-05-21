using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend.Entities;

public partial class Storeddatum
{
    [Key] // ← ensures EF recognizes it as the primary key
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // ← ensures it's auto-generated
    public string Id { get; set; } = null!;

    public string? Deviceid { get; set; }

    public string? Linktopicture { get; set; }

    public DateTime? Date { get; set; }
}
