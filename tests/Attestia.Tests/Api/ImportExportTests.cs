using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Attestia.Core.Xml;
using Attestia.Tests.Infrastructure;
using Attestia.Tests.Xml;

namespace Attestia.Tests.Api;

/// <summary>Anagrafiche minime referenziate dai file di esempio in /contracts/samples; nessun attestato iniziale.</summary>
public sealed class ImportScenario : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    public ImportScenario(MsSqlFixture server)
    {
        var db = new ClassDatabase(server);
        ConnectionString = db.ConnectionString;
        Task.Run(async () =>
        {
            foreach (var n in new[] { 10, 11, 12, 13, 20, 21, 22, 23, 30, 31 })
            {
                await Sql.AddEmployeeAsync(ConnectionString, $"E00{n}", n < 20 ? "Produzione" : "Logistica");
            }

            await Sql.AddEmployeeAsync(ConnectionString, "E0059", active: false);
            await Sql.AddCourseAsync(ConnectionString, "GDPR", 24, mandatory: true);
            await Sql.AddCourseAsync(ConnectionString, "ANTINC", 60);
            await Sql.AddCourseAsync(ConnectionString, "SIC-SPEC", 60, mandatory: true);
            await Sql.AddCourseAsync(ConnectionString, "PRIMO-SOC", 36);
        }).GetAwaiter().GetResult();
        Factory = new ApiFactory(ConnectionString, now: Now);
    }

    public string ConnectionString { get; }

    public ApiFactory Factory { get; }

    public void Dispose() => Factory.Dispose();
}

[Trait("Category", "Integration")]
public sealed class ImportExportTests(ImportScenario scenario) : IClassFixture<ImportScenario>
{
    private readonly ApiFactory _api = scenario.Factory;
    private readonly string _cs = scenario.ConnectionString;

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] content, string fileName = "export.xml", bool dryRun = false)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/imports/certificates?dryRun={dryRun.ToString().ToLowerInvariant()}", form);
    }

    private static byte[] Sample(string name) => File.ReadAllBytes(CompletionXmlParserTests.Sample(name));

    private Task<int> CertificateCount(string employee) =>
        Sql.ScalarAsync<int>(_cs, "SELECT COUNT(*) FROM dbo.Certificates c JOIN dbo.Employees e ON e.Id = c.EmployeeId WHERE e.EmployeeNumber = @employee", new { employee });

    private static string Xml(string exportId, params (string Emp, string Course, string Date)[] rows) =>
        $"""<completions xmlns="urn:attestia:import:v1" exportId="{exportId}" generatedAt="2026-11-20T08:00:00Z">"""
        + string.Concat(rows.Select(r => $"<completion><employeeNumber>{r.Emp}</employeeNumber><courseCode>{r.Course}</courseCode><completedOn>{r.Date}</completedOn></completion>"))
        + "</completions>";

    [Fact]
    public async Task Valid_file_is_imported_and_the_same_file_is_refused_the_second_time()
    {
        using var client = _api.ClientAs("E0030", "HR");

        using var first = await Upload(client, Sample("import-valid.xml"), "lms-novembre.xml");
        var report = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var second = await Upload(client, Sample("import-valid.xml"), "copia.xml");
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(4, report.GetProperty("okRows").GetInt32());
        Assert.Equal(0, report.GetProperty("rejectedRows").GetInt32());
        Assert.Equal("lms-novembre.xml", report.GetProperty("fileName").GetString());
        Assert.Equal(64, report.GetProperty("sha256").GetString()!.Length);
        Assert.Equal("Imported", report.GetProperty("rows")[0].GetProperty("outcome").GetString());
        Assert.Equal("2028-11-12", report.GetProperty("rows")[0].GetProperty("expiresOn").GetString());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("già stato importato", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, await CertificateCount("E0010"));
        Assert.Equal("XmlImport", await Sql.ScalarAsync<string>(_cs,
            "SELECT TOP 1 c.Source FROM dbo.Certificates c JOIN dbo.Employees e ON e.Id = c.EmployeeId WHERE e.EmployeeNumber = 'E0010'"));
    }

    [Fact]
    public async Task Reimporting_the_same_content_in_a_new_file_creates_no_duplicates()
    {
        using var client = _api.ClientAs("E0030", "HR");
        var rows = new[] { ("E0031", "GDPR", "2026-10-01"), ("E0031", "ANTINC", "2026-10-02") };

        using var first = await Upload(client, Encoding.UTF8.GetBytes(Xml("A-1", rows)));
        using var again = await Upload(client, Encoding.UTF8.GetBytes(Xml("A-2", rows))); // exportId diverso => SHA diverso
        var report = await again.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, report.GetProperty("okRows").GetInt32());
        Assert.All(report.GetProperty("rows").EnumerateArray(), r => Assert.Equal("Attestato già registrato.", r.GetProperty("reason").GetString()));
        Assert.Equal(2, await CertificateCount("E0031"));
    }

    [Fact]
    public async Task Semantic_errors_are_reported_per_line_and_valid_rows_are_saved()
    {
        using var client = _api.ClientAs("E0030", "HR");

        using var response = await Upload(client, Sample("import-semantic-errors.xml"));
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rejected = report.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("outcome").GetString() == "Rejected")
            .ToDictionary(r => r.GetProperty("lineNumber").GetInt32(), r => r.GetProperty("reason").GetString());
        var history = await client.GetFromJsonAsync<JsonElement>("/api/imports");
        var log = history.EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == report.GetProperty("importId").GetInt32());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7, report.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, report.GetProperty("okRows").GetInt32());
        Assert.Equal(new Dictionary<int, string?>
        {
            [11] = "Matricola inesistente.",
            [16] = "Codice corso inesistente.",
            [21] = "Data di completamento futura.",
            [26] = "Riga duplicata nel file (stessi dati della riga 5).",
            [31] = "Dipendente non più attivo.",
        }, rejected);
        Assert.Equal(5, log.GetProperty("rejectedRows").GetInt32());
        Assert.Equal("E0030", log.GetProperty("importedBy").GetString());
        Assert.Equal(5, log.GetProperty("errors").GetArrayLength());
        Assert.Equal(11, log.GetProperty("errors")[0].GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Schema_errors_reject_the_whole_file_with_line_numbers()
    {
        using var client = _api.ClientAs("E0030", "HR");

        using var response = await Upload(client, Sample("import-schema-errors.xml"), "schema-ko.xml");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(problem.GetProperty("schemaErrors").EnumerateArray(), e => e.GetProperty("line").GetInt32() == 12);
        Assert.Equal(0, await Sql.ScalarAsync<int>(_cs, "SELECT COUNT(*) FROM dbo.ImportLogs WHERE FileName = 'schema-ko.xml'"));
    }

    [Fact]
    public async Task Dry_run_previews_the_outcome_without_saving_anything()
    {
        using var client = _api.ClientAs("E0030", "HR");
        var content = Encoding.UTF8.GetBytes(Xml("DRY-1", ("E0020", "PRIMO-SOC", "2026-09-09"), ("E9999", "GDPR", "2026-09-09")));

        using var preview = await Upload(client, content, dryRun: true);
        var report = await preview.Content.ReadFromJsonAsync<JsonElement>();
        using var real = await Upload(client, content); // l'anteprima non "consuma" lo SHA

        Assert.True(report.GetProperty("dryRun").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("importId").ValueKind);
        Assert.Equal(1, report.GetProperty("okRows").GetInt32());
        Assert.Equal(HttpStatusCode.OK, real.StatusCode);
    }

    [Fact]
    public async Task Concurrent_imports_of_overlapping_files_never_duplicate_a_certificate()
    {
        using var client = _api.ClientAs("E0030", "HR");
        var shared = ("E0021", "SIC-SPEC", "2026-08-08");

        var uploads = await Task.WhenAll(
            Upload(client, Encoding.UTF8.GetBytes(Xml("RACE-1", shared, ("E0021", "GDPR", "2026-08-01")))),
            Upload(client, Encoding.UTF8.GetBytes(Xml("RACE-2", shared, ("E0021", "ANTINC", "2026-08-02")))));

        Assert.All(uploads, u => Assert.Equal(HttpStatusCode.OK, u.StatusCode));
        Assert.Equal(1, await Sql.ScalarAsync<int>(_cs, """
            SELECT COUNT(*) FROM dbo.Certificates c JOIN dbo.Employees e ON e.Id = c.EmployeeId JOIN dbo.Courses co ON co.Id = c.CourseId
            WHERE e.EmployeeNumber = 'E0021' AND co.Code = 'SIC-SPEC'
            """));
        foreach (var u in uploads)
        {
            u.Dispose();
        }
    }

    [Theory]
    [InlineData("dati.csv", 10, HttpStatusCode.UnsupportedMediaType)]
    [InlineData("vuoto.xml", 0, HttpStatusCode.BadRequest)]
    [InlineData("enorme.xml", 2 * 1024 * 1024 + 1, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Upload_rejects_wrong_type_empty_and_oversized_files(string fileName, int size, HttpStatusCode expected)
    {
        using var client = _api.ClientAs("E0030", "HR");

        using var response = await Upload(client, new byte[size], fileName);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rate_limited_per_user()
    {
        using var limited = new ApiFactory(_cs, now: ImportScenario.Now, uploadsPerMinute: 2);
        using var client = limited.ClientAs("E0030", "HR");
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            using var r = await Upload(client, Encoding.UTF8.GetBytes(Xml($"RL-{i}", ("E9999", "GDPR", "2026-01-01"))), dryRun: true);
            statuses.Add(r.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Single_line_files_with_many_rows_are_supported()
    {
        using var client = _api.ClientAs("E0030", "HR");

        using var response = await Upload(client, Encoding.UTF8.GetBytes(Xml("ONE-LINE", ("E0012", "GDPR", "2026-05-05"), ("E0012", "ANTINC", "2026-05-05"), ("E0012", "GDPR", "2026-05-05"))));
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, report.GetProperty("okRows").GetInt32());
        Assert.Equal(1, report.GetProperty("rejectedRows").GetInt32());
    }

    [Fact]
    public async Task Employees_cannot_import()
    {
        using var client = _api.ClientAs("E0010", "Employee");

        using var response = await Upload(client, Sample("import-valid.xml"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_is_valid_against_the_export_xsd_and_contains_the_expiring_rows()
    {
        await Sql.ExecAsync(_cs, """
            INSERT INTO dbo.Certificates (EmployeeId, CourseId, CompletedOn, ValidityMonths, Source)
            SELECT e.Id, co.Id, '2024-12-10', co.ValidityMonths, 'Manual'
            FROM dbo.Employees e CROSS JOIN dbo.Courses co WHERE e.EmployeeNumber = 'E0023' AND co.Code = 'GDPR'
            """); // scade il 10/12/2026: tra 16 giorni
        using var client = _api.ClientAs("E0030", "HR");

        using var response = await client.GetAsync("/api/exports/expirations.xml?withinDays=30&department=Logistica");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var schemaErrors = new List<string>();
        var doc = XDocument.Load(new MemoryStream(bytes));
        doc.Validate(XmlSchemas.Export, (_, e) => schemaErrors.Add(e.Message));
        XNamespace ns = XmlSchemas.ExportNamespace;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("scadenze-2026-11-24.xml", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Empty(schemaErrors);
        Assert.Equal("Logistica", doc.Root!.Attribute("department")!.Value);
        var row = Assert.Single(doc.Root.Elements(ns + "expiration"), e => e.Element(ns + "employeeNumber")!.Value == "E0023");
        Assert.Equal("ExpiringSoon", row.Element(ns + "status")!.Value);
        Assert.Equal("16", row.Element(ns + "daysToExpiry")!.Value);
        Assert.Equal(doc.Root.Elements(ns + "expiration").Count().ToString(System.Globalization.CultureInfo.InvariantCulture), doc.Root.Attribute("count")!.Value);
    }

    [Fact]
    public async Task Export_xsd_rejects_a_tampered_document()
    {
        var doc = XDocument.Parse("""<expirations xmlns="urn:attestia:export:v1" generatedAt="2026-11-24T09:00:00Z" asOf="2026-11-24" withinDays="30" count="0"><expiration/></expirations>""");
        var errors = new List<string>();

        doc.Validate(XmlSchemas.Export, (_, e) => errors.Add(e.Message));

        Assert.NotEmpty(errors);
    }
}
