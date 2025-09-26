using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;


internal class MSBuildLoggerProvider : ILoggerProvider
{
    private readonly TaskLoggingHelper _loggingHelper;

    public MSBuildLoggerProvider(TaskLoggingHelper loggingHelperToWrap)
    {
        _loggingHelper = loggingHelperToWrap;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new MSBuildLogger(categoryName, _loggingHelper);
    }

    public void Dispose() { }
}
