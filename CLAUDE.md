# Regole di lavoro per AuroraPAR v2

Progetto: AuroraPAR v2 (C# .NET 9 WPF), repository **ATCRoom/AuroraPAR-v2**, sito https://atcroom.github.io/AuroraPAR-v2/ (cartella `docs/`). Le cose da fare e le idee sono in `TODO.md`.

## Comunicazione
- Rispondi sempre in italiano.
- L'utente prova le build su Windows e le passa ad alcuni amici: ogni build deve essere utilizzabile da chi non è sviluppatore.

## Quando scrivere codice
- Prima proponi; scrivi codice solo dopo un via esplicito ("vai", "parti", "procedi", "ok").
- I bug si correggono subito.
- Le idee vanno in `TODO.md`.
- "Non fare niente" o "aspetta" vuol dire non fare niente.

## Build
- Niente SDK dotnet in locale: si compila solo tramite GitHub Actions (`.github/workflows/build.yml`).
- Gli errori di compilazione si leggono dalle annotations dei check-run.

## Revisioni
- Per lavori grandi o rischiosi, fai fare una revisione da un agente indipendente in sola lettura.

## Immagini pubbliche
- Niente nominativi reali e niente percorsi con il nome utente di Windows dell'utente.

## Versioni
- Ogni modifica a un file in `Profiles/` deve alzare il suo `"BundledVersion"` (altrimenti gli utenti non ricevono l'aggiornamento).
- Alza `<Version>` in `AuroraPAR.csproj` a ogni release (2.0.0 per la finale).

## Repository
- Non fare mai push su **bornac1/AuroraPAR** (il progetto originale).

## Flusso di pubblicazione
1. Commit.
2. Push sul ramo di prova.
3. Attesa della CI.
4. Link ad Actions.
- Su `master` solo quando lo dice l'utente: a quel punto controlla che la release sia aggiornata (Releases, tag `v<Version>`) e dai il link.

## Test e idee
- Per test e idee usa una checklist interattiva con i pulsanti **Reject** e **Frozen**.
- Gli elementi Frozen vanno nella sezione `## Frozen` di `TODO.md`.
