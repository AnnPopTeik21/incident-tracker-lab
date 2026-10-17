# Карта архітектури

Документ описує, з яких частин складається трекер інцидентів (варіант 2-A),
як через них проходять запити і де стоять перевірки. Розділ ЛР 3 описує вхід,
cookie-сеанс і те, звідки сервер бере поточного користувача.
Код тут не повторюється: лише назви файлів і методів, щоб їх можна було знайти пошуком.

## Компоненти

| Компонент | Де лежить | За що відповідає |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` (`index.html`, `app.js`, `styles.css`) | Форма і кнопки, `fetch` до API, показ відповіді через `textContent` |
| Browser client сеансу (ЛР 3) | `Client/auth-lab.html`, `auth-lab.js`, `auth-lab.css` | Реєстрація, вхід, стан сеансу, профіль, створення інциденту, вихід; cookie не читає |
| Identity і cookie (ЛР 3) | `Scaffolding/Lab03Identity.cs`, `Data/Entities/ApplicationUser.cs` | Облікові записи, перевірка пароля, cookie-схема `Identity.Application`, `/api/auth/*`, `/api/me` |
| Security (ЛР 3) | `Security/VerifiedUser.cs`, `Security/LoginThrottling.cs`, `Security/AuthAuditLog.cs` | Id лише з principal, ліміт спроб входу, події автентифікації |
| ASP.NET Core API, рівень Presentation | `Presentation/Endpoints/IncidentEndpoints.cs`, `Presentation/Contracts/` | Маршрути, перевірка параметрів запиту, HTTP-коди, контракти відповіді |
| Application | `Application/Incidents/IncidentQueries.cs`, `Application/Incidents/SeverityBreakdownQuery.cs` | Сценарії читання: список, деталі, підсумок за severity |
| EF Core (Data) | `Data/SecureLabDbContext.cs`, `Data/Entities/`, `Data/Migrations/` | Відображення класів C# на таблиці, побудова SQL-запитів |
| PostgreSQL | `infra/compose.yaml` (контейнер `postgres`) | Зберігає таблиці `incidents`, `incident_comments`, `incident_status_history` і з ЛР 3 `identity_*` (замість `study_users`) |

## Маршрут, змінений у ЛР 1

`GET /api/incidents/severity-summary` з необов'язковим параметром `status`.

```text
кнопка «Порахувати» (#breakdown-button) у Client/index.html
  → обробник submit форми #breakdown-form → loadSeverityBreakdown() у Client/app.js
  → apiFetch("/api/incidents/severity-summary?status=...")
  → IncidentEndpoints.GetSeverityBreakdownAsync (перевіряє status через TryReadStatusFilter)
  → SeverityBreakdownQuery.CountBySeverityAsync (DI, scoped)
  → SecureLabDbContext.Incidents.AsNoTracking().GroupBy(Severity).Count()
  → PostgreSQL: SELECT severity, count(*) FROM incidents [WHERE status = @p] GROUP BY severity
  → доповнення відсутніх рівнів нулями в пам'яті, порядок з Enum.GetValues<IncidentSeverity>()
  → List<SeverityCountDto> → JSON [{ "severity": "...", "count": N }, ...]
  → li.textContent = "Low: 1" у списку #breakdown-list
```

Контракт:

| Що | Значення |
|---|---|
| Метод і URL | `GET /api/incidents/severity-summary` |
| Параметр | `status` (необов'язковий): одна назва з `IncidentStatus` у точному регістрі |
| Тіло запиту | немає |
| Успіх | `200`, `application/json`, масив `SeverityCountDto { severity, count }` |
| Політика нульових груп | повний перелік: завжди чотири елементи, рівень без інцидентів має `count: 0` |
| Порядок | Low, Medium, High, Critical (порядок оголошення enum `IncidentSeverity`) |
| Невалідний `status` | `400`, `application/problem+json`, поле `errors.status` |
| Заборонені поля | description, owner ID, email, коментарі |

Готовий маршрут деталей, який я дослідила на етапі 2:

```text
клік по картці → loadIncidentDetails(id) у app.js
  → GET /api/incidents/{id:guid}
  → IncidentEndpoints.GetDetailsAsync → IncidentQueries.GetDetailsAsync
  → SecureLabDbContext.Incidents → таблиця incidents (+ study_users, incident_comments)
  → IncidentDetailsResponse → JSON → renderIncidentDetails → textContent / createTextNode
  (null із query → 404 Problem Details)
```

## Маршрути, додані в ЛР 2

### Пошук `GET /api/incidents/search`

```text
GET /api/incidents/search?q=...&sortBy=...&order=...
  → Lab02Endpoints.SearchIncidentsAsync (binding q, sortBy, order з query string)
  → IncidentSearchQuery.ResolveOrdering: sortBy шукається у словнику SortKeys
      (createdAtUtc | severity | status, регістр не важливий), order = asc | desc;
      інше → 400 з ключем sortBy або order, до SQL не доходить
  → IncidentSearchQuery.FindAsync
      → IlikeText.AnywhereLiteral(q) (Application/Search): екранує !, % і _ символом !, додає % з обох боків
      → Incidents.AsNoTracking().WhereTitleOrDescriptionLike(pattern) (метод розширення,
        EF.Functions.ILike(Title, pattern, "!") || EF.Functions.ILike(Description, pattern, "!"))
        (pattern стає параметром @pattern; текст команди незмінний)
      → OrderBy / OrderByDescending(вираз із SortKeys) → ThenBy(Id) → Take(50)
  → PostgreSQL: SELECT ... FROM incidents WHERE title ILIKE @pattern ESCAPE '!' OR ... ORDER BY CASE ... LIMIT
  → IncidentSearchItemDto[] (id, title, description, severity, status, createdAtUtc)
```

До виправлення (vulnerable commit `202f7b4`) той самий маршрут склеював `q` і `sortBy` у рядок SQL
у `Scaffolding/Lab02Endpoints.cs` і передавав його у `FromSqlRaw`.

### Створення `POST /api/incidents`

```text
JSON body → CreateIncidentRequest (лише title, description, severity, occurredAtUtc)
  → Lab02Endpoints.CreateIncidentAsync → IncidentCreationService.CreateAsync
      → nowUtc = TimeProvider.GetUtcNow() (один раз)
      → IncidentCreationRules.Check: Trim один раз; required, 160 / 4000, severity лише назва
        (без цифр і ком) + Enum.TryParse + Enum.IsDefined, occurredAtUtc ≤ now + 5 хв,
        High/Critical → опис ≥ 40 символів → 400 ValidationProblem до доступу до БД
      → AnyAsync(Title == title && Status != Closed) → 409 Problem Details
      → Incident: Id, OwnerUserId (з ЛР 3: id перевіреного principal), Status New, CreatedAtUtc, UpdatedAtUtc задає сервер
  → 201 Created + Location /api/incidents/{id} + IncidentCreatedDto
```

Зламаний JSON або дата в неправильному форматі: `BadHttpRequestException` →
`AddExceptionHandler(StatusCodeSelector)` → `400 application/problem+json` без деталей винятку.

## Маршрути ЛР 3: автентифікація

Ланцюжок довіри: пароль іде лише до login → `SignInManager.PasswordSignInAsync` перевіряє його
за збереженим hash → сервер видає зашифровану cookie `.AspNetCore.Identity.Application` →
браузер сам додає її до наступних запитів → `UseAuthentication()` розшифровує cookie і формує
`HttpContext.User` → `UseAuthorization()` пускає далі лише автентифікований запит до endpoint з
`.RequireAuthorization()` → endpoint бере id з claim `NameIdentifier` через `VerifiedUser.TryGetId`.

```text
POST /api/auth/register  (SignUpRequest: userName, email, displayName, password)
  → SignUpRules.Check: Trim імені/email/displayName, userName 4–40 [A-Za-z0-9._-],
    email до 120 і формат, displayName 2–80, password 1–128 → 400 ValidationProblem
  → UserManager.CreateAsync(user, password)  (hash рахує Identity, роль Reporter ставить сервер)
      Password* / InvalidUserName / InvalidEmail → 400 з ключем поля
      DuplicateUserName або DuplicateEmail → 409, один текст для обох
  → 201 Created, Location /api/me, SignUpResult {id, userName, displayName}

POST /api/auth/login  (LoginInput: userName, password)   RequireRateLimiting("login")
  → ліміт: фіксоване вікно, ключ RemoteIpAddress, 4 запити за 30 с → 429 Problem Details + Retry-After
  → PasswordSignInAsync → 204 + Set-Cookie | 401 однаковий для невідомого імені і хибного пароля
  → AuthAuditLog: auth.login result=success userId=… | result=rejected | result=throttled, at, trace

GET /api/me   RequireAuthorization()
  → без cookie 401 Problem Details (OnRedirectToLogin, без 302)
  → MeResponse {userId, userName, roles} лише з claims; X-Demo-UserId не читається

POST /api/auth/logout
  → SignInManager.SignOutAsync() → 204 + Set-Cookie з expires=1970 → наступний /api/me дає 401

POST /api/incidents   RequireAuthorization()
  → ownerUserId = VerifiedUser.TryGetId(HttpContext.User); поле ownerUserId у JSON не входить у DTO
```

Налаштування cookie (надані scaffold, не змінювались): `HttpOnly = true`, `SameSite = Lax`,
`SecurePolicy = SameAsRequest` у Development і `Always` поза ним, строк дії стандартний (14 днів
з продовженням), cookie сеансова (без `expires`). Локальний HTTP не доводить `Secure`: атрибут
з'являється лише в HTTPS-запиті, це перевіряє `SessionCookieTests`.

Залишкові ризики: CSRF для запитів, що змінюють дані, лише частково закриває `SameSite=Lax`
(повна модель у ЛР 6). Реєстрація з відповіддю 409 підтверджує, що ім'я або email зайняті
(свідомий компроміс заради зрозумілої форми). Ліміт входів живе в пам'яті одного процесу.

## Межі довіри

| Межа | Які дані її перетинають | Чого не можна припускати | Де контроль |
|---|---|---|---|
| браузер → API | метод, URL, параметр `status`, path `{id}`, заголовки | що прийде лише значення з `<select>` | `TryReadStatusFilter`: без цифр, ком і пробілів, `Enum.TryParse` з точним регістром + `Enum.IsDefined`; для деталей обмеження маршруту `:guid` |
| API → PostgreSQL | значення фільтра, `id`, `q` пошуку, вибір `sortBy` | що рядок із запиту можна вклеїти в SQL | LINQ і EF Core передають значення параметром (`@requiredStatus`); пошук через LINQ `EF.Functions.ILike` з параметром і екрануванням символом `!`; `sortBy` лише ключ словника серверних виразів |
| JSON body → модель створення | title, description, severity, occurredAtUtc і будь-які зайві поля | що клієнт надішле лише дозволені поля або коректні значення | `CreateIncidentRequest` без owner, status, id, audit-полів; `IncidentCreationRules` на сервері |
| API → браузер | JSON відповіді | що право бачити запис означає право бачити всі поля | окремі DTO: `SeverityCountDto` (2 поля), `IncidentDetailsResponse` без `OwnerUserId` і email |
| відповідь → DOM | текст із JSON (назви рівнів, description) | що текст із бази безпечний як HTML | лише `textContent` і `document.createTextNode`; тест `ClientScript_DoesNotUseDangerousInnerHtmlSink` |
| cookie → principal (ЛР 3) | зашифрована cookie сеансу | що заголовок `X-Demo-UserId`, query чи поле body доводять особу | cookie розшифровує обробник схеми; id лише з `HttpContext.User`; `.RequireAuthorization()` на `/api/me` і `POST /api/incidents` |
| форма входу → API (ЛР 3) | userName, password | що повторні спроби входу безкоштовні | ліміт 4 спроби за 30 с з однієї IP; відмова однакова для будь-якого імені |
| API → журнал (ЛР 3) | події входу, виходу, реєстрації | що у журнал можна писати форму запиту | `AuthAuditLog`: лише тип події, результат, час, userId після успіху, trace id |
| конфігурація → API | connection string, `ASPNETCORE_ENVIRONMENT` | що локальні навчальні значення підходять деінде | реальні значення лише через змінну середовища, `infra/.env` у `.gitignore` |

## Конфігураційні входи

Значення тут не наводяться, лише звідки вони беруться.

| Вхід | Що задає | Хто читає |
|---|---|---|
| `global.json` | нижню межу .NET SDK 10.0.100, `rollForward: latestFeature` (з ЛР 3) | `dotnet` CLI |
| `src/SecureLab.Api/appsettings.json`, `appsettings.Development.json` | рівні журналів, локальний connection string, прапорці міграцій і reset | API під час старту |
| `src/SecureLab.Api/Properties/launchSettings.json` | URL `http://localhost:5080`, середовище Development | `dotnet run` |
| `infra/compose.yaml` + `infra/.env.example` або локальний `infra/.env` | образ PostgreSQL, порт на 127.0.0.1, навчальні облікові дані | Docker Compose |
| секція `Auth:LoginThrottle` в `appsettings.json` | ліміт входів: `PermitLimit` 4, `WindowSeconds` 30 | `LoginThrottling` через `IOptions` |
| змінна середовища `SeedUsers__Password` | пароль чотирьох навчальних акаунтів під час seed і в тестах; у Git його немає | `DbSeeder`, тести |
| змінна середовища `ConnectionStrings__SecureLab` | перевизначає connection string без змін у Git | конфігурація ASP.NET Core (має пріоритет над appsettings) |

Залежність: порт у `infra/.env` (`POSTGRES_PORT`) і порт у `ConnectionStrings__SecureLab` мають збігатися.
Якщо змінено один, треба змінити й другий.

## Повернення до відомого стану

1. Зупинити API (`Ctrl+C`).
2. `dotnet run --no-build --project src/SecureLab.Api -- --reset-database` (з тією самою
   змінною `ConnectionStrings__SecureLab`, якщо порт не стандартний).
3. Команда очищує лише навчальні таблиці (з ЛР 3 також `identity_*`) і заново пише seed: п'ять
   інцидентів (Low 3, Medium 1, High 1), чотири акаунти Identity (alice, bob, morgan, admin) з паролем
   із `SeedUsers__Password`, ролі Reporter, Analyst, Administrator. Працює тільки в Development.
4. Знову запустити API і перевірити `GET /api/incidents` та `GET /api/incidents/severity-summary`.
