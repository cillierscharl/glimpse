namespace Glimpse.Services;

public class ScanProgressService
{
    public int TotalFiles { get; set; }
    public int AlreadyIndexed { get; set; }
    public int _processedFiles;
    public int ProcessedFiles 
    { 
        get => _processedFiles; 
        set => _processedFiles = value; 
    }
    public volatile bool IsScanning;
    public string? CurrentFile { get; set; }

    public int TotalIndexed => AlreadyIndexed + ProcessedFiles;
    public int RemainingFiles => TotalFiles - TotalIndexed;
    public int PercentComplete => TotalFiles > 0 ? (int)(TotalIndexed * 100.0 / TotalFiles) : 100;
}
