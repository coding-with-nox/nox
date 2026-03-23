# Guida Utente — Nox Dashboard

> Questa guida è rivolta ai **manager e revisori** che usano la dashboard Nox per supervisionare e approvare il lavoro degli agenti AI.

---

## Cos'è Nox?

Nox è un sistema che coordina un team di **agenti AI specializzati** — analisti, sviluppatori, QA, architect — che lavorano insieme come una software house virtuale. Tu sei il **responsabile umano**: gli agenti ti chiedono approvazione nei momenti chiave, e tu decidi se andare avanti, bloccare o modificare.

Non devi scrivere codice. Il tuo ruolo è **revisionare e approvare** il lavoro degli agenti.

---

## Accesso alla Dashboard

1. Apri il browser e vai su **http://localhost:5050** (o l'indirizzo fornito dal team tecnico)
2. Effettua il login con le credenziali fornite
3. Sei sulla **schermata principale** (Dashboard)

---

## Schermata Principale

Appena entri vedi 4 contatori in alto:

| Contatore | Cosa significa |
|-----------|---------------|
| **Pending Reviews** | Quante decisioni stanno aspettando la tua approvazione |
| **Active Flows** | Quanti processi sono in esecuzione in questo momento |
| **Running Agents** | Quanti agenti AI stanno lavorando |
| **Skills Awaiting** | Quante nuove capacità proposte dagli agenti aspettano approvazione |

Sotto trovi:
- **Recent Flow Runs** — i processi avviati di recente con il loro stato
- **Pending Reviews** — le prime 5 revisioni urgenti con un link diretto

> Se "Pending Reviews" è maggiore di zero, vai subito nella sezione **Review Queue** — un agente aspetta la tua risposta.

---

## Menu di Navigazione

| Voce | Sezione | Cosa fai |
|------|---------|----------|
| 🏠 Dashboard | Home | Panoramica generale |
| 🔀 Flow Designer | Orchestration | Visualizza i processi definiti |
| 🤖 Agent Monitor | Orchestration | Monitora gli agenti in tempo reale |
| 🧑‍💼 Review Queue | HITL | **Approva o rifiuta** le richieste degli agenti |
| ⚡ Skill Approval | HITL | Approva nuove capacità proposte dagli agenti |
| 🔌 MCP Servers | HITL | Gestisci gli strumenti esterni degli agenti |

> I numeri rossi/gialli accanto alle voci indicano quante richieste sono in attesa.

---

## Review Queue — Il tuo compito principale

La **Review Queue** (`🧑‍💼 Review Queue`) è il cuore del sistema. Qui gli agenti ti presentano decisioni che non possono prendere da soli.

### Tipi di revisione

| Tipo | Cosa significa |
|------|---------------|
| **Approval** | L'agente chiede il via libera per procedere |
| **Review** | L'agente ha prodotto qualcosa e vuole il tuo parere |
| **DataInput** | L'agente ha bisogno di un'informazione da te |
| **Veto** | Stai decidendo se bloccare un'azione critica |
| **MultiChoice** | Devi scegliere tra più opzioni |

### Come approvare o rifiutare

1. Vai su **Review Queue**
2. Vedi le schede delle richieste pendenti, ordinate per urgenza (quelle in scadenza prima appaiono per prime)
3. Leggi il titolo, la descrizione e il contesto della richiesta
4. Clicca:
   - **✓ Approve** — l'agente procede
   - **✗ Reject** — l'agente si ferma
   - **↑ Escalate** — passi la decisione a un superiore o a un altro revisore
5. Per le richieste **MultiChoice**, vedrai i bottoni con le opzioni — clicca quella che preferisci

> Le decisioni che hai preso appaiono nella tabella **Recent Decisions** in fondo alla pagina.

### La pagina si aggiorna automaticamente

La Review Queue è **in tempo reale**: quando un agente crea una nuova richiesta, appare automaticamente senza ricaricare la pagina. Il pallino verde "Live" in alto a destra lo conferma.

---

## Skill Approval — Nuove capacità degli agenti

Gli agenti possono **proporre nuove capacità** (skills) per lavorare meglio. Prima che vengano attivate, devi approvarle.

1. Vai su **⚡ Skill Approval**
2. Nella sezione **Pending Approval** vedi le proposte
3. Per ogni proposta puoi espandere il dettaglio cliccando "Definition"
4. Clicca **Approve** per attivare la skill o **Reject** per rifiutarla
5. Nella tabella sotto vedi tutte le skills già attive

---

## MCP Servers — Strumenti esterni

Gli agenti usano strumenti esterni (server MCP) come accesso a database, browser web, sistemi git. Quando un agente vuole collegare un nuovo strumento, devi approvarlo.

1. Vai su **🔌 MCP Servers**
2. Vedi la lista dei server già attivi
3. Se un agente ha proposto un nuovo server, compare con stato **PendingApproval**
4. Clicca **Approve** per autorizzarlo, o lascialo in attesa per parlarne col team tecnico

---

## Agent Monitor — Cosa stanno facendo gli agenti

La pagina **🤖 Agent Monitor** mostra lo stato di tutti gli agenti attivi:

| Colore | Stato | Significato |
|--------|-------|-------------|
| 🔵 Blu | Running | L'agente sta lavorando |
| 🟡 Giallo | WaitingForHitl | **L'agente aspetta te** |
| ⚫ Grigio | Suspended / Idle | L'agente è in pausa |
| 🔴 Rosso (barra) | Token alto | L'agente ha usato molta memoria — avvisa il team tecnico |

Puoi filtrare per ID di un flusso specifico usando il campo in alto.

---

## Flow Designer — I processi

La pagina **🔀 Flow Designer** mostra i processi (flow) configurati dal team tecnico. Ogni flow è un insieme di passaggi che gli agenti eseguono in sequenza o in parallelo.

Da qui puoi:
- Vedere quali flow esistono
- Avviare manualmente un flow cliccando **▶ Start Run**
- Vedere le run recenti e il loro stato

---

## Domande frequenti

**Gli agenti stanno lavorando in background anche se chiudo la dashboard?**
Sì. La dashboard è solo un pannello di controllo. I processi continuano finché qualcuno non li ferma o finché non completano.

**Cosa succede se non approvo una richiesta?**
L'agente aspetta. Se la richiesta ha una scadenza (visibile nell'angolo della scheda), dopo quella ora viene automaticamente escalata.

**Posso vedere cosa ha scritto un agente?**
Sì, nella scheda della Review Queue trovi il campo **Context** che mostra il lavoro prodotto dall'agente in formato dettagliato.

**Ho approvato per sbaglio — posso annullare?**
No, le decisioni sono definitive. Contatta il team tecnico per intervenire manualmente sul database.

**Che ruoli esistono?**

| Ruolo | Cosa può fare |
|-------|--------------|
| **Viewer** | Vedere tutto, non può approvare |
| **Manager** | Avviare flow, approvare/rifiutare revisioni |
| **Admin** | Tutto, inclusa la gestione utenti e GDPR |
