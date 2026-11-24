using System.Security.Cryptography;
using System.Text.Json;
using Attestia.Core.Errors;
using Attestia.Core.Importing;
using Attestia.Core.Time;
using Attestia.Core.Xml;
using Attestia.Data.Repositories;

namespace Attestia.Api.XmlExchange;

public sealed record ImportReport
{
    public int? ImportId { get; init; }
    public required bool DryRun { get; init; }
    public required string FileName { get; init; }
    public required string Sha256 { get; init; }
    public string? ExportId { get; init; }
    public required int TotalRows { get; init; }
    public required int OkRows { get; init; }
    public required int RejectedRows { get; init; }
    public required IReadOnlyList<ImportRowResult> Rows { get; init; }
}

/// <summary>Il file non rispetta l'XSD: viene rifiutato per intero (nessuna riga salvata).</summary>
public sealed class SchemaValidationException(IReadOnlyList<XmlIssue> issues) : Exception("Il file non rispetta lo schema di import.")
{
    public IReadOnlyList<XmlIssue> Issues { get; } = issues;
}

/// <summary>
/// Orchestrazione dell'import: SHA-256 → rifiuto se gia' importato → parsing sicuro + XSD →
/// validazione di dominio per riga → salvataggio in un'unica transazione (righe valide + log).
/// Se un'altra transazione inserisce gli stessi attestati nel frattempo, la validazione viene ripetuta.
/// </summary>
public sealed partial class ImportService(IImportRepository imports, IReportRepository reports, IBusinessClock clock, ILogger<ImportService> logger)
{
    public const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions ErrorsJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImportReport> ImportAsync(byte[] content, string fileName, string importedBy, bool dryRun, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var safeName = SanitizeFileName(fileName);

        if (await imports.GetBySha256Async(sha256, ct) is { } previous)
        {
            throw new ConflictException(
                $"Questo file è già stato importato il {previous.ImportedAt:dd/MM/yyyy HH:mm} (UTC) da {previous.ImportedBy} (import n. {previous.Id}). Nessuna riga è stata salvata.");
        }

        using var stream = new MemoryStream(content, writable: false);
        var parsed = CompletionXmlParser.Parse(stream, MaxFileBytes);
        if (!parsed.IsValid)
        {
            throw new SchemaValidationException(parsed.Errors);
        }

        var warningDays = await reports.ExpiringSoonDaysAsync(ct);
        for (var attempt = 1; ; attempt++)
        {
            var refs = await imports.PrepareAsync(parsed.Rows, ct);
            var plan = ImportValidator.Validate(parsed.Rows, refs, clock.Today, warningDays);
            var rejected = plan.Rejected.ToList();
            var accepted = plan.Accepted.ToList();

            if (dryRun)
            {
                return Report(null, true, safeName, sha256, parsed, plan);
            }

            var errorsJson = rejected.Count == 0
                ? null
                : JsonSerializer.Serialize(rejected.Select(r => new { line = r.LineNumber, r.EmployeeNumber, r.CourseCode, r.CompletedOn, reason = r.Reason }), ErrorsJsonOptions);

            try
            {
                var log = await imports.SaveAsync(new ImportSaveRequest(safeName, sha256, importedBy, plan.Rows.Count, errorsJson, accepted), ct);
                Imported(logger, log.Id, log.TotalRows, log.OkRows, log.RejectedRows);
                return Report(log.Id, false, safeName, sha256, parsed, plan);
            }
            catch (ImportRaceException) when (attempt < MaxAttempts)
            {
                RetryingAfterRace(logger, attempt);
            }
        }
    }

    private static ImportReport Report(int? id, bool dryRun, string fileName, string sha256, CompletionParseResult parsed, ImportPlan plan) => new()
    {
        ImportId = id,
        DryRun = dryRun,
        FileName = fileName,
        Sha256 = sha256,
        ExportId = parsed.ExportId,
        TotalRows = plan.Rows.Count,
        OkRows = plan.Accepted.Count(),
        RejectedRows = plan.Rejected.Count(),
        Rows = plan.Rows,
    };

    /// <summary>Solo il nome (niente percorsi), senza caratteri di controllo, al massimo 200 caratteri.</summary>
    internal static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "import.xml";
        }

        return name.Length > 200 ? name[..200] : name;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import {ImportId} completato: {Total} righe, {Ok} importate, {Rejected} scartate")]
    private static partial void Imported(ILogger logger, int importId, int total, int ok, int rejected);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conflitto concorrente durante l'import (tentativo {Attempt}): ripeto la validazione")]
    private static partial void RetryingAfterRace(ILogger logger, int attempt);
}
