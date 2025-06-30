using System.Text.RegularExpressions;

namespace Shooter.Tests.Infrastructure;

/// <summary>
/// Utility class for analyzing log files in tests.
/// Provides grep-like functionality for finding patterns in logs.
/// </summary>
public class LogAnalyzer
{
    private readonly string _logDirectory;
    
    public LogAnalyzer(string logDirectory)
    {
        _logDirectory = logDirectory;
    }
    
    /// <summary>
    /// Waits for a specific log entry to appear in a log file.
    /// </summary>
    public async Task<LogEntry?> WaitForLogEntry(
        string logFileName, 
        string pattern, 
        TimeSpan? timeout = null,
        bool useRegex = false)
    {
        var timeoutTime = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var logPath = Path.Combine(_logDirectory, logFileName);
        
        while (DateTime.UtcNow < timeoutTime)
        {
            if (File.Exists(logPath))
            {
                var entries = await ReadLogEntries(logPath);
                var match = FindMatch(entries, pattern, useRegex);
                
                if (match != null)
                    return match;
            }
            
            await Task.Delay(100);
        }
        
        return null;
    }
    
    /// <summary>
    /// Waits for multiple log entries across different files.
    /// </summary>
    public async Task<Dictionary<string, LogEntry>> WaitForLogEntries(
        Dictionary<string, string> filePatterns,
        TimeSpan? timeout = null)
    {
        var results = new Dictionary<string, LogEntry>();
        var timeoutTime = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        
        while (DateTime.UtcNow < timeoutTime && results.Count < filePatterns.Count)
        {
            foreach (var kvp in filePatterns)
            {
                if (results.ContainsKey(kvp.Key))
                    continue;
                    
                var logPath = Path.Combine(_logDirectory, kvp.Key);
                if (File.Exists(logPath))
                {
                    var entries = await ReadLogEntries(logPath);
                    var match = FindMatch(entries, kvp.Value, false);
                    
                    if (match != null)
                        results[kvp.Key] = match;
                }
            }
            
            if (results.Count < filePatterns.Count)
                await Task.Delay(100);
        }
        
        return results;
    }
    
    /// <summary>
    /// Gets all log entries from a file that match a pattern.
    /// </summary>
    public async Task<List<LogEntry>> GetMatchingEntries(
        string logFileName,
        string pattern,
        bool useRegex = false)
    {
        var logPath = Path.Combine(_logDirectory, logFileName);
        if (!File.Exists(logPath))
            return new List<LogEntry>();
            
        var entries = await ReadLogEntries(logPath);
        return entries.Where(e => IsMatch(e, pattern, useRegex)).ToList();
    }
    
    /// <summary>
    /// Counts occurrences of a pattern across all log files.
    /// </summary>
    public async Task<Dictionary<string, int>> CountOccurrences(
        string pattern,
        bool useRegex = false)
    {
        var results = new Dictionary<string, int>();
        var logFiles = Directory.GetFiles(_logDirectory, "*.log");
        
        foreach (var logFile in logFiles)
        {
            var fileName = Path.GetFileName(logFile);
            var entries = await ReadLogEntries(logFile);
            var count = entries.Count(e => IsMatch(e, pattern, useRegex));
            
            if (count > 0)
                results[fileName] = count;
        }
        
        return results;
    }
    
    /// <summary>
    /// Extracts chat messages from log entries.
    /// </summary>
    public async Task<List<ChatMessageLog>> ExtractChatMessages(string logFileName)
    {
        var messages = new List<ChatMessageLog>();
        var entries = await GetMatchingEntries(logFileName, "chat message", false);
        
        foreach (var entry in entries)
        {
            var match = Regex.Match(entry.Message, @"Received chat message from (.+?): (.+)");
            if (match.Success)
            {
                messages.Add(new ChatMessageLog
                {
                    Timestamp = entry.Timestamp,
                    Sender = match.Groups[1].Value,
                    Message = match.Groups[2].Value,
                    LogFile = logFileName
                });
            }
        }
        
        return messages;
    }
    
    private async Task<List<LogEntry>> ReadLogEntries(string logPath)
    {
        var entries = new List<LogEntry>();
        
        using var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var entry = ParseLogLine(line);
            if (entry != null)
                entries.Add(entry);
        }
        
        return entries;
    }
    
    private LogEntry? ParseLogLine(string line)
    {
        // Expected format: 2024-01-15 10:30:45.123 [INFO ] Category: Message
        var match = Regex.Match(line, @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w+)\s*\] (.+?): (.+)$");
        
        if (match.Success)
        {
            return new LogEntry
            {
                Timestamp = DateTime.Parse(match.Groups[1].Value),
                Level = match.Groups[2].Value.Trim(),
                Category = match.Groups[3].Value,
                Message = match.Groups[4].Value
            };
        }
        
        return null;
    }
    
    private LogEntry? FindMatch(List<LogEntry> entries, string pattern, bool useRegex)
    {
        return entries.FirstOrDefault(e => IsMatch(e, pattern, useRegex));
    }
    
    private bool IsMatch(LogEntry entry, string pattern, bool useRegex)
    {
        if (useRegex)
        {
            return Regex.IsMatch(entry.Message, pattern, RegexOptions.IgnoreCase);
        }
        else
        {
            return entry.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ChatMessageLog
{
    public DateTime Timestamp { get; set; }
    public string Sender { get; set; } = "";
    public string Message { get; set; } = "";
    public string LogFile { get; set; } = "";
}