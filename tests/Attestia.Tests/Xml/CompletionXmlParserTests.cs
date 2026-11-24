using System.Text;
using Attestia.Core.Xml;

namespace Attestia.Tests.Xml;

public sealed class CompletionXmlParserTests
{
    internal static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Contracts", "samples", name);

    private static CompletionParseResult ParseText(string xml, long max = CompletionXmlParser.DefaultMaxCharacters) =>
        CompletionXmlParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)), max);

    private const string Header = """<completions xmlns="urn:attestia:import:v1" exportId="T-1" generatedAt="2026-11-20T08:30:00+01:00">""";

    [Fact]
    public void Valid_sample_is_parsed_with_source_line_numbers()
    {
        using var file = File.OpenRead(Sample("import-valid.xml"));

        var result = CompletionXmlParser.Parse(file);

        Assert.True(result.IsValid);
        Assert.Equal("LMS-2026-11-0001", result.ExportId);
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(new ParsedCompletion(4, "E0010", "GDPR", new DateOnly(2026, 11, 12), 92.5m), result.Rows[0]);
        Assert.Null(result.Rows[1].Score);
        Assert.Equal([4, 10, 15, 21], result.Rows.Select(r => r.LineNumber));
    }

    [Fact]
    public void Schema_errors_sample_reports_every_error_with_its_line_and_returns_no_rows()
    {
        using var file = File.OpenRead(Sample("import-schema-errors.xml"));

        var result = CompletionXmlParser.Parse(file);

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
        // Stesse righe segnalate da xmllint sullo stesso file (verifica incrociata con un validatore indipendente).
        Assert.Superset(new HashSet<int> { 7, 12, 15, 18, 24 }, result.Errors.Select(e => e.LineNumber).ToHashSet());
    }

    [Fact]
    public void Semantic_errors_sample_is_schema_valid()
    {
        using var file = File.OpenRead(Sample("import-semantic-errors.xml"));

        var result = CompletionXmlParser.Parse(file);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.Rows.Count);
    }

    [Fact]
    public void External_entities_are_never_resolved_xxe()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE completions [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <completions xmlns="urn:attestia:import:v1" exportId="X" generatedAt="2026-01-01T00:00:00Z">
              <completion><employeeNumber>&xxe;</employeeNumber><courseCode>GDPR</courseCode><completedOn>2026-01-01</completedOn></completion>
            </completions>
            """;

        var result = ParseText(xml);

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
        Assert.Contains("DTD", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("root:", StringComparison.Ordinal));
    }

    [Fact]
    public void Entity_expansion_bombs_are_rejected_billion_laughs()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [ <!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;"><!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;"> ]>
            <completions xmlns="urn:attestia:import:v1" exportId="X" generatedAt="2026-01-01T00:00:00Z">&lol3;</completions>
            """;

        Assert.Contains("DTD", Assert.Single(ParseText(xml).Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_cannot_choose_its_own_schema()
    {
        // schemaLocation verso uno schema "permissivo": ignorato, si valida solo contro l'XSD incorporato.
        var xml = Header.Replace(">", """ xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="urn:attestia:import:v1 http://attacker.example.test/lax.xsd">""", StringComparison.Ordinal)
            + "<completion><employeeNumber>bad number</employeeNumber><courseCode>GDPR</courseCode><completedOn>2026-01-01</completedOn></completion></completions>";

        var result = ParseText(xml);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Wrong_namespace_is_rejected()
    {
        var result = ParseText("""<completions xmlns="urn:other" exportId="X" generatedAt="2026-01-01T00:00:00Z"/>""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Excessive_depth_is_rejected()
    {
        var nested = string.Concat(Enumerable.Repeat("<x>", 20)) + string.Concat(Enumerable.Repeat("</x>", 20));
        var xml = Header + $"<completion><employeeNumber>E0001</employeeNumber><courseCode>GDPR</courseCode><completedOn>2026-01-01</completedOn>{nested}</completion></completions>";

        var result = ParseText(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Profondità", StringComparison.Ordinal));
    }

    [Fact]
    public void Documents_over_the_size_limit_are_rejected()
    {
        var rows = string.Concat(Enumerable.Repeat("<completion><employeeNumber>E0001</employeeNumber><courseCode>GDPR</courseCode><completedOn>2026-01-01</completedOn></completion>", 50));

        var result = ParseText(Header + rows + "</completions>", max: 1_000);

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("2026-01-31+01:00")]
    [InlineData("2026-01-31Z")]
    [InlineData("2026-02-30")]
    public void Dates_must_be_plain_calendar_dates(string date)
    {
        var result = ParseText(Header + $"<completion><employeeNumber>E0001</employeeNumber><courseCode>GDPR</courseCode><completedOn>{date}</completedOn></completion></completions>");

        Assert.False(result.IsValid);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("+50", 50)]
    [InlineData("99.99", 99.99)]
    [InlineData("0", 0)]
    public void Scores_follow_xsd_decimal_syntax(string score, double expected)
    {
        var result = ParseText(Header + $"<completion><employeeNumber>E0001</employeeNumber><courseCode>GDPR</courseCode><completedOn>2026-01-01</completedOn><score>{score}</score></completion></completions>");

        Assert.True(result.IsValid);
        Assert.Equal((decimal)expected, Assert.Single(result.Rows).Score);
    }

    [Fact]
    public void Malformed_xml_is_reported_not_thrown()
    {
        var result = ParseText(Header + "<completion>");

        Assert.False(result.IsValid);
        Assert.StartsWith("XML non ben formato", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }
}
