namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Service to export completed task execution transcripts from the database
/// into standard datasets (SFT and DPO) for LLM fine-tuning.
/// </summary>
public interface ITranscriptExportService
{
    /// <summary>
    /// Exports all successfully completed tasks as SFT training data in OpenAI chat JSONL format.
    /// </summary>
    Task<string> ExportSftJsonlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports DPO preference pairs (chosen vs rejected) for tasks where an initial attempt failed.
    /// </summary>
    Task<string> ExportDpoJsonlAsync(CancellationToken cancellationToken = default);
}
