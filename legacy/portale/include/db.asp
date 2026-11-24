<%
' Include: niente direttiva @ ne' Option Explicit (li dichiara la pagina che lo include;
' ripeterli in un include causa gli errori ASP 0141 e "Option Explicit" duplicato).
Const adCmdStoredProc = 4
Const adVarWChar = 202
Const adParamInput = 1
Const adUseClient = 3
Const adStateOpen = 1
Const adOpenStatic = 3
Const adLockReadOnly = 1

Function OpenConnection()
    Dim connectionString, connection
    ' Esempio: Provider=MSOLEDBSQL19;Server=.;Database=Attestia;Integrated Security=SSPI;
    connectionString = GetEnv("ATTESTIA_PORTAL_DB")
    If Len(Trim(connectionString)) = 0 Then Err.Raise vbObjectError + 1, , GenericError
    Set connection = Server.CreateObject("ADODB.Connection")
    connection.CursorLocation = adUseClient
    connection.Open connectionString
    Set OpenConnection = connection
End Function

Function GetCertificateHistory(ByVal employeeNumber)
    Dim connection, cmd, history
    Set connection = OpenConnection()
    Set cmd = Server.CreateObject("ADODB.Command")
    Set cmd.ActiveConnection = connection
    cmd.CommandType = adCmdStoredProc
    cmd.CommandText = "dbo.usp_EmployeeCertificateHistory"
    cmd.Parameters.Append cmd.CreateParameter("@EmployeeNumber", adVarWChar, adParamInput, 20, employeeNumber)
    Set history = Server.CreateObject("ADODB.Recordset")
    history.CursorLocation = adUseClient
    history.Open cmd, , adOpenStatic, adLockReadOnly
    If history.State = adStateOpen Then Set history.ActiveConnection = Nothing
    connection.Close
    Set GetCertificateHistory = history
End Function

Function HasRows(ByVal rs)
    HasRows = False
    ' La procedura termina senza SELECT per una matricola sconosciuta.
    If rs.State = adStateOpen Then HasRows = Not rs.EOF
End Function
%>
