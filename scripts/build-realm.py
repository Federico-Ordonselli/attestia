#!/usr/bin/env python3
"""Genera deploy/docker/keycloak/realm-attestia.json (realm di SOLO sviluppo).

Il file JSON e' versionato; questo script esiste per renderlo leggibile e rigenerabile.
Password degli utenti: valori palesemente finti, solo per l'IdP locale.
"""
import json
import pathlib

SP_BASE = "https://localhost:7180"
SP_ENTITY_ID = "urn:attestia:sp"

admin_only = {"view": ["admin"], "edit": ["admin"]}
user_view = {"view": ["admin", "user"], "edit": ["admin"]}

user_profile = {
    "attributes": [
        {"name": "username", "displayName": "${username}", "permissions": {"view": ["admin", "user"], "edit": ["admin"]},
         "validations": {"length": {"min": 3, "max": 255}, "username-prohibited-characters": {}, "up-username-not-idn-homograph": {}}},
        {"name": "email", "displayName": "${email}", "permissions": user_view, "validations": {"email": {}, "length": {"max": 255}}},
        {"name": "firstName", "displayName": "${firstName}", "permissions": user_view, "validations": {"length": {"max": 255}}},
        {"name": "lastName", "displayName": "${lastName}", "permissions": user_view, "validations": {"length": {"max": 255}}},
        {"name": "employeeNumber", "displayName": "Matricola", "permissions": admin_only,
         "validations": {"pattern": {"pattern": "^E[0-9]{4}$", "error-message": "Formato matricola: E0000"}}},
        {"name": "role", "displayName": "Ruolo Attestia", "permissions": admin_only,
         "validations": {"options": {"options": ["Employee", "HR", "Admin"]}}},
        {"name": "displayName", "displayName": "Nome visualizzato", "permissions": admin_only, "validations": {"length": {"max": 200}}},
    ],
    "groups": [],
}


def mapper(attr: str) -> dict:
    return {
        "name": attr,
        "protocol": "saml",
        "protocolMapper": "saml-user-attribute-mapper",
        "consentRequired": False,
        "config": {
            "user.attribute": attr,
            "attribute.name": attr,
            "friendly.name": attr,
            "attribute.nameformat": "Basic",
            "aggregate.attrs": "false",
        },
    }


def user(username, first, last, number, role):
    return {
        "username": username,
        "enabled": True,
        "emailVerified": True,
        "email": f"{username}@example.test",
        "firstName": first,
        "lastName": last,
        "attributes": {"employeeNumber": [number], "role": [role], "displayName": [f"{first} {last}"]},
        "credentials": [{"type": "password", "value": "Dev-Only-Password-1", "temporary": False}],
        "requiredActions": [],
    }


realm = {
    "realm": "attestia",
    "displayName": "Attestia (sviluppo)",
    "enabled": True,
    "sslRequired": "external",
    "registrationAllowed": False,
    "loginWithEmailAllowed": False,
    "resetPasswordAllowed": False,
    "bruteForceProtected": True,
    "internationalizationEnabled": True,
    "supportedLocales": ["it", "en"],
    "defaultLocale": "it",
    "accessCodeLifespanLogin": 1800,
    "ssoSessionIdleTimeout": 3600,
    "components": {
        "org.keycloak.userprofile.UserProfileProvider": [
            {"providerId": "declarative-user-profile", "subComponents": {},
             "config": {"kc.user.profile.config": [json.dumps(user_profile)]}}
        ]
    },
    "clients": [
        {
            "clientId": SP_ENTITY_ID,
            "name": "Attestia Service Provider",
            "protocol": "saml",
            "enabled": True,
            "publicClient": False,
            "frontchannelLogout": True,
            "fullScopeAllowed": False,
            "baseUrl": f"{SP_BASE}/admin/",
            "redirectUris": [f"{SP_BASE}/sso/*"],
            "attributes": {
                "saml.assertion.signature": "true",
                "saml.server.signature": "true",
                "saml.server.signature.keyinfo.ext": "false",
                "saml.signature.algorithm": "RSA_SHA256",
                "saml_signature_canonicalization_method": "http://www.w3.org/2001/10/xml-exc-c14n#",
                "saml.client.signature": "false",
                "saml.encrypt": "false",
                "saml.authnstatement": "true",
                "saml.force.post.binding": "true",
                "saml_force_name_id_format": "true",
                "saml_name_id_format": "username",
                "saml_assertion_consumer_url_post": f"{SP_BASE}/sso/acs",
                "saml_single_logout_service_url_redirect": f"{SP_BASE}/sso/logout",
                "saml_single_logout_service_url_post": f"{SP_BASE}/sso/logout",
                "saml.assertion.lifespan": "300",
            },
            "protocolMappers": [mapper("employeeNumber"), mapper("role"), mapper("displayName")],
        }
    ],
    "users": [
        user("dipendente", "Nadia", "Verdolino", "E0001", "Employee"),
        user("hr", "Ettore", "Calcagnetti", "E0002", "HR"),
        user("admin", "Livia", "Sbaraglini", "E0003", "Admin"),
    ],
}

out = pathlib.Path(__file__).resolve().parent.parent / "deploy/docker/keycloak/realm-attestia.json"
out.write_text(json.dumps(realm, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"scritto {out}")
