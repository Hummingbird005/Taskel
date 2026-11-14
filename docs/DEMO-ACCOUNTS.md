# Demo accounts and sample data

Taskel can fill an empty database with sample accounts and tasks, so the interface can be tried and
tested without setting anything up. **These passwords are public. Never turn this on for a server that real
people use.**

## Turning it on

The data is added when the API starts with `Seed:DemoData` set to `true` **and the database has no accounts
yet**. A database that already has an account is never touched.

- In **Development** it is on: `appsettings.Development.json` sets it. Start the API on an empty database
  (for example right after `docker compose up -d`) and the data is there.
- Anywhere else it is off unless you set it: `Seed__DemoData=true`.
- To start over in development, empty the database and restart the API:
  `docker compose exec postgres psql -U taskel -d taskel -c 'truncate users, groups cascade'`.

The API logs a warning when it adds the data.

## Accounts

All are local accounts: sign in on the **Cont local** tab with the email and password below.

| Role | Name | Email | Password | Department |
|---|---|---|---|---|
| Administrator (initial admin) | Cristina Vasile | `admin@taskel.example` | `Admin-Demo-2026` | IT |
| Manager | Ion Popescu | `manager.vanzari@taskel.example` | `Manager-Demo-2026` | Vânzări |
| Manager | Radu Marin | `manager.suport@taskel.example` | `Manager-Demo-2026` | Suport |
| User | Maria Ionescu | `maria.ionescu@taskel.example` | `User-Demo-2026` | Vânzări |
| User | Andrei Stan | `andrei.stan@taskel.example` | `User-Demo-2026` | Vânzări |
| User | Elena Dumitru | `elena.dumitru@taskel.example` | `User-Demo-2026` | Vânzări |
| User | Dan Pavel | `dan.pavel@taskel.example` | `User-Demo-2026` | Suport |

What each role can do:

- **Administrator**: everything a manager can, for every department; manages roles, departments and every
  application group (**Utilizatori**, **Grupuri**); sets up the mail server, Active Directory, Keycloak and the
  attachment size limit (**Setări sistem**).
- **Manager**: sees the workload, calendar and tasks of the people in their own department (**Echipă**), read
  only; creates groups with people from their department.
- **User**: works on their own tasks and on the tasks given to them.

The administrator above is an ordinary account with the Admin role. On a real server the first administrator
is named with `Auth:AdminEmails` instead (see the README).

## What the sample tasks show

Dates are relative to the day the data is added, so "overdue" and "today" stay meaningful.

| Sign in as | Where to look | What you see |
|---|---|---|
| Ion (manager) | **Acasă**, **Toate task-urile** | Overdue, due today, upcoming, an interval, completed tasks, and a task from an email that needs a reply to complete |
| Ion | **Delegate** | Tasks he created and gave to Maria, Andrei, Elena and to the **Ofertare** group |
| Ion | **Echipă** → Încărcare / Calendar | The Vânzări department: four people, their load and their tasks by day |
| Maria (user) | **De preluat** | *Ofertă pentru licitația Primăriei*, given to the Ofertare group and not yet taken |
| Maria | **Direcționate** | *Pregătește raportul trimestrial*: Ion gave it to her, she passed it on to Andrei; she can still read it |
| Andrei (user) | **Toate task-urile** | The same report, now his, with its path under **Traseu** |
| Radu (manager) | **Echipă** | The Suport department only: himself and Dan |
| Cristina (admin) | **Utilizatori**, **Grupuri**, **Echipă** | Every account, every group, any department |
| Cristina (admin) | **Setări sistem** | The mail server, Active Directory and Keycloak panels, with their test buttons |

## Keycloak accounts (development realm)

`docker compose --profile keycloak up -d` starts a Keycloak with the realm `taskel` from
`deploy/keycloak/taskel-realm.json`. Connect Taskel to it as described in [KEYCLOAK.md](KEYCLOAK.md), then use
**Conectează-te cu …** on the sign-in screen. These passwords are public too.

| Keycloak user | Password | Realm roles | Group | Department | In Taskel |
|---|---|---|---|---|---|
| `ana.admin` | `Keycloak-Demo-2026` | `taskel-user`, `taskel-admin` | – | IT | Administrator |
| `mihai.manager` | `Keycloak-Demo-2026` | `taskel-user`, `taskel-manager` | `/Vanzari` | Vânzări | Manager |
| `irina.user` | `Keycloak-Demo-2026` | `taskel-user` | `/Vanzari/Ofertare` | Vânzări | User |
| `fara.rol` | `Keycloak-Demo-2026` | – | – | – | Refused when `taskel-user` is the required role |

The Keycloak administration console is at http://localhost:8080 (`admin` / `admin`). The client `taskel` has the
secret `taskel-dev-secret`.
