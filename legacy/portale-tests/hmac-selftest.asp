<%@ Language="VBScript" CodePage=65001 EnableSessionState=False %>
<% Option Explicit
' AUTOTEST DA COLLAUDO - non fa parte del pacchetto e va rimosso subito dopo l'uso.
' Copiarlo temporaneamente in C:\inetpub\attestia\portale\ e aprire https://attestia.local/portale/hmac-selftest.asp
' Verifica che l'HMAC calcolato in VBScript (via COM .NET Framework) coincida con il vettore calcolato
' con Python (hmac/hashlib) e usato anche dai test .NET (tests/Attestia.Tests/Sso/SsoCookieTests.cs).
%>
<!--#include file="include/helpers.asp"-->
<!--#include file="include/auth.asp"-->
<%
Const VectorKey = "YXR0ZXN0aWEtdGVzdC12ZWN0b3Ita2V5LTAxMjM0NTY3ODlhYmNkZWY="
Const VectorPayload = "v1|E0001|Employee|1893456000"
Const VectorSignature = "bf8110b684f1a1469583295c22ad83fab40e113e4cc08f58103e9217d0ef791d"
Dim actual, timeOk
Response.ContentType = "text/plain"
Response.Charset = "utf-8"
If Request.ServerVariables("REMOTE_ADDR") <> "127.0.0.1" And Request.ServerVariables("REMOTE_ADDR") <> "::1" Then
    Response.Status = "404 Not Found"
    Response.End
End If
actual = HmacSha256Hex(VectorPayload, VectorKey)
timeOk = Abs(UnixNow() - DateDiff("s", #1970-01-01#, Now())) < 15 * 3600
Response.Write "HMAC: " & H(actual) & vbCrLf
If SignatureMatches(actual, VectorSignature) Then Response.Write "HMAC OK" & vbCrLf Else Response.Write "HMAC DIVERSO DAL VETTORE" & vbCrLf
If timeOk Then Response.Write "UnixNow OK" & vbCrLf Else Response.Write "UnixNow ANOMALO" & vbCrLf
%>
