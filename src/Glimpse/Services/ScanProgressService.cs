namespace Glimpse.Services;

public class ScanProgressService
{
    public int TotalFiles { get; set; }
    public int _processedFiles;
    public int ProcessedFiles 
    { 
        get => _processedFiles; 
        set => _processedFiles = value; 
    }
    public volatile bool IsScanning;
    public string? CurrentFile { get; set; }

    public int RemainingFiles => TotalFiles - ProcessedFiles;
    public int PercentComplete => TotalFiles > 0 ? (int)(ProcessedFiles * 100.0 / TotalFiles) : 100;
}
