using System;
using Godot;

public static class Logger
{
    public static void Log(LogLevel level, params object[] message)
    {
        // DEBUG is development-only noise; keep release consoles clean.
        if (level == LogLevel.DEBUG && !OS.IsDebugBuild())
            return;

        var dateTime = DateTime.Now;
        var timestamp = $"[{dateTime:yyyy-MM-dd HH:mm:ss}]";

        // Stack inspection is best-effort: inlined/optimised frames can be
        // missing, and logging must never throw while reporting something else.
        string typeName = "?";
        string methodName = "?";
        try
        {
            var frame = new System.Diagnostics.StackTrace().GetFrame(2);
            var callingMethod = frame?.GetMethod();
            typeName = callingMethod?.DeclaringType?.Name ?? "?";
            methodName = callingMethod?.Name ?? "?";
        }
        catch
        {
            // fall through with placeholders
        }

        string logMessage = $"{timestamp} [{level}] [{typeName}] [{methodName}] ";
        string color = level switch
        {
            LogLevel.DEBUG => "WHITE",
            LogLevel.INFO => "CYAN",
            LogLevel.WARNING => "YELLOW",
            LogLevel.ERROR => "MAROON",
            _ => "WHITE"
        };

        GD.PrintRich([$"[color={color}]{logMessage}[/color]", .. message]);
    }

    public static void Debug(params object[] message)
    {
        Log(LogLevel.DEBUG, message);
    }

    public static void Info(params object[] message)
    {
        Log(LogLevel.INFO, message);
    }

    public static void Warning(params object[] message)
    {
        Log(LogLevel.WARNING, message);
    }

    public static void Error(params object[] message)
    {
        Log(LogLevel.ERROR, message);
    }
}