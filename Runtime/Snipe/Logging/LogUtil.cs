using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniIT.Snipe.Logging
{
    public static class LogUtil
    {
	    public static string GetReducedException(Exception ex)
	    {
		    var summaries = new List<string>();
		    GetExceptionSummaries(ex, summaries);
		    return string.Join(" ---> ", summaries);
	    }

	    private static void GetExceptionSummaries(Exception ex, List<string> summaries)
	    {
		    if (ex is AggregateException aggEx)
		    {
			    foreach (var inner in aggEx.Flatten().InnerExceptions)
			    {
				    GetExceptionSummaries(inner, summaries);
			    }
		    }
		    else
		    {
			    var type = ex.GetType().Name;
			    var message = ex.Message;

			    // Get the first line of the stack trace if available
			    var firstStackLine = ex.StackTrace?
				    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				    .FirstOrDefault();

			    var summary = $"{type}: {message}";
			    if (!string.IsNullOrWhiteSpace(firstStackLine))
			    {
				    summary += $" | at {firstStackLine.Trim()}";
			    }

			    summaries.Add(summary);

			    if (ex.InnerException != null)
			    {
				    GetExceptionSummaries(ex.InnerException, summaries);
			    }
		    }
	    }

    }
}
