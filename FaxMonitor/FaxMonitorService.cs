using FAXCOMEXLib;
using FaxMonitor.Data;
using Microsoft.EntityFrameworkCore;

namespace FaxMonitor;

public class FaxMonitorService : BackgroundService
{
    private const int PollingIntervalMs = 50;
    private readonly ILogger<FaxMonitorService> _logger;
    private readonly IDbContextFactory<FaxDbContext> _contextFactory;
    private FaxServer? _faxServer;
    private readonly Dictionary<int, string> _devices = new();
    private readonly SortedList<string, FaxOutgoingJob> _outgoingJobs = new();
    public FaxMonitorService(ILogger<FaxMonitorService> logger, IDbContextFactory<FaxDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_faxServer is null)
                    ConnectToFaxServer();
                else
                    CheckQueue();
                await Task.Delay(PollingIntervalMs, stoppingToken);
            }
            _logger.LogInformation("{time} Monitor App Stopped", DateTime.Now);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("{time} Monitor App Stopped", DateTime.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError("{time} Exception: {ex}, {message}", DateTime.Now, ex.ToString(), ex.Message);
            Environment.Exit(1);
        }
    }

    private void ConnectToFaxServer()
    {
        try
        {
            _faxServer = new FaxServer();                
            _faxServer.Connect(Environment.MachineName);                
            _logger.LogInformation("{time} Connected to Fax service", DateTime.Now);
            var security = _faxServer.Security2.GrantedRights;
            foreach (var enumVal in Enum.GetValues(typeof(FAX_ACCESS_RIGHTS_ENUM_2)))
            {
                var accessLevel = (FAX_ACCESS_RIGHTS_ENUM_2)enumVal;
                if ((security & accessLevel) > 0)
                {
                    _logger.LogInformation("{time} Process has {level} permission", DateTime.Now, accessLevel.ToString()[4..]);
                }
            }
            var devices = _faxServer.GetDevices();
            _logger.LogInformation("{time} Found {count} fax devices", DateTime.Now, devices.Count);                
            _devices.Clear();     
            foreach (var deviceObject in devices)
            {
                if (deviceObject is IFaxDevice device)
                {
                    _devices.Add(device.Id, device.DeviceName);
                    _logger.LogInformation("{time} Enumerated device {name}, id: {id}", DateTime.Now, device.DeviceName, device.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _faxServer = null;
            _logger.LogInformation("{time} Failure to connect to fax service. Error {ex}, {msg}", DateTime.Now, ex.ToString(), ex.Message);
            return;
        }
        RegisterFaxServerEvents();
    }    

    private void RegisterFaxServerEvents()
    {
        if (_faxServer == null)
        {
            _logger.LogInformation("{time} Unexpected null fax server during event registration", DateTime.Now);
            return;
        }
        _faxServer.OnServerShutDown += FaxServer_OnShutDown;
        _faxServer.OnIncomingJobAdded += FaxServer_OnIncomingJobAdded;
        _faxServer.OnIncomingJobRemoved += FaxServer_OnIncomingJobRemoved;
        _faxServer.OnIncomingJobChanged += FaxServer_OnIncomingJobChanged;
        _faxServer.ListenToServerEvents(FAX_SERVER_EVENTS_TYPE_ENUM.fsetIN_QUEUE | FAX_SERVER_EVENTS_TYPE_ENUM.fsetFXSSVC_ENDED);                    
    }

    private void UnRegisterFaxServerEvents()
    {
        if (_faxServer == null)
        {
            _logger.LogInformation("{time} Unexpected null fax server during event unregistration", DateTime.Now);
            return;
        }
        _faxServer.OnServerShutDown -= FaxServer_OnShutDown;
        _faxServer.OnIncomingJobAdded -= FaxServer_OnIncomingJobAdded;
        _faxServer.OnIncomingJobRemoved -= FaxServer_OnIncomingJobRemoved;
        _faxServer.OnIncomingJobChanged -= FaxServer_OnIncomingJobChanged;            
    }

    private void CheckQueue()
    {
        if (_faxServer is null) return;            
        var outQueue = _faxServer.Folders.OutgoingQueue;
        outQueue.Refresh();
        var jobQueue = outQueue.GetJobs().Cast<FaxOutgoingJob>().OrderBy(j => j.Id).ToList();
        var processedJobs = new SortedSet<string>();
        var closedJobs = new SortedList<string, FAX_JOB_STATUS_ENUM>();
        
        foreach (var job in jobQueue)
        {
            if (job.Status >= FAX_JOB_STATUS_ENUM.fjsRETRIES_EXCEEDED)
            {
                closedJobs.Add(job.Id, job.Status);
            }
            else if (_outgoingJobs.TryGetValue(job.Id, out var oldJob))
            {                    
                //existing job. see if there's any status change
                processedJobs.Add(job.Id);
                if (oldJob.Status == job.Status && oldJob.ExtendedStatusCode == job.ExtendedStatusCode && oldJob.CurrentPage == job.CurrentPage) continue;
                OnJobChanged(job.Id, job);
                var idx = _outgoingJobs.IndexOfKey(job.Id);
                _outgoingJobs.SetValueAtIndex(idx, job);
            }
            else
            {                
                processedJobs.Add(job.Id);
                OnJobAdded(job.Id, false, job.Sender.Name, job.SubmissionTime);
                OnJobChanged(job.Id, job);
                _outgoingJobs.Add(job.Id, job);
            }
        }
        //get a list of jobs not in the queue
        var removeJobs = _outgoingJobs.Keys.Except(processedJobs).ToList();
        //remove the jobs
        foreach (var job in removeJobs)
        {                
            if (closedJobs.TryGetValue(job, out var status))
                OnJobRemoved(job, status);
            else
                OnJobRemoved(job);
            _outgoingJobs.Remove(job);
        }
    }

    private void RegisterAccount(FaxAccount account)
    {
        //a user is allowed to listen only to their own account.
        //registering for events from other accounts will throw an exception
        if (account.AccountName == "CCCC\\tech")
            account.ListenToAccountEvents(FAX_ACCOUNT_EVENTS_TYPE_ENUM.faetOUT_QUEUE);
        account.OnOutgoingJobAdded += Account_OnOutgoingJobAdded;
        account.OnOutgoingJobChanged += Account_OnJobChanged;
        account.OnOutgoingJobRemoved += Account_OnJobRemoved;
        _logger.LogInformation("Found account {account}", account.AccountName);
    }

    private void UnregisterAccount(FaxAccount account)
    {
        account.OnOutgoingJobAdded -= Account_OnOutgoingJobAdded;
        account.OnOutgoingJobChanged -= Account_OnJobChanged;
        account.OnOutgoingJobRemoved -= Account_OnJobRemoved;
    }

    private void FaxServer_OnIncomingJobChanged(FaxServer pFaxServer, string bstrJobId, FaxJobStatus pJobStatus)
    {
        OnJobChanged(bstrJobId, pJobStatus);
    }

    private void FaxServer_OnIncomingJobRemoved(FaxServer pFaxServer, string bstrJobId)
    {
        OnJobRemoved(bstrJobId);
    }

    private void FaxServer_OnIncomingJobAdded(FaxServer pFaxServer, string bstrJobId)
    {
        OnJobAdded(bstrJobId, true, "Local");
    }

    private void Account_OnJobRemoved(FaxAccount pFaxAccount, string bstrJobId)
    {
        OnJobRemoved(bstrJobId);
    }      

    private void Account_OnJobChanged(FaxAccount pFaxAccount, string bstrJobId, FaxJobStatus pJobStatus)
    {
        OnJobChanged(bstrJobId, pJobStatus);
    }

    private void Account_OnOutgoingJobAdded(FaxAccount pFaxAccount, string bstrJobId)
    {
        OnJobAdded(bstrJobId, false, pFaxAccount.AccountName);
    }

    private void OnJobAdded(string bstrJobId, bool incoming, string user, DateTime? submissionTime = null)
    {
        submissionTime ??= DateTime.Now;
        using var db = _contextFactory.CreateDbContext();
        var job = new Job { ServerJobId = bstrJobId, Created = submissionTime.Value, Incoming = incoming, User = user };
        db.Job.Add(job);
        db.SaveChanges();
    }

    private void OnJobRemoved(string bstrJobId, FAX_JOB_STATUS_ENUM? status = null)
    {
        var closedDateTime = DateTime.Now;
        using var db = _contextFactory.CreateDbContext();
        var job = db.Job.FirstOrDefault(j => j.ServerJobId == bstrJobId);
        if (job != null)
        {
            job.Closed = closedDateTime;
            if (status != null)
                job.Status = status.Value.ToDbVal();
            else
            { 
                job.Status = "COMPLETED";
                var lastEvent = db.JobEvent.OrderByDescending(e => e.EventId).FirstOrDefault(e => e.JobId == job.Id);
                if (lastEvent != null)
                    job.Events.Add(new() { JobId = job.Id, DeviceName = lastEvent.DeviceName, EventDateTime = closedDateTime, CurrentPage = lastEvent.CurrentPage, 
                        Status = job.Status, ExtendedStatus = lastEvent.Status });
            }
            db.SaveChanges();
            _logger.LogInformation("{time} Job {id} closed with status {status}", DateTime.Now, bstrJobId, status?.ToDbVal() ?? "none");
        }
    }

    private void OnJobChanged(string bstrJobId, FaxJobStatus pJobStatus)
    {
        var deviceName = GetDeviceName(pJobStatus.DeviceId);
        var direction = "UNKNOWN FAX:";
        using var db = _contextFactory.CreateDbContext();
        var job = db.Job.FirstOrDefault(j => j.ServerJobId == bstrJobId);
        var status = pJobStatus.Status.ToDbVal();
        var extStatus = pJobStatus.ExtendedStatusCode.ToDbVal();
        if (job != null)
        {
            if (pJobStatus.DeviceId == 0)
            {
                //remember which device to blame for the current status
                var lastEvent = db.JobEvent.OrderByDescending(e => e.EventId).FirstOrDefault(e => e.JobId == job.Id);
                if (lastEvent != null)
                    deviceName = lastEvent.DeviceName;
            }
            job.CSID = pJobStatus.CSID?.Trim();
            job.TSID = pJobStatus.TSID?.Trim();
            job.PageTotal = job.Incoming ? pJobStatus.CurrentPage : pJobStatus.Pages;
            job.Status = status;
            job.ExtendedStatus = extStatus;
            job.Events.Add(new()
            {
                CurrentPage = pJobStatus.CurrentPage,
                DeviceName = deviceName,
                EventDateTime = DateTime.Now,
                Status = status,
                ExtendedStatus = extStatus
            }); 
            db.SaveChanges();
            direction = job.Incoming ? "INCOMING FAX:" : "OUTGOING FAX:";
        }

        _logger.LogInformation("{time} {direction} Device {deviceName} Job {jobId} TSID: {tsid}, CSID: {csid}, page: {page}/{totalpages} status {status}, ext {extended}",
            DateTime.Now, direction, deviceName, bstrJobId, pJobStatus.TSID, pJobStatus.CSID?.TrimEnd(), pJobStatus.CurrentPage, pJobStatus.Pages, status, extStatus);
    }

    private void OnJobChanged(string bstrJobId, FaxOutgoingJob pJob)
    {
        if (pJob is null)
        {
            _logger.LogError("{time} Unexpected null for job {id}", DateTime.Now, bstrJobId);
            return;
        }
        var deviceName = GetDeviceName(pJob.DeviceId);
        using var db = _contextFactory.CreateDbContext();
        var job = db.Job.FirstOrDefault(j => j.ServerJobId == bstrJobId);
        var status = pJob.Status.ToDbVal();
        var extStatus = pJob.ExtendedStatusCode.ToDbVal();
        if (job != null)
        {
            if (pJob.DeviceId == 0)
            {
                var lastEvent = db.JobEvent.OrderByDescending(e => e.EventId).FirstOrDefault(e => e.JobId == job.Id);
                if (lastEvent != null)
                    deviceName = lastEvent.DeviceName;
            }
            job.CSID = pJob.CSID?.Trim();
            job.TSID = pJob.TSID?.Trim();
            job.PageTotal =pJob.Pages;
            job.Status = status;
            job.ExtendedStatus = extStatus;
            job.Events.Add(new()
            {
                CurrentPage = pJob.CurrentPage,
                DeviceName = deviceName,
                EventDateTime = DateTime.Now,
                Status = status,
                ExtendedStatus = extStatus
            });
            _logger.LogInformation("{time} OUTGOING FAX: Device {deviceName} Job {jobId} TSID: {tsid}, CSID: {csid}, page: {page}/{totalpages} status {status}, ext {extended}",
                DateTime.Now, deviceName, bstrJobId, pJob.TSID, pJob.CSID?.TrimEnd(), pJob.CurrentPage, pJob.Pages, status, extStatus);

            db.SaveChanges();
        }
    }

    private void FaxServer_OnShutDown(FaxServer pFaxServer)
    {
        try
        {
            if (_faxServer == null)
            {
                _logger.LogInformation("{time} Unexpected null fax service during server shutdown", DateTime.Now);
                return;
            }
            _logger.LogInformation("{time} Fax service shutting down", DateTime.Now);
            UnRegisterFaxServerEvents();
            _faxServer.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogError("{time} Error on shutdown: {ex}, {msg}", DateTime.Now, ex.ToString(), ex.Message);
        }
        finally
        {
            _faxServer = null;
            _logger.LogInformation("{time} Fax monitoring going dormant", DateTime.Now);
        }
    }

    private string GetDeviceName(int deviceId)
    {
        if (deviceId == 0)
        {
            //job status change with no associated device
            return "(No Device)";
        }
        if (_faxServer == null)
        {
            _logger.LogInformation("{time} Unexpected null fax service during job changed", DateTime.Now);
            return "(Failed to get fax service)";
        }
        if (!_devices.TryGetValue(deviceId, out var deviceName))
        {
            var devices = _faxServer.GetDevices();
            try
            {
                var device = devices.ItemById[deviceId];
                deviceName = device.DeviceName;
            }
            catch (ArgumentException)
            {
                _logger.LogInformation("{time} Unable to get fax device with Id {id}", DateTime.Now, deviceId);
                return $"(Failed to get from id {deviceId})";
            }

        }
        return deviceName;
    }
}

public static class StatusExtensions
{
public static string ToDbVal(this FAX_JOB_STATUS_ENUM value)
{
    string status = value.ToString();
    if (status == "96") return "NOLINE,RETRYING"; // yeah, this happens.
    if (status == "33") return "NOLINE,PENDING"; // this too.
    if (status == "80") return "PAUSED,RETRYING"; // ??
    return status.Length > 3 ? status[3..] : status;
}

public static string ToDbVal(this FAX_JOB_EXTENDED_STATUS_ENUM value)
{
    var status = value.ToString();
    return status.Length > 4 ? status[4..] : status;
}
}