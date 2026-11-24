<%@ Language="VBScript" CodePage=65001 EnableSessionState=False %>
<% Option Explicit
%>
<!--#include file="include/helpers.asp"-->
<!--#include file="include/auth.asp"-->
<!--#include file="include/db.asp"-->
<%
PrivateResponse
RequireLogin
RequireRole "Employee,HR,Admin"
On Error Resume Next
RenderHistory
If Err.Number <> 0 Then
    Response.Clear
    Response.Status = "500 Internal Server Error"
%>
<!doctype html>
<html lang="it"><meta charset="utf-8"><title>Errore del portale</title>
<main><h1>Storico formazione</h1><p role="alert"><%= H(GenericError) %></p>
<a href="default.asp">Ricarica la pagina</a></main></html>
<%
End If
On Error GoTo 0

Sub RenderHistory()
    Dim rs, counts, statuses, status, hasHistory, employeeName, mandatory, expiry, score
    statuses = Array("Valid", "ExpiringSoon", "Expired", "Missing")
    Set counts = Server.CreateObject("Scripting.Dictionary")
    For Each status In statuses
        counts.Add status, 0
    Next
    Set rs = GetCertificateHistory(CurrentEmployeeNumber)
    hasHistory = HasRows(rs)
    employeeName = CurrentEmployeeNumber
    If hasHistory Then
        employeeName = rs("FirstName").Value & " " & rs("LastName").Value
        Do Until rs.EOF
            If CBool(rs("IsCurrent").Value) Then
                status = rs("Status").Value
                counts(status) = counts(status) + 1
            End If
            rs.MoveNext
        Loop
        rs.MoveFirst
    End If
%>
<!doctype html>
<html lang="it">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title><%= H(Application("PortalTitle")) %> | Attestia</title>
    <link rel="stylesheet" href="assets/vendor/bootstrap/css/bootstrap.min.css">
    <link rel="stylesheet" href="assets/vendor/datatables/dataTables.bootstrap.min.css">
    <link rel="stylesheet" href="assets/css/portal.css">
    <script defer src="assets/vendor/jquery/jquery.min.js"></script>
    <script defer src="assets/vendor/bootstrap/js/bootstrap.min.js"></script>
    <script defer src="assets/vendor/datatables/dataTables.min.js"></script>
    <script defer src="assets/vendor/datatables/dataTables.bootstrap.min.js"></script>
    <script defer src="assets/js/portal.js"></script>
</head>
<body>
<a class="skip-link" href="#contenuto">Vai al contenuto</a>
<header class="portal-header"><div class="container">
    <strong>Attestia</strong><a href="logout.asp">Esci dal portale</a>
</div></header>
<main class="container" id="contenuto" tabindex="-1">
    <h1>Il mio storico formazione</h1>
    <p>Benvenuto nel portale, <strong><%= H(employeeName) %></strong>.</p>
    <section aria-labelledby="summary-title">
        <h2 id="summary-title">Situazione attuale</h2>
        <ul class="summary">
        <% For Each status In statuses %>
            <li><span class="label <%= H(StatusBadgeClass(status)) %>"><%= H(StatusLabel(status)) %></span>
                <strong data-count="<%= H(status) %>"><%= H(counts(status)) %></strong></li>
        <% Next %>
        </ul>
    </section>
    <section aria-labelledby="history-title">
        <h2 id="history-title">Attestati e rinnovi</h2>
        <button type="button" id="refresh" class="btn btn-default" hidden>Aggiorna</button>
        <p role="status" id="feedback"></p>
        <p role="alert" id="refresh-error"></p>
        <% If Not hasHistory Then %><p id="empty-history">Nessun attestato registrato</p><% End If %>
        <div class="table-responsive">
        <table class="table table-striped" id="history">
            <caption>Storico dei corsi e degli attestati personali</caption>
            <thead><tr>
                <th scope="col">Corso</th><th scope="col">Categoria</th><th scope="col">Obbligatorio</th>
                <th scope="col">Completato il</th><th scope="col">Scade il</th>
                <th scope="col">Punteggio</th><th scope="col">Stato</th>
            </tr></thead>
            <tbody>
            <%
            If hasHistory Then
                Do Until rs.EOF
                    mandatory = "No"
                    If CBool(rs("IsMandatory").Value) Then mandatory = "Sì"
                    expiry = "Non scade"
                    If Not IsNull(rs("ExpiresOn").Value) Then expiry = ItDate(rs("ExpiresOn").Value)
                    score = "—"
                    If Not IsNull(rs("Score").Value) Then score = Replace(JsonNumber(rs("Score").Value), ".", ",")
                    status = rs("Status").Value
            %>
                <tr>
                    <td><%= H(rs("CourseTitle").Value) %> (<%= H(rs("CourseCode").Value) %>)</td>
                    <td><%= H(rs("Category").Value) %></td><td><%= H(mandatory) %></td>
                    <td data-order="<%= H(IsoDate(rs("CompletedOn").Value)) %>"><%= H(ItDate(rs("CompletedOn").Value)) %></td>
                    <td data-order="<%= H(IsoDate(rs("ExpiresOn").Value)) %>"><%= H(expiry) %></td>
                    <td><%= H(score) %></td>
                    <td><span class="label <%= H(StatusBadgeClass(status)) %>"><%= H(StatusLabel(status)) %></span>
                    <% If Not CBool(rs("IsCurrent").Value) Then %><span class="label label-default">Rinnovato</span><% End If %></td>
                </tr>
            <%
                    rs.MoveNext
                Loop
            End If
            If rs.State = adStateOpen Then rs.Close
            %>
            </tbody>
        </table></div>
    </section>
</main>
<footer class="container">Portale basato su Bootstrap 3 (fuori supporto), mantenuto per rappresentare un sistema preesistente.</footer>
</body>
</html>
<%
End Sub
%>
