<%
' Include: niente direttiva @ ne' Option Explicit (li dichiara la pagina che lo include;
' ripeterli in un include causa gli errori ASP 0141 e "Option Explicit" duplicato).
Const GenericError = "Si è verificato un errore. Riprova più tardi."

Function H(ByVal value)
    If IsNull(value) Then value = ""
    H = Server.HTMLEncode(CStr(value))
End Function

Function JsonString(ByVal value)
    Dim result, i, ch, code
    If IsNull(value) Then
        JsonString = "null"
        Exit Function
    End If
    value = CStr(value)
    result = Chr(34)
    For i = 1 To Len(value)
        ch = Mid(value, i, 1)
        code = AscW(ch) And 65535
        Select Case code
            Case 34: result = result & "\" & Chr(34)
            Case 92: result = result & "\\"
            Case 47: result = result & "\/"
            Case 8: result = result & "\b"
            Case 12: result = result & "\f"
            Case 10: result = result & "\n"
            Case 13: result = result & "\r"
            Case 9: result = result & "\t"
            Case Else
                If code < 32 Or code = 38 Or code = 39 Or code = 60 Or code = 62 Then
                    result = result & "\u" & Right("0000" & LCase(Hex(code)), 4)
                Else
                    result = result & ch
                End If
        End Select
    Next
    JsonString = result & Chr(34)
End Function

Function TwoDigits(ByVal value)
    TwoDigits = Right("0" & CStr(value), 2)
End Function

Function IsoDate(ByVal value)
    IsoDate = Null
    If IsNull(value) Then Exit Function
    If VarType(value) = vbString Then
        If Len(value) = 10 And Mid(value, 5, 1) = "-" And Mid(value, 8, 1) = "-" Then
            IsoDate = value
            Exit Function
        End If
    End If
    IsoDate = Right("0000" & Year(value), 4) & "-" & TwoDigits(Month(value)) & "-" & TwoDigits(Day(value))
End Function

Function ItDate(ByVal value)
    ItDate = "—"
    If IsNull(value) Then Exit Function
    value = IsoDate(value)
    ItDate = Right(value, 2) & "/" & Mid(value, 6, 2) & "/" & Left(value, 4)
End Function

Function UtcTimestamp()
    Dim seconds, stamp
    seconds = UnixNow()
    stamp = DateAdd("s", seconds - Int(seconds / 86400) * 86400, DateAdd("d", Int(seconds / 86400), DateSerial(1970, 1, 1)))
    UtcTimestamp = IsoDate(stamp) & "T" & TwoDigits(Hour(stamp)) & ":" & TwoDigits(Minute(stamp)) & ":" & TwoDigits(Second(stamp)) & "Z"
End Function

Function JsonNumber(ByVal value)
    JsonNumber = "null"
    If IsNull(value) Then Exit Function
    JsonNumber = Replace(CStr(CDbl(value)), ",", ".")
End Function

Function JsonBoolean(ByVal value)
    JsonBoolean = "false"
    If CBool(value) Then JsonBoolean = "true"
End Function

Function StatusLabel(ByVal status)
    Select Case status
        Case "Valid": StatusLabel = "Valido"
        Case "ExpiringSoon": StatusLabel = "In scadenza"
        Case "Expired": StatusLabel = "Scaduto"
        Case "Missing": StatusLabel = "Mancante"
        Case Else: StatusLabel = "Sconosciuto"
    End Select
End Function

Function StatusBadgeClass(ByVal status)
    Select Case status
        Case "Valid": StatusBadgeClass = "label-success"
        Case "ExpiringSoon": StatusBadgeClass = "label-warning"
        Case "Expired": StatusBadgeClass = "label-danger"
        Case Else: StatusBadgeClass = "label-default"
    End Select
End Function

Function GetEnv(ByVal name)
    Dim shell, environment
    Set shell = CreateObject("WScript.Shell")
    Set environment = shell.Environment("PROCESS")
    GetEnv = environment(name)
End Function

Sub PrivateResponse()
    Response.Buffer = True
    Response.CodePage = 65001
    Response.Charset = "utf-8"
    Response.AddHeader "Cache-Control", "no-store, private"
    Response.AddHeader "Pragma", "no-cache"
    Response.Expires = -1
End Sub

Sub SendJsonError(ByVal status, ByVal message)
    Response.Clear
    Response.Status = status
    Response.ContentType = "application/json"
    Response.Write "{""error"":"
    Response.Write JsonString(message)
    Response.Write "}"
    Response.End
End Sub
%>
<script language="JScript" runat="server">
function UnixNow() {
    return Math.floor(new Date().getTime() / 1000);
}
</script>
