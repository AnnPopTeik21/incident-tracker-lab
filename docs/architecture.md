# Карта архітектури

Документ описує, з яких частин складається трекер інцидентів (варіант 2-A),
як через них проходить запит підсумку за критичністю і де стоять перевірки.
Код тут не повторюється: лише назви файлів і методів, щоб їх можна було знайти пошуком.

## Компоненти

| Компонент | Де лежить | За що відповідає |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` (`index.html`, `app.js`, `styles.css`) | Форма і кнопки, `fetch` до API, показ відповіді через `textContent` |
| ASP.NET Core API, рівень Presentation | `Presentation/Endpoints/IncidentEndpoints.cs`, `Presentation/Contracts/` | Маршрути, перевірка параметрів запиту, HTTP-коди, контракти відповіді |
| Application | `Application/Incidents/IncidentQueries.cs`, `Application/Incidents/SeverityBreakdownQuery.cs` | Сценарії читання: список, деталі, підсумок за severity |
| EF Core (Data) | `Data/SecureLabDbContext.cs`, `Data/Entities/`, `Data/Migrations/` | Відображення класів C# на таблиці, побудова SQL-запитів |
| PostgreSQL | `infra/compose.yaml` (контейнер `postgres`) | Зберігає таблиці `incidents`, `study_users`, `incident_comments`, `incident_status_history` |

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

## Межі довіри

| Межа | Які дані її перетинають | Чого не можна припускати | Де контроль |
|---|---|---|---|
| браузер → API | метод, URL, параметр `status`, path `{id}`, заголовки | що прийде лише значення з `<select>` | `TryReadStatusFilter`: без цифр, ком і пробілів, `Enum.TryParse` з точним регістром + `Enum.IsDefined`; для деталей обмеження маршруту `:guid` |
| API → PostgreSQL | значення фільтра, `id` | що рядок із запиту можна вклеїти в SQL | LINQ і EF Core передають значення параметром (`@requiredStatus`), `AsNoTracking()` для читання |
| API → браузер | JSON відповіді | що право бачити запис означає право бачити всі поля | окремі DTO: `SeverityCountDto` (2 поля), `IncidentDetailsResponse` без `OwnerUserId` і email |
| відповідь → DOM | текст із JSON (назви рівнів, description) | що текст із бази безпечний як HTML | лише `textContent` і `document.createTextNode`; тест `ClientScript_DoesNotUseDangerousInnerHtmlSink` |
| конфігурація → API | connection string, `ASPNETCORE_ENVIRONMENT` | що локальні навчальні значення підходять деінде | реальні значення лише через змінну середовища, `infra/.env` у `.gitignore` |

## Конфігураційні входи

Значення тут не наводяться, лише звідки вони беруться.

| Вхід | Що задає | Хто читає |
|---|---|---|
| `global.json` | смугу .NET SDK 10.0.3xx, `rollForward: latestPatch` | `dotnet` CLI |
| `src/SecureLab.Api/appsettings.json`, `appsettings.Development.json` | рівні журналів, локальний connection string, прапорці міграцій і reset | API під час старту |
| `src/SecureLab.Api/Properties/launchSettings.json` | URL `http://localhost:5080`, середовище Development | `dotnet run` |
| `infra/compose.yaml` + `infra/.env.example` або локальний `infra/.env` | образ PostgreSQL, порт на 127.0.0.1, навчальні облікові дані | Docker Compose |
| змінна середовища `ConnectionStrings__SecureLab` | перевизначає connection string без змін у Git | конфігурація ASP.NET Core (має пріоритет над appsettings) |

Залежність: порт у `infra/.env` (`POSTGRES_PORT`) і порт у `ConnectionStrings__SecureLab` мають збігатися.
Якщо змінено один, треба змінити й другий.

## Повернення до відомого стану

1. Зупинити API (`Ctrl+C`).
2. `dotnet run --no-build --project src/SecureLab.Api -- --reset-database` (з тією самою
   змінною `ConnectionStrings__SecureLab`, якщо порт не стандартний).
3. Команда очищує лише навчальні таблиці і заново пише seed: три інциденти (Low, Medium, High),
   чотири користувачі, два публічні коментарі. Працює тільки в Development.
4. Знову запустити API і перевірити `GET /api/incidents` та `GET /api/incidents/severity-summary`.
