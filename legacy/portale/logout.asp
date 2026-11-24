<%@ Language="VBScript" CodePage=65001 EnableSessionState=False %>
<% Option Explicit
Response.Buffer = True
Response.AddHeader "Cache-Control", "no-store"
Response.AddHeader "Set-Cookie", "attestia_sso=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; Secure; SameSite=Lax"
Response.Redirect "/sso/logout"
Response.End
%>
