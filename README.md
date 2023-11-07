# FaxMonitor
Uses FAXCOMEXLib to monitor a server's incoming and outgoing fax jobs.  Stores results to a SQLite database.  Has a few views to see result stats based on device, day, recipient.
## Note:
While incoming faxes use events and log each change, FAXCOMEXLib does not allow subscription to (other user's) outgoing events.  So this uses the very inefficient method of polling the outbound queue and checking each job for a status change. This results in a few results being missed (mostly busy signals). Depending on your fax volume you may want to reduce "Automatically delete faxes older than" option to lighten the polling load.
