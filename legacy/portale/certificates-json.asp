<%@ Language="VBScript" CodePage=65001 EnableSessionState=False %>
<% Option Explicit
%>
<!--#include file="include/helpers.asp"-->
<!--#include file="include/auth.asp"-->
<!--#include file="include/db.asp"-->
<%
PrivateResponse
Response.ContentType = "application/json"
If Not Authenticated() Then SendJsonError "401 Unauthorized", "Accesso richiesto."
On Error Resume Next
WriteHistory
If Err.Number <> 0 Then SendJsonError "500 Internal Server Error", GenericError
On Error GoTo 0

Sub WriteHistory()
    Dim rs
    Set rs = GetCertificateHistory(CurrentEmployeeNumber)
    Response.Write "{""employeeNumber"":"
    Response.Write JsonString(CurrentEmployeeNumber)
    Response.Write ",""generatedAt"":"
    Response.Write JsonString(UtcTimestamp())
    Response.Write ",""items"":["
    If HasRows(rs) Then
        Do Until rs.EOF
            Response.Write "{""courseCode"":"
            Response.Write JsonString(rs("CourseCode").Value)
            Response.Write ",""courseTitle"":"
            Response.Write JsonString(rs("CourseTitle").Value)
            Response.Write ",""category"":"
            Response.Write JsonString(rs("Category").Value)
            Response.Write ",""isMandatory"":"
            Response.Write JsonBoolean(rs("IsMandatory").Value)
            Response.Write ",""completedOn"":"
            Response.Write JsonString(IsoDate(rs("CompletedOn").Value))
            Response.Write ",""expiresOn"":"
            Response.Write JsonString(IsoDate(rs("ExpiresOn").Value))
            Response.Write ",""score"":"
            Response.Write JsonNumber(rs("Score").Value)
            Response.Write ",""status"":"
            Response.Write JsonString(rs("Status").Value)
            Response.Write ",""isCurrent"":"
            Response.Write JsonBoolean(rs("IsCurrent").Value)
            Response.Write "}"
            rs.MoveNext
            If Not rs.EOF Then Response.Write ","
        Loop
    End If
    If rs.State = adStateOpen Then rs.Close
    Response.Write "]}"
End Sub
%>
