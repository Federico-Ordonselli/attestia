<%
' Include: niente direttiva @ ne' Option Explicit (li dichiara la pagina che lo include;
' ripeterli in un include causa gli errori ASP 0141 e "Option Explicit" duplicato).
Const CookieName = "attestia_sso"
Const CookieVersion = "v1"
Dim CurrentEmployeeNumber, CurrentRole

Function Matches(ByVal value, ByVal pattern)
    Dim expression
    Set expression = New RegExp
    expression.Pattern = pattern
    Matches = expression.Test(value)
End Function

Function SignatureMatches(ByVal actual, ByVal expected)
    Dim difference, i
    SignatureMatches = False
    If Len(actual) <> 64 Or Len(expected) <> 64 Then Exit Function
    difference = 0
    For i = 1 To 64
        difference = difference Or (Asc(Mid(actual, i, 1)) Xor Asc(Mid(expected, i, 1)))
    Next
    SignatureMatches = (difference = 0)
End Function

Function CookieSignature(ByVal payload)
    CookieSignature = HmacSha256Hex(payload, GetEnv("ATTESTIA_SSO_KEY"))
End Function

' HMAC-SHA256 in esadecimale minuscolo; separata dalla lettura della chiave per poterla verificare
' con il vettore di test indipendente (legacy/portale-tests/hmac-selftest.asp).
Function HmacSha256Hex(ByVal payload, ByVal encodedKey)
    Dim doc, node, key, algorithm, encoding, bytes, digest, i, result
    HmacSha256Hex = ""
    If IsNull(encodedKey) Or IsEmpty(encodedKey) Then Exit Function
    encodedKey = CStr(encodedKey)
    If Len(encodedKey) = 0 Then Exit Function
    encodedKey = Replace(Replace(Replace(Replace(encodedKey, " ", ""), vbTab, ""), vbCr, ""), vbLf, "")
    If Not Matches(encodedKey, "^([A-Za-z0-9+/]{4})*([A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$") Then Exit Function
    Set doc = CreateObject("MSXML2.DOMDocument.6.0")
    Set node = doc.createElement("key")
    node.dataType = "bin.base64"
    node.text = encodedKey
    key = node.nodeTypedValue
    If LenB(key) < 32 Then Exit Function

    Set algorithm = CreateObject("System.Security.Cryptography.HMACSHA256")
    Set encoding = CreateObject("System.Text.UTF8Encoding")
    algorithm.Key = key
    bytes = encoding.GetBytes_4(CStr(payload))
    digest = algorithm.ComputeHash_2(bytes)
    result = ""
    For i = 1 To LenB(digest)
        result = result & Right("0" & Hex(AscB(MidB(digest, i, 1))), 2)
    Next
    HmacSha256Hex = LCase(result)
End Function

Function VerifyCookie()
    Dim value, parts, payload
    VerifyCookie = False
    value = CStr(Request.Cookies(CookieName))
    If Len(value) = 0 Or Len(value) > 120 Then Exit Function
    If Matches(value, "[^A-Za-z0-9|-]") Then Exit Function
    parts = Split(value, "|")
    If UBound(parts) <> 4 Then Exit Function
    If parts(0) <> CookieVersion Then Exit Function
    If Not Matches(parts(1), "^[A-Z0-9-]{2,20}$") Then Exit Function
    Select Case parts(2)
        Case "Employee", "HR", "Admin"
        Case Else: Exit Function
    End Select
    If Not Matches(parts(3), "^[0-9]{1,10}$") Then Exit Function
    If Not Matches(parts(4), "^[0-9a-f]{64}$") Then Exit Function
    If CDbl(parts(3)) <= UnixNow() Then Exit Function
    payload = Left(value, Len(value) - 65)
    If Not SignatureMatches(parts(4), CookieSignature(payload)) Then Exit Function
    CurrentEmployeeNumber = parts(1)
    CurrentRole = parts(2)
    VerifyCookie = True
End Function

Function Authenticated()
    Dim verified
    verified = False
    CurrentEmployeeNumber = ""
    CurrentRole = ""
    ' Un errore COM interrompe VerifyCookie e torna a questo unico gestore.
    On Error Resume Next
    Err.Clear
    verified = VerifyCookie()
    If Err.Number <> 0 Then verified = False
    On Error GoTo 0
    Authenticated = verified
End Function

Sub RequireLogin()
    If Not Authenticated() Then
        Response.Redirect "/sso/login?returnUrl=" & Server.URLEncode("/portale/")
        Response.End
    End If
End Sub

Sub RequireRole(ByVal rolesCsv)
    Dim role
    For Each role In Split(rolesCsv, ",")
        If CurrentRole = Trim(role) Then Exit Sub
    Next
    Response.Redirect "/portale/denied.asp"
    Response.End
End Sub
%>
