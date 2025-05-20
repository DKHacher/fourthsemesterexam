using System;
using System.Collections.Generic;

namespace backend.Entities;

public partial class Storeddatum
{
    public string Id { get; set; } = null!;

    public string? Deviceid { get; set; }

    public string? Linktopicture { get; set; }

    public DateTime? Date { get; set; }
}
