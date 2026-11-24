<%@ Language="VBScript" CodePage=65001 EnableSessionState=False %>
<% Option Explicit
%>
<!--#include file="include/helpers.asp"-->
<%
PrivateResponse
Response.Status = "403 Forbidden"
%>
<!doctype html>
<html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Accesso negato | Attestia</title><link rel="stylesheet" href="assets/css/portal.css"></head>
<body><main><h1>Accesso negato</h1><p role="alert">Il tuo ruolo non consente di accedere a questa pagina.</p>
<a href="logout.asp">Esci dal portale</a></main></body></html>
