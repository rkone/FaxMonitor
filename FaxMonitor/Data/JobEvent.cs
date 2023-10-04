using FAXCOMEXLib;
using System.ComponentModel.DataAnnotations;

namespace FaxMonitor.Data;

public class JobEvent
{
    [Key]
    public int EventId { get; set; }
    public int JobId { get; set; }
    public Job? Job { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public DateTime EventDateTime { get; set; }
    public int CurrentPage { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ExtendedStatus { get; set; } = string.Empty;
}
