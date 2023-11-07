using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaxMonitor.Migrations
{
    /// <inheritdoc />
    public partial class CreateViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE VIEW ResultsByDeviceAndDate AS
SELECT SUBSTR(j.Created, 0, 11) AS CreatedDate, e.DeviceName, SUM(CASE WHEN e.extendedstatus='DIALING' THEN 1 ELSE 0 END) AS Calls, 
SUM(CASE WHEN (e.Status='RETRYING' AND e.ExtendedStatus='BUSY') OR (j.Status='RETRIES_EXCEEDED' AND e.EventDateTime = le.LastEvent AND e.ExtendedStatus = 'DIALING') THEN 1 ELSE 0 END) AS Busy, 
SUM(CASE WHEN e.Status='RETRYING' AND e.ExtendedStatus='NO_ANSWER' THEN 1 ELSE 0 END) AS NoAnswer, 
SUM(CASE WHEN (e.Status='RETRYING' AND e.ExtendedStatus IN ('DISCONNECTED','FATAL_ERROR')) 
	OR (j.Status='RETRIES_EXCEEDED' AND e.EventDateTime = le.LastEvent AND e.ExtendedStatus <> 'DIALING') THEN 1 ELSE 0 END) AS Failed, 
SUM(CASE WHEN e.Status='COMPLETED' THEN 1 ELSE 0 END) AS Completed, 
ROUND(CAST(SUM(CASE WHEN e.Status='COMPLETED' THEN e.CurrentPage ELSE 0 END) AS REAL) / CAST (SUM(CASE WHEN e.Status='COMPLETED' THEN 1 ELSE 0 END) AS REAL),2) AS AvgPages 
FROM Job j LEFT OUTER JOIN JobEvent e ON j.id=e.JobId 
LEFT OUTER JOIN (SELECT JobId, MAX(EventDateTime) AS LastEvent FROM JobEvent GROUP BY JobId) le ON j.Id = le.JobId
WHERE j.incoming=0 AND e.ExtendedStatus<> 'NOLINE,RETRYING'								
GROUP BY SUBSTR(j.Created, 0, 11), e.DeviceName
HAVING SUM(CASE WHEN e.extendedstatus='DIALING' THEN 1 ELSE 0 END) > 0
ORDER BY SUBSTR(j.Created, 0, 11), e.DeviceName");

            migrationBuilder.Sql(@"CREATE VIEW ResultsByDeviceAndRecipient AS
SELECT LTRIM(REPLACE(RecipientNumber,'-',''),1) AS Recipient, e.DeviceName, SUM(CASE WHEN e.extendedstatus='DIALING' THEN 1 ELSE 0 END) AS Calls, 
SUM(CASE WHEN (e.Status='RETRYING' AND e.ExtendedStatus='BUSY') OR (j.Status='RETRIES_EXCEEDED' AND e.EventDateTime = le.LastEvent AND e.ExtendedStatus = 'DIALING') THEN 1 ELSE 0 END) AS Busy, 
SUM(CASE WHEN e.Status='RETRYING' AND e.ExtendedStatus='NO_ANSWER' THEN 1 ELSE 0 END) AS NoAnswer, 
SUM(CASE WHEN (e.Status='RETRYING' AND e.ExtendedStatus IN ('DISCONNECTED','FATAL_ERROR'))
	OR (j.Status='RETRIES_EXCEEDED' AND e.EventDateTime = le.LastEvent AND e.ExtendedStatus <> 'DIALING') THEN 1 ELSE 0 END) AS Failed, 
SUM(CASE WHEN e.Status='COMPLETED' THEN 1 ELSE 0 END) AS Completed, 
ROUND(CAST(SUM(CASE WHEN e.Status='COMPLETED' THEN e.CurrentPage ELSE 0 END) AS REAL) / CAST (SUM(CASE WHEN e.Status='COMPLETED' THEN 1 ELSE 0 END) AS REAL),2) AS AvgPages 
FROM Job j LEFT OUTER JOIN JobEvent e ON j.id=e.JobId 
LEFT OUTER JOIN (SELECT JobId, MAX(EventDateTime) AS LastEvent FROM JobEvent GROUP BY JobId) le ON j.Id = le.JobId
WHERE j.incoming=0
GROUP BY e.DeviceName, LTRIM(REPLACE(RecipientNumber,'-',''),1)
HAVING SUM(CASE WHEN e.extendedstatus='DIALING' THEN 1 ELSE 0 END) > 0
ORDER BY SUM(CASE WHEN e.Status='RETRYING' AND e.ExtendedStatus IN ('DISCONNECTED','FATAL_ERROR') THEN 1 ELSE 0 END) DESC");

            migrationBuilder.Sql(@"CREATE VIEW ActiveJobs AS
SELECT j.ServerJobId, j.RecipientNumber, j.TSID, J.CSID, j.Status, j.ExtendedStatus, e.*	 from job j INNER JOIN JobEvent e ON j.id=e.JobId 
WHERE j.Status NOT IN ('COMPLETED', 'RETRYING', 'NOLINE,RETRYING', 'RETRIES_EXCEEDED') AND j.Status <> 'NOLINE,PENDING' AND created > '2023-10-17'
ORDER BY j.ServerJobId, e.EventDateTime DESC");

            migrationBuilder.Sql(@"CREATE VIEW TotalJobsByMonth AS
SELECT SUBSTR(Created, 0, 8) AS Month, SUM(Incoming) AS Incoming, SUM(CASE WHEN Incoming=0 THEN 1 ELSE 0 END) AS Outgoing, COUNT(*) AS Total FROM Job -- WHERE Incoming=1
GROUP BY SUBSTR(Created, 0, 8)");
        }
    

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW TotalJobsByMonth");
            migrationBuilder.Sql("DROP VIEW ActiveJobs");
            migrationBuilder.Sql("DROP VIEW ResultsByDeviceAndDate");
            migrationBuilder.Sql("DROP VIEW ResultsByDeviceAndRecipient");
        }
    }
}
