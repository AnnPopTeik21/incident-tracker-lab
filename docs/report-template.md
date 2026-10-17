# Звіт до лабораторної роботи № 2

Виконала: Чубукіна Анна Олександрівна, КБ-41. Повний звіт з рисунками подано окремим файлом .docx.

## 1. Ідентифікація стану

- Варіант: baseline 2-A «Трекер інцидентів», профіль A «Пошук» (індивідуальних варіантів у ЛР 2 немає).
- Вхід: власний тег `v0.1.0` (`8ba3147`) + scaffold `lab-02-start-v1` (`.scaffolds/lab-02.json`).
- Робоча гілка: `lab/2-input-sqli` (створена від `v0.1.0`).
- Проміжний коміт: scaffold і тест S-01, який фіксує уразливий пошук: `6eb0315`.
- Vulnerable commit (контракт створення є, пошук ще уразливий): `202f7b4`.
- Fixed commit (пошук через LINQ ILike, sortBy з allowlist): `0b40b7b`.
- Фінальний тег: `v0.2.0` (annotated) на останньому коміті гілки; hash тегу записано у звіті .docx.

## 2. Змінені маршрути

```text
GET /api/incidents/search?q&sortBy&order
  → Lab02Endpoints.SearchIncidentsAsync
  → IncidentSearchQuery.ResolveOrdering (словник SortKeys) → 400 для невідомого ключа
  → IncidentSearchQuery.FindAsync → IlikeText.AnywhereLiteral(q)
  → WhereTitleOrDescriptionLike(pattern): EF.Functions.ILike(..., "!") → ILIKE @pattern ESCAPE '!'
  → IncidentSearchItemDto[]

POST /api/incidents
  → CreateIncidentRequest → IncidentCreationService.CreateAsync
  → IncidentCreationRules.Check → 400 | AnyAsync(дублікат) → 409 | SaveChanges → 201 IncidentCreatedDto
```

Root cause до виправлення: `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`, обробник
`MapGet("/api/incidents/search", ...)`, рядки 21–23 у `202f7b4`: `var sql = "SELECT ... ILIKE '%" + (q ?? "") + ...`
і `ORDER BY " + order`, потім `FromSqlRaw(sql)`.

## 3. Виконані зміни

| Файл | Що змінено |
|---|---|
| `Presentation/Contracts/CreateIncidentContracts.cs` | `CreateIncidentRequest` (4 поля) і `IncidentCreatedDto` (6 полів) |
| `Application/Incidents/IncidentCreationRules.cs` | серверні перевірки 2-A, Trim один раз, cross-field правило |
| `Application/Incidents/IncidentCreationService.cs` | перевірка дубліката (409), entity з серверними полями |
| `Presentation/Contracts/SearchContracts.cs` | `IncidentSearchItemDto` з тими самими шістьма полями, що в scaffold |
| `Application/Search/IlikeText.cs` | `AnywhereLiteral`: екранування `!`, `%`, `_` символом `!` |
| `Application/Search/IncidentTextMatching.cs` | метод розширення `WhereTitleOrDescriptionLike` з `EF.Functions.ILike` |
| `Application/Incidents/IncidentSearchQuery.cs` | LINQ-запит, словник `SortKeys`, параметр `order` |
| `Scaffolding/Lab02Endpoints.cs` | тонкі обробники, metadata для OpenAPI |
| `Program.cs` | DI для нових класів, `TimeProvider.System`, `StatusCodeSelector` для 400 на зламаний JSON |
| `tests/SecureLab.Api.Tests/CreateIncidentContractTests.cs` | T-01, T-02, T-03, T-06, T-09, CP-01 |
| `tests/SecureLab.Api.Tests/SearchInjectionTests.cs` | S-01 (у vulnerable commit), S-02, T-04, T-05, буквальні метасимволи, сортування |
| `tests/SecureLab.Api.Tests/SeverityBreakdownTests.cs` | лише очікувані числа: Low 3 замість 1 (новий seed) |
| `tests/http/incident-search-create.http` | ручні сценарії ЛР 2 |

## 4. Перевірка

PostgreSQL на моєму ПК слухає 54331 (`infra/.env`), бо 54329 зайнятий іншим локальним сервісом.

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | reset, унікальний title | POST /api/incidents | 201, DTO без зайвих полів | 201, Location, 6 полів, status New | тест `T01_...`, .http |
| T-02 | — | POST із severity "7", порожнім title, без occurredAtUtc, з датою +10 хв | 400 problem+json | 400, ключ відповідного поля, без внутрішніх деталей | тест `T02_...` (10 випадків + зламаний JSON) |
| T-03 | seed «Невідома спроба входу» (InProgress) | повторний POST із тим самим title | 409 | 409 Problem Details; інший регістр дає 201; Closed не блокує | тести `T03_...` |
| S-01 | `202f7b4`, reset | GET search?q=`nothing%' OR 1=1 --` | небажано розширена вибірка | 200 і всі 5 записів seed (для q=nothing: 0) | тест `S01_...` у `202f7b4` (доданий у `6eb0315`) |
| S-02 | `0b40b7b`, reset | той самий ввід | порожній результат | 200 і [] | тест `S02_...` |
| T-04 | `0b40b7b` | q=лист, q=комп'ютерного | коректні записи, без 500 | по 1 запису; до fix апостроф давав 500 | тести `NormalSearch_...`, `T04_...` |
| T-05 | `0b40b7b` | sortBy=title, `created_at_utc DESC`, `severity;`, 1; order=sideways | 400 | 400, ключ sortBy або order | тест `T05_...` |
| T-06 | — | GET /api/incidents/99999999-9999-9999-9999-999999999999 | 404 Problem Details | 404, «Інцидент не знайдено» | тест `T06_...` |
| T-09 | унікальні title | High/Critical, опис 39 і 40 символів після Trim | 400 (description), потім 201 | 400 з ключем description; 201 | тест `T09_...` (4 випадки) |
| A-01 | `0b40b7b` | code search FromSqlRaw, ExecuteSqlRaw, $"SELECT, + query, ORDER BY | кожен збіг класифіковано | одна точка raw SQL (TRUNCATE у reset), безпечна; пошук на LINQ | таблиця |

Автоматична перевірка після останньої зміни коду: `dotnet test ... --configuration Release` — 58 тестів, 58 пройшли.

### A-01. Точки доступу до даних

| Файл / метод | Механізм | Категорія | Висновок |
|---|---|---|---|
| `IncidentSearchQuery.FindAsync` | LINQ `WhereTitleOrDescriptionLike`, `EF.Functions.ILike` | параметризоване значення | pattern іде параметром; ORDER BY з LINQ-виразу словника |
| `DatabaseBootstrap.InitializeAsync` | `ExecuteSqlRawAsync("TRUNCATE ...")` | trusted static SQL | константа без вводу, лише Development + AllowReset |
| `IncidentQueries.GetListAsync`, `GetDetailsAsync` | LINQ `Where`, `Select` | параметризоване значення | status і id ідуть параметрами |
| `SeverityBreakdownQuery.CountBySeverityAsync` | LINQ `GroupBy` | параметризоване значення | фільтр status параметром |
| `IncidentCreationService.CreateAsync` | LINQ `AnyAsync`, `Add`, `SaveChangesAsync` | параметризоване значення | INSERT будує EF Core з параметрами |
| `DbSeeder`, `Lab02Seed` | LINQ, `AddRange` | trusted static data | лише константи seed |
| `Program.cs` /health | `CanConnectAsync` | без SQL від клієнта | — |

## 5. Security-сценарій

1. Контекст і гіпотеза: пошук отримує недовірені `q` і `sortBy` з URL; вони можуть потрапити у структуру SQL.
2. Стан до: vulnerable commit `202f7b4` (S-01 доданий у `6eb0315`), `Lab02Endpoints.cs`, склеювання рядка `sql` і `FromSqlRaw(sql)`.
3. PoC: тест `S01_ControlInput_OnVulnerableState_WidensResultToEveryIncident`, один read-only ввід.
4. Спостереження: `q=nothing` дає 0 записів, контрольний ввід дає всі 5; `q=комп'ютерного` дає 500.
5. Першопричина: апостроф закривав літерал SQL, `OR 1=1` ставав умовою, `--` коментував решту, разом з ORDER BY.
   `sortBy` поза трьома відомими значеннями йшов у ORDER BY як є.
6. Виправлення: LINQ з `EF.Functions.ILike`, екранування `!`, `%`, `_` символом `!`; `sortBy` лише ключ словника серверних виразів.
7. Retest: той самий ввід дає 200 і []; лист, комп'ютерного, D'Angelo знаходяться; `%`, `_`, `!`, `\` шукаються буквально.
8. Залишковий ризик: немає автентифікації (owner з demo context до ЛР 3), немає пагінації понад LIMIT 50,
   немає унікального обмеження title у БД для паралельних POST, інші endpoint потребують окремого огляду.

## 6. Висновок

Створення інциденту має явний контракт і перевіряється на сервері. Пошук більше не змішує дані зі структурою
SQL: значення йде параметром, сортування обирає сервер. 58 тестів зелені, секретів у diff немає.
