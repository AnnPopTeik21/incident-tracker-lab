# Звіт ЛР04

Виконала: Чубукіна Анна Олександрівна, КБ-41. Повний звіт з рисунками подано окремим файлом .docx.
Паролів, значень cookie, CSRF-токенів і connection string тут немає.

## Контекст і межа виконання
- Рівень: відмінний («Відмінно»). Виконано весь добрий рівень, A-11 для search і severity-summary,
  окремий finding A-11, відокремлений сервіс правил і обґрунтування в `docs/access-matrix.md`.
- Baseline 2-A, base tag `v0.3.0` = `f591bff`, гілка `lab/4-authorization` від нього.
- Головна action: mutation, `PATCH /api/incidents/{id}` (зміна title/description). Політика: **403** для
  чужого запису, **404** лише для неіснуючого id. Маршрути мого рівня: list, details, search,
  severity-summary, PATCH, DELETE, `POST /api/incidents/{id}/triage`, `GET /api/incident-comments/{id}`.
- Виправлень ЛР 3 після тегу не було, стартові перевірки пройшли: alice → `/api/me` 200 з id …0001 і роллю
  Reporter, morgan → роль Analyst, анонімний `/api/me` і `POST /api/incidents` → 401. Тести на `v0.3.0`: 91 з 91.

## Матриця і server-side рішення
Матриця: `docs/access-matrix.md`.
- `Application/Access/CallerProfile.cs`: id і роль лише з `HttpContext.User`.
- `Application/Access/IncidentAccessGuard.cs`: єдине місце правил. Метод `Decide(caller, action, facts)`
  повертає `AccessVerdict` (Granted, Refused, Absent); `DecideComment` спершу перевіряє parent, потім internal.
- `Application/Access/IncidentScope.cs`: умова за власником у самому SQL для list, search і severity-summary.
- `Application/Incidents/IncidentChangeService.cs`: tracked entity → рішення сторожа → перевірка змісту
  або статусу → зміна → `SaveChangesAsync`.
- DTO: `UpdateIncidentRequest(title, description)` і `TriageIncidentRequest(note)` з
  `JsonUnmappedMemberHandling.Disallow`. Owner, status, severity, role сервер не приймає.
- Аудит: `Security/AccessDenialLog.cs` (`LoggerMessage`, EventId 4101, категорія `SecureLab.AccessAudit`).

## Вразливий стан і PoC
- Vulnerable commit `8c49da4`: `PATCH /api/incidents/{id}` шукав запис лише за id, без власника.
  Попередній коміт `60ac45a` додав internal comment і каркас звіту.
- Alice → власний інцидент …0001: 200. Alice → інцидент Bob …0002 з тією самою cookie і CSRF-токеном,
  змінено лише id: **200**, Bob після цього бачить заголовок «Заголовок змінено з облікового запису Alice».
- Окремо: у стані `v0.3.0` `GET /api/incidents/{id}`, `search` і `severity-summary` відповідали навіть без входу.

## Виправлення та retest
- Fixed commit `cd218cf`, тег `v0.4.0` на ньому.
- Той самий PATCH Alice → Bob: **403** «Доступ до інциденту заборонено», заголовок Bob без змін; DELETE теж 403.
- Власний PATCH Alice: 200, змінився лише опис.
- Helper `login-and-read.py --user alice --incident bob`: `read_status=403`; `--user morgan --incident alice`: 200.
- Неіснуючий id: 404 «Інцидент не знайдено». A-10 не застосовується (політика 403).

## Доказова таблиця

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|---|
| A-01 | добрий | Анонімний доступ | немає cookie | 8 захищених маршрутів | `401`, не redirect | 401 problem+json, без Location, на всіх 8 | `OwnershipBoundaryTests.Anonymous_Gets401Problem_NotRedirect` |
| A-02 | достатній | Власний ресурс | Alice, власний запис | PATCH description | allow | 200, змінився лише опис, owner/status/severity ті самі | `ForeignIncidentChangeTests.Owner_ChangesDescription_AndOnlyContentMoves`; details: `Owner_ReadsOwnDetails` |
| A-03 | достатній | Чужий ресурс | Alice, інцидент Bob | той самий PATCH з id Bob | 403, стан без змін | 403, знімок рядка до і після однаковий | `Reporter_PatchingBobsIncident_GetsForbidden_AndRecordStaysTheSame`, PowerShell |
| A-03R | достатній | Аналітичний read | Morgan, інцидент Alice | GET details | allow, без права mutation | 200; PATCH Morgan → 403, стан без змін. Details owner/non-owner: 200 / 403 | `Analyst_ReadsReporterIncident_ForAnalysis`, `Analyst_PatchingReporterIncident_*`, `OtherReporter_GetsForbidden_*`, `.http` |
| A-04 | добрий | Усі змінювальні дії | Alice, інцидент Bob | DELETE id Bob | deny, стан без змін | 403, запис на місці | `Reporter_DeletingBobsIncident_*` |
| A-05 | добрий | List | Alice | GET list | лише свої | 3 записи, усі Alice; Bob 2 свої; Morgan усі | `ReadPathScopeTests.List_*` |
| A-06 | добрий | Вертикальне підвищення | Alice, власний New …0003 | triage | 403, status New | 403, статус New | `Reporter_TriagingOwnNewIncident_*` |
| A-07 | добрий | Дозволена привілейована дія | Morgan, новий запис New | triage | allow | 200, New → Triaged, рядок історії від Morgan; повтор 409 | `Analyst_TriagesNewIncident_*`, `Analyst_OnAlreadyTriagedIncident_GetsConflict_*` |
| A-08a | добрий | Ігнорування поля | — | — | — | не виконувалось: обрано A-08b | — |
| A-08b | добрий | Overposting відхиляється | Alice, власний запис | PATCH з ownerUserId / status / severity / role / isTransitionAllowed | 400, стан без змін | 400 для кожного поля, рядок без змін (навіть title) | `ExtraProtectedField_Gets400_AndWholeRecordStays` (5 випадків), triage зі `status` → 400 |
| A-09 | добрий | Audit event | відмова triage | знайти `authorization.denied` | без секретів, той самий trace | subjectId, action, resource, result, час UTC, trace = traceId відповіді; імені, пароля, токена, нотатки немає | `DeniedTriage_LeavesSafeAuditEvent_WithResponseTraceId`, журнал API |
| A-10 | добрий, лише 404 | Нерозрізнюваність | — | — | — | не застосовується: політика 403 (обґрунтування в матриці) | `UnknownId_GetsNotFound_WhichDiffersFromForeignRefusal` фіксує 403 і 404 |
| A-11 | відмінний | search і severity-summary | Alice | `search?q=O'Brien`, summary | лише свої | search: `[]` (до: запис Bob …0004); summary: Low 2, Medium 1, High 0 (до: 3/1/1) | `ReadPathScopeTests.Search_*`, `SeveritySummary_*` |
| A-12 | добрий | Child і internal | Alice; comment-2, comment-3 | GET comment | deny; internal не видно | comment-2 → 403, comment-3 → 403, comment-1 → 200; details Alice без нотатки, Morgan з нотаткою | `ChildResourceTests` |

## Основний finding і залишковий ризик
1. Контекст: Incident, дія PATCH (зміна змісту), локальні Alice і Bob (Reporter).
2. Гіпотеза: Alice змінить запис Bob, підставивши його id.
3. До: `8c49da4`, PATCH Alice → …0002 дав 200, заголовок Bob змінився.
4. Причина: `IncidentChangeService.ChangeContentAsync` шукав запис лише за id і одразу змінював поля.
   Порівняння з principal і рішення за матрицею не було.
5. Виправлення: виклик `IncidentAccessGuard.Decide(caller, EditContent, facts)` до будь-якої зміни; DTO
   лише title і description, зайве поле дає 400.
6. Retest: той самий запит → 403, стан Bob без змін.
7. Позитивна регресія: власний PATCH 200, Analyst читає details, Analyst робить triage.
8. Залишковий ризик: одночасні triage без захисту версії; 403 підтверджує існування id; роль у cookie діє
   до повторного входу. CSRF-токен з ЛР 3 тепер захищає PATCH, DELETE і triage; повний огляд у ЛР 6.

Схема: запит Alice з id → cookie дає principal → `CallerProfile` → факти запису (власник, статус) →
`IncidentAccessGuard` → 403/404 або дозвіл → зміна і `SaveChangesAsync` лише після дозволу.

## Finding A-11 (відмінний рівень)
- До: `GET /api/incidents/search?q=O'Brien` з cookie Alice повертав інцидент Bob …0004 з описом.
  Без cookie той самий запит теж давав 200. Severity-summary для Alice рахував High 1 з інциденту Bob.
- Причина: `IncidentSearchQuery.FindAsync` і `SeverityBreakdownQuery` будували запит від усієї таблиці,
  маршрути не мали `RequireAuthorization`.
- Виправлення: `IncidentScope.VisibleTo` стоїть першим у запиті, до ILIKE, сортування, `Take(50)` і `GroupBy`;
  обидва маршрути вимагають входу.
- Після: Alice отримує `[]`, Bob свій запис; summary Alice Low 2, Medium 1, High 0; анонім 401.

## Browser flow
Login alice на `auth-lab.html` → головна сторінка (у списку лише 3 записи Alice) → деталі «Підозрілий лист
із вкладенням» → поле «Відкрити за id» з id Bob → «Помилка: 403 Доступ до інциденту заборонено».
Відмова пишеться через `textContent`, у `app.js` немає `innerHTML`.

## Відмінний рівень
- Сервіс правил відокремлений від endpoint-ів і зареєстрований у DI; endpoint-и лише питають його.
- Перевірка тестами: якщо сторож завжди дозволяє, 12 з 31 тесту трьох класів стають червоними.
- Least privilege, видимість ресурсу і залишкові ризики: розділи в `docs/access-matrix.md`.
- Batch/export у проєкті немає; сценарію одночасних переходів викладач не давав.

## Перевірки, відтворення і подання
- Windows 11, PowerShell 7, .NET SDK 10.0.302, PostgreSQL 18 у Docker Desktop (порт 54331, `infra/.env`).
- `docker compose --env-file infra/.env -f infra/compose.yaml up -d --wait`,
  `dotnet run --project src/SecureLab.Api -- --reset-database`,
  `dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release`.
- Підсумок після останньої зміни: 136 з 136, з них 45 нових. Build: 0 попереджень.
- Коміти: `60ac45a` (seed і каркас звіту), vulnerable `8c49da4`, fixed `cd218cf` = `v0.4.0`;
  цей звіт з hash іде наступним комітом після тегу.

## Використання ШІ
ШІ не використовувався. Журнал: не застосовується.
