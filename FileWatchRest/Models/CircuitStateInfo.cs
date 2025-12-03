namespace FileWatchRest.Models;

public class CircuitStateInfo {
    public string Endpoint { get; set; } = string.Empty;
    public int Failures { get; set; }
    public DateTimeOffset? OpenUntil { get; set; }
    public bool IsOpen => OpenUntil > DateTimeOffset.Now;
}
