#!/usr/bin/env python3
"""Controlli statici dei sorgenti ASP e dei loro contratti con SQL Server e SSO."""
from __future__ import annotations

import codecs
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PORTAL = ROOT / "legacy/portale"
HISTORY = "dbo.usp_employeecertificatehistory"
VB_STRING = r'"(?:[^"\n]|"")*"'
SQL_KEYWORD = re.compile(r"\b(?:SELECT|INSERT|UPDATE|DELETE|EXEC(?:UTE)?)\b", re.I)
SAFE_HELPER = re.compile(r"(?:H|JsonString|IsoDate|JsonNumber|JsonBoolean)\s*\(", re.I)
BLOCK = re.compile(r"<%(?!@)(.*?)%>|<script\b([^>]*)>(.*?)</script\s*>", re.I | re.S)
DIRECTIVE = re.compile(r"\A<%@\s*Language\s*=\s*\"?VBScript\"?\b[^\r\n]*?%>[ \t]*\n", re.I)


def read_contracts():
    procedures = {}
    bodies = {}
    for path in sorted((ROOT / "db/migrations").glob("*.sql")):
        sql = path.read_text(encoding="utf-8-sig")
        sql = re.sub(r"'(?:''|[^'])*'|--[^\n]*|/\*.*?\*/",
                     lambda m: "''" if m[0].startswith("'") else " ", sql, flags=re.S)
        for batch in re.split(r"^\s*GO\s*$", sql, flags=re.I | re.M):
            match = re.search(r"\bCREATE\s+OR\s+ALTER\s+PROC(?:EDURE)?\s+([\w.\[\]]+)(.*?)\bAS\b(.*)",
                              batch, re.I | re.S)
            if match:
                name = re.sub(r"[\[\]]", "", match[1]).lower()
                procedures[name] = set(re.findall(r"@\w+", match[2].lower()))
                bodies[name] = match[3]
    if HISTORY not in bodies:
        raise ValueError("Procedura dello storico assente dalle migrazioni.")
    # La prima proiezione al livello esterno stabilisce i nomi anche per UNION ALL.
    columns = set()
    depth = 0
    collecting = False
    last_name = ""
    for token in re.findall(r"\[[^\]]+\]|[\w@]+|[(),.]", bodies[HISTORY]):
        word = token.strip("[]").lower()
        if token == "(":
            depth += 1
        elif token == ")":
            depth -= 1
        elif depth == 0:
            if not collecting:
                collecting = word == "select"
            elif word in {",", "from"}:
                if not re.fullmatch(r"\w+", last_name):
                    raise ValueError("Colonna della proiezione SQL non riconosciuta.")
                columns.add(last_name)
                last_name = ""
                if word == "from":
                    break
            else:
                last_name = word
    if not columns:
        raise ValueError("Proiezione della procedura dello storico assente.")
    return procedures, columns


def statements(source):
    """Estrae le istruzioni VB, mantenendo le righe e ignorando i commenti."""
    for block in BLOCK.finditer(source):
        if block[1] is None:
            if not (re.search(r"\brunat\s*=\s*['\"]?server\b", block[2], re.I)
                    and re.search(r"\blanguage\s*=\s*['\"]?VBScript\b", block[2], re.I)):
                continue
            group = 3
        else:
            group = 1
        first = source.count("\n", 0, block.start(group)) + 1
        pending = ""
        for line, raw in enumerate(block[group].splitlines(), first):
            # Gli apostrofi e i due punti dentro le stringhe appartengono al dato.
            raw = re.sub(VB_STRING + r"|'.*$", lambda m: m[0] if m[0][0] == '"' else "", raw)
            if not pending:
                start = line
            pending += raw.strip()
            if pending.endswith(" _"):
                pending = pending[:-1]
                continue
            for match in re.finditer(r"(?:" + VB_STRING + r'|[^:"])+', pending):
                statement = match[0].strip()
                if re.match(r"rem(?:\s|$)", statement, re.I):
                    break
                if statement:
                    yield start, statement
            pending = ""


def safe_output(argument):
    argument = argument.strip()
    # Comprende anche Response.Write(...) senza consentire testo dopo la parentesi finale.
    def outer_call(expression, opening):
        depth = 0
        for i, char in enumerate(re.sub(VB_STRING, lambda m: " " * len(m[0]), expression)):
            if i < opening:
                continue
            depth += (char == "(") - (char == ")")
            if depth == 0:
                return i == len(expression) - 1
        return False

    if argument.startswith("(") and outer_call(argument, 0):
        argument = argument[1:-1].strip()
    if re.fullmatch(VB_STRING, argument):
        return True
    helper = SAFE_HELPER.match(argument)
    return bool(helper and outer_call(argument, helper.end() - 1))


def inspect_vb(source, path, procedures, columns, report):
    commands = {}
    rows = list(statements(source))
    is_include = path.parent.name.lower() == "include"
    if path.suffix.lower() == ".asp" and not is_include:
        if not DIRECTIVE.match(source):
            report(1, "Direttiva VBScript mancante in prima riga.")
        if not rows or rows[0][1].lower() != "option explicit":
            report(rows[0][0] if rows else 1, "Option Explicit deve essere la prima istruzione.")
    if is_include:
        # Un include viene copiato nella pagina: una seconda direttiva @ (ASP 0141) o un secondo
        # Option Explicit impedirebbero la compilazione. Li dichiara la pagina che lo include.
        if re.search(r"<%\s*@", source):
            report(1, "Un file incluso non deve contenere la direttiva @.")
        if any(statement.lower() == "option explicit" for _, statement in rows):
            report(1, "Un file incluso non deve ripetere Option Explicit.")
    for line, statement in rows:
        masked = re.sub(VB_STRING, lambda m: " " * len(m[0]), statement)
        if re.match(r"(?:end\s+)?(?:function|sub)\b", masked, re.I):
            commands.clear()
        for literal in re.findall(VB_STRING, statement):
            if SQL_KEYWORD.search(literal):
                report(line, "Parola chiave SQL in una stringa VBScript.")
        for call in re.finditer(r"\.\s*Execute\b", masked, re.I):
            tail = statement[call.end():].strip()
            if tail not in {"", "()"}:
                report(line, "Execute deve usare il comando parametrizzato senza testo SQL.")
        for command in re.finditer(r"\b(\w+)\.CommandText\s*=", masked, re.I):
            value = statement[command.end():].strip()
            name = value[1:-1].lower() if re.fullmatch(VB_STRING, value) else ""
            commands[command[1].lower()] = name
            if name not in procedures:
                report(line, "CommandText non identifica una procedura delle migrazioni.")
        for parameter in re.finditer(r"\b(\w+)\.CreateParameter\b", masked, re.I):
            literal = re.match(r'\s*\(?\s*"(@\w+)"', statement[parameter.end():])
            name = commands.get(parameter[1].lower())
            if not literal or literal[1].lower() not in procedures.get(name, set()):
                report(line, "Parametro assente nella procedura associata al comando.")
        for field in re.finditer(r"\brs\s*(?:\.\s*Fields\s*)?\(", masked, re.I):
            literal = re.match(r'\s*"(\w+)"\s*\)', statement[field.end():])
            if not literal or literal[1].lower() not in columns:
                report(line, "Colonna assente nella proiezione della procedura dello storico.")
        outputs = [statement[m.end():] for m in re.finditer(r"\bResponse\s*\.\s*Write\b", masked, re.I)]
        if statement.startswith("="):
            outputs.append(statement[1:])
        for argument in outputs:
            if not safe_output(argument):
                report(line, "Espressione di output non protetta: " + argument.strip())


def cookie_contract(source, errors):
    path = ROOT / "src/Attestia.Core/Sso/SsoCookie.cs"
    if not path.exists():
        print("Avviso: SsoCookie.cs assente; confronto con il contratto .NET saltato.")
    csharp = path.read_text(encoding="utf-8-sig") if path.exists() else None
    for key, expected in (("CookieName", "attestia_sso"), ("CookieVersion", "v1")):
        declaration = re.search(r'^\s*Const\s+' + key + r'\s*=\s*"([^"]+)"', source, re.M | re.I)
        if not declaration or declaration[1] != expected:
            errors.append(f"include/auth.asp: costante {key} difforme dal contratto.")
        if csharp is not None:
            constants = re.findall(r'\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"\s*;', csharp)
            endings = ("name",) if key == "CookieName" else ("version", "prefix")
            named = [value for name, value in constants if name.lower().endswith(endings)]
            if expected not in named:
                errors.append(f"SsoCookie.cs: valore della costante {key} assente o difforme.")


def main():
    errors = []
    procedures, columns = read_contracts()
    count = 0
    auth = ""
    for path in sorted(PORTAL.rglob("*")):
        relative = path.relative_to(PORTAL)
        if relative.parts[:2] == ("assets", "vendor"):
            continue
        suffix = path.suffix.lower()
        if suffix not in {".asp", ".asa", ".js", ".css"}:
            continue

        def report(line, message):
            errors.append(f"{relative}:{line}: {message}")

        raw = path.read_bytes()
        if suffix in {".asp", ".asa"}:
            if raw.startswith(codecs.BOM_UTF8):
                report(1, "BOM UTF-8 presente.")
            if re.search(rb"(?<!\r)\n|\r(?!\n)", raw):
                report(1, "Terminazioni di riga diverse da CRLF.")
        try:
            source = raw.decode("utf-8").replace("\r\n", "\n")
        except UnicodeDecodeError:
            report(1, "Codifica UTF-8 non valida.")
            continue
        count += 1
        if relative.as_posix() == "include/auth.asp":
            auth = source
        if suffix in {".asp", ".asa"}:
            inspect_vb(source, path, procedures, columns, report)
        if suffix != ".asa":
            for line, value in enumerate(source.splitlines(), 1):
                if re.search(r"https?://", value, re.I):
                    report(line, "Riferimento HTTP esterno: usare risorse locali.")
    cookie_contract(auth, errors)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        print(f"Controllo fallito con {len(errors)} segnalazioni.", file=sys.stderr)
        return 1
    print(f"Verifica superata: {count} sorgenti, {len(procedures)} procedure censite, "
          f"{len(columns)} colonne nello storico.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as error:
        sys.exit(f"Verifica interrotta: {error}")
