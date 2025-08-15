using System.Diagnostics;
using System.Security.Principal;

namespace UsnJrnl2Evtx;

public static class EventLogSetup
{
    public static void EnsureEventSource(string sourceName, string logName)
    {
        if (EventLog.SourceExists(sourceName))
        {
            String currentLog = EventLog.LogNameFromSourceName(sourceName, ".");
            if (!string.Equals(currentLog, logName, StringComparison.OrdinalIgnoreCase))
            {
                // Source exists but points to a different log; re-map (requires admin)
                EventLog.DeleteEventSource(sourceName);
                EventLog.CreateEventSource(new EventSourceCreationData(sourceName, logName));
            }
        }
        else
        {
            EventLog.CreateEventSource(new EventSourceCreationData(sourceName, logName));
        }
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}