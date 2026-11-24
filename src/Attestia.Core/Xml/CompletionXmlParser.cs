using System.Globalization;
using System.Xml;
using System.Xml.Schema;

namespace Attestia.Core.Xml;

/// <summary>Una riga <c>&lt;completion&gt;</c> del file, con il numero di riga di origine.</summary>
public sealed record ParsedCompletion(int LineNumber, string EmployeeNumber, string CourseCode, DateOnly CompletedOn, decimal? Score);

public sealed record XmlIssue(int LineNumber, int LinePosition, string Message);

public sealed record CompletionParseResult(
    string? ExportId,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<ParsedCompletion> Rows,
    IReadOnlyList<XmlIssue> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Parser in streaming e sicuro per il formato <c>urn:attestia:import:v1</c>.
/// Difese: DTD vietati (niente XXE ne' "billion laughs"), nessun resolver (niente accesso a file o rete),
/// nessuno schema dichiarato nel documento (si valida SOLO contro l'XSD incorporato), limite di dimensione
/// e di profondita'. Gli errori di schema sono raccolti tutti, con riga e colonna.
/// </summary>
public static class CompletionXmlParser
{
    public const long DefaultMaxCharacters = 2 * 1024 * 1024;
    public const int MaxDepth = 8;

    public static CompletionParseResult Parse(Stream xml, long maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var errors = new List<XmlIssue>();
        var rows = new List<ParsedCompletion>();
        string? exportId = null;
        DateTimeOffset? generatedAt = null;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = maxCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            ValidationType = ValidationType.Schema,
            // Volutamente NON ProcessInlineSchema / ProcessSchemaLocation: il file non sceglie lo schema.
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
            Schemas = XmlSchemas.Import,
            CloseInput = false,
        };
        settings.ValidationEventHandler += (_, e) =>
            errors.Add(new XmlIssue(e.Exception.LineNumber, e.Exception.LinePosition, e.Message));

        try
        {
            using var reader = XmlReader.Create(xml, settings);
            var lineInfo = (IXmlLineInfo)reader;

            while (reader.Read())
            {
                if (reader.Depth > MaxDepth)
                {
                    errors.Add(new XmlIssue(lineInfo.LineNumber, lineInfo.LinePosition, $"Profondità massima ({MaxDepth}) superata."));
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != XmlSchemas.ImportNamespace)
                {
                    continue;
                }

                if (reader is { LocalName: "completions", Depth: 0 })
                {
                    exportId = reader.GetAttribute("exportId");
                    generatedAt = DateTimeOffset.TryParse(reader.GetAttribute("generatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var g) ? g : null;
                }
                else if (reader is { LocalName: "completion", Depth: 1 })
                {
                    var line = lineInfo.LineNumber;
                    var row = ReadCompletion(reader, line);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                    else
                    {
                        // Difesa in profondita': una riga mai scartata in silenzio.
                        errors.Add(new XmlIssue(line, 0, "Riga non interpretabile: campi obbligatori mancanti o non validi."));
                    }
                }
            }
        }
        catch (DepthExceededException ex)
        {
            errors.Add(new XmlIssue(ex.Line, ex.Position, $"Profondità massima ({MaxDepth}) superata."));
        }
        catch (XmlException ex)
        {
            // Documento non ben formato, DTD presente, limite di dimensione superato...
            errors.Add(new XmlIssue(ex.LineNumber, ex.LinePosition, Describe(ex)));
        }

        return new CompletionParseResult(exportId, generatedAt, errors.Count == 0 ? rows : [], errors);
    }

    private static ParsedCompletion? ReadCompletion(XmlReader reader, int lineNumber)
    {
        string? employee = null, course = null, completed = null, score = null;
        using (var row = reader.ReadSubtree())
        {
            row.Read(); // <completion>
            row.Read(); // primo figlio
            // ReadElementContentAsString avanza gia' al nodo successivo: si chiama Read() solo negli altri casi,
            // altrimenti si salterebbe un elemento su due.
            while (!row.EOF)
            {
                if (row.NodeType != XmlNodeType.Element)
                {
                    row.Read();
                    continue;
                }

                switch (row.LocalName)
                {
                    case "employeeNumber": employee = row.ReadElementContentAsString(); break;
                    case "courseCode": course = row.ReadElementContentAsString(); break;
                    case "completedOn": completed = row.ReadElementContentAsString(); break;
                    case "score": score = row.ReadElementContentAsString(); break;
                    default: SkipChecked(row); break;
                }
            }
        }

        // Se un valore manca o non e' convertibile, l'errore di schema e' gia' stato registrato.
        if (employee is null || course is null
            || !DateOnly.TryParseExact(completed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        decimal? parsedScore = null;
        if (score is not null)
        {
            try
            {
                parsedScore = XmlConvert.ToDecimal(score); // stessa sintassi lessicale di xs:decimal
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        return new ParsedCompletion(lineNumber, employee.Trim(), course.Trim(), date, parsedScore);
    }

    /// <summary>Come <see cref="XmlReader.Skip"/>, ma verifica la profondita' di ogni nodo attraversato.</summary>
    private static void SkipChecked(XmlReader reader)
    {
        var depth = reader.Depth;
        if (!reader.IsEmptyElement)
        {
            while (reader.Read() && reader.Depth > depth)
            {
                if (reader.Depth > MaxDepth)
                {
                    var li = (IXmlLineInfo)reader;
                    throw new DepthExceededException(li.LineNumber, li.LinePosition);
                }
            }
        }

        reader.Read();
    }

    private sealed class DepthExceededException(int line, int position) : Exception
    {
        public int Line { get; } = line;

        public int Position { get; } = position;
    }

    private static string Describe(XmlException ex) =>
        ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
            ? "Il file contiene una DTD: non è consentita per motivi di sicurezza."
            : ex.Message.Contains("MaxCharactersInDocument", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("limite", StringComparison.OrdinalIgnoreCase)
                ? "Il file supera la dimensione massima consentita."
                : $"XML non ben formato: {ex.Message}";
}
