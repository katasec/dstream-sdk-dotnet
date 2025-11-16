namespace Katasec.DStream.Abstractions;

// ---------- Command envelope for lifecycle operations ----------
public class CommandEnvelope<TConfig> where TConfig : class
{
    public string Command { get; set; } = "run";
    public TConfig? Config { get; set; }
}
