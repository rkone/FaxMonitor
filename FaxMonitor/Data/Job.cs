using FAXCOMEXLib;

namespace FaxMonitor.Data;

public class Job
{
    public int Id { get; set; }
    public string ServerJobId { get; set; } = string.Empty;
    public bool Incoming { get; set; }
    public string? TSID { get; set; }
    public string? CSID { get; set; }
    public int PageTotal { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Closed { get; set; }
    public string? Status { get; set; }
    public string? ExtendedStatus { get; set; }
    public string User { get; set; } = string.Empty;

    public List<JobEvent> Events { get; set; } = new();

}
