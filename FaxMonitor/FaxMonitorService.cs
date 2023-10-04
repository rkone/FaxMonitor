using FAXCOMEXLib;
using FaxMonitor.Data;
using Microsoft.EntityFrameworkCore;

namespace FaxMonitor
{
    public class FaxMonitorService : BackgroundService
    {
        private readonly ILogger<FaxMonitorService> _logger;
        private readonly IDbContextFactory<FaxDbContext> _contextFactory;
        private FaxServer? _faxServer;
        private FaxAccounts? _faxAccounts;
        private readonly Dictionary<int, string> _devices = new();       

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
                    await Task.Delay(1000, stoppingToken);
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
            _faxServer.OnOutgoingMessageAdded += FaxServer_OnOutgoingMessageAdded;
            _faxServer.OnOutgoingMessageRemoved += FaxServer_OnOutgoingMessageRemoved;
            _faxServer.ListenToServerEvents(FAX_SERVER_EVENTS_TYPE_ENUM.fsetIN_QUEUE | FAX_SERVER_EVENTS_TYPE_ENUM.fsetOUT_ARCHIVE
                | FAX_SERVER_EVENTS_TYPE_ENUM.fsetFXSSVC_ENDED);
            _faxAccounts = _faxServer.FaxAccountSet.GetAccounts();
            foreach (var item in _faxAccounts)
            {                
                if (item is FaxAccount account)
                {
                    RegisterAccount(account);
                }
            }                                  
        }

        private void FaxServer_OnOutgoingMessageRemoved(FaxServer pFaxServer, string bstrMessageId)
        {
            _logger.LogInformation("{time} Outgoing Message {msg} Removed", DateTime.Now, bstrMessageId);
        }

        private void FaxServer_OnOutgoingMessageAdded(FaxServer pFaxServer, string bstrMessageId)
        {
            _logger.LogInformation("{time} Outgoing Message {msg} Added", DateTime.Now, bstrMessageId);
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
            _faxServer.OnOutgoingMessageAdded -= FaxServer_OnOutgoingMessageAdded;
            _faxServer.OnOutgoingMessageRemoved -= FaxServer_OnOutgoingMessageRemoved;

            if (_faxAccounts == null)
            {
                _logger.LogInformation("{time} Unexpected null fax accounts during event unregistration", DateTime.Now);
                return;
            }
            foreach (var item in _faxAccounts)
            {
                if (item is FaxAccount account)
                {
                    UnregisterAccount(account);
                }
            }
        }

        private void RegisterAccount(FaxAccount account)
        {
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

        private void OnJobAdded(string bstrJobId, bool incoming, string user)
        {
            using var db = _contextFactory.CreateDbContext();
            var job = new Job { ServerJobId = bstrJobId, Created = DateTime.Now, Incoming = incoming, User = user };
            db.Job.Add(job);
            db.SaveChanges();
        }

        private void OnJobRemoved(string bstrJobId)
        {
            using var db = _contextFactory.CreateDbContext();
            var job = db.Job.FirstOrDefault(j => j.ServerJobId == bstrJobId);
            if (job != null)
            {
                job.Closed = DateTime.Now;
                db.SaveChanges();
            }
        }

        private void OnJobChanged(string bstrJobId, FaxJobStatus pJobStatus)
        {
            var deviceName = GetDeviceName(pJobStatus.DeviceId);
            var direction = "UNKNOWN FAX:";
            using var db = _contextFactory.CreateDbContext();
            var job = db.Job.FirstOrDefault(j => j.ServerJobId == bstrJobId);
            if (job != null)
            {
                job.CSID = pJobStatus.CSID.Trim();
                job.TSID = pJobStatus.TSID.Trim();
                job.PageTotal = job.Incoming ? pJobStatus.CurrentPage : pJobStatus.Pages;
                job.Status = pJobStatus.Status.ToString()[3..];
                job.ExtendedStatus = pJobStatus.ExtendedStatusCode.ToString()[4..];
                job.Events.Add(new()
                {
                    CurrentPage = pJobStatus.CurrentPage,
                    DeviceName = deviceName,
                    EventDateTime = DateTime.Now,
                    Status = pJobStatus.Status.ToString()[3..],
                    ExtendedStatus = pJobStatus.ExtendedStatusCode.ToString()[4..]
                });
                db.SaveChanges();
                direction = job.Incoming ? "INCOMING FAX:" : "OUTGOING FAX:";
            }

            _logger.LogInformation("{time} {direction} Device {deviceName} Job {jobId} TSID: {tsid}, CSID: {csid}, page: {page}/{totalpages} status {status}, ext {extended}",
                DateTime.Now, direction, deviceName, bstrJobId, pJobStatus.TSID, pJobStatus.CSID.TrimEnd(), pJobStatus.CurrentPage, pJobStatus.Pages,
                pJobStatus.Status.ToString().Remove(0, 3), pJobStatus.ExtendedStatusCode.ToString().Remove(0, 4));
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
}