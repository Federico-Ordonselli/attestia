using System.Globalization;
using System.Text;
using System.Xml;
using Attestia.Core.Models;
using Attestia.Core.Xml;

namespace Attestia.Api.XmlExchange;

/// <summary>Scrive l'export delle scadenze nel formato <c>urn:attestia:export:v1</c> (vedi contracts/attestia-export-v1.xsd).</summary>
public static class ExpirationsXmlWriter
{
    public static async Task WriteAsync(Stream output, IReadOnlyList<ExpiringCertificate> rows, DateTimeOffset generatedAt, DateOnly asOf,
        int withinDays, string? department, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = true };
        await using var w = XmlWriter.Create(output, settings);

        await w.WriteStartDocumentAsync();
        await w.WriteStartElementAsync(null, "expirations", XmlSchemas.ExportNamespace);
        await w.WriteAttributeStringAsync(null, "generatedAt", null, XmlConvert.ToString(generatedAt));
        await w.WriteAttributeStringAsync(null, "asOf", null, Date(asOf));
        await w.WriteAttributeStringAsync(null, "withinDays", null, withinDays.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(department))
        {
            await w.WriteAttributeStringAsync(null, "department", null, department);
        }

        await w.WriteAttributeStringAsync(null, "count", null, rows.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            await w.WriteStartElementAsync(null, "expiration", XmlSchemas.ExportNamespace);
            await Element(w, "employeeNumber", r.EmployeeNumber);
            await Element(w, "lastName", r.LastName);
            await Element(w, "firstName", r.FirstName);
            await Element(w, "department", r.Department);
            await Element(w, "courseCode", r.CourseCode);
            await Element(w, "courseTitle", r.CourseTitle);
            await Element(w, "completedOn", Date(r.CompletedOn));
            await Element(w, "expiresOn", Date(r.ExpiresOn));
            await Element(w, "daysToExpiry", r.DaysToExpiry.ToString(CultureInfo.InvariantCulture));
            await Element(w, "status", r.Status.ToString());
            await w.WriteEndElementAsync();
        }

        await w.WriteEndElementAsync();
        await w.WriteEndDocumentAsync();
        await w.FlushAsync();
    }

    private static Task Element(XmlWriter w, string name, string value) => w.WriteElementStringAsync(null, name, XmlSchemas.ExportNamespace, value);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
