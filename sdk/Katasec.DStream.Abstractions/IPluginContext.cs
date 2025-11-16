namespace Katasec.DStream.Abstractions;

// ---------- Runtime context & event model ----------
public interface IPluginContext
{
    // Simple logger for stdin/stdout mode
    object Logger { get; }
}
