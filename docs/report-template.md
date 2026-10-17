# Звіт до лабораторної роботи № 1

Виконала: Чубукіна Анна Олександрівна, КБ-41. Повний звіт з рисунками подано окремим файлом .docx.

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів» (індивідуальних профілів у ЛР 1 немає).
- Робоча гілка: `lab/1-system`.
- Основна гілка: `main` (злиття `--no-ff`).
- Фінальний тег: `v0.1.0` (annotated, стоїть на merge-коміті в `main`).
- Commit hash перевіреного коду на робочій гілці: `b361a70`.
- Hash самого тегу `v0.1.0` записано у звіті .docx: файл у репозиторії не може містити hash коміту, у якому лежить.

## 2. Змінений маршрут

```text
кнопка «Порахувати» → loadSeverityBreakdown() (Client/app.js)
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeverityBreakdownAsync → TryReadStatusFilter
  → SeverityBreakdownQuery.CountBySeverityAsync
  → SecureLabDbContext.Incidents → incidents (GROUP BY severity)
  → SeverityCountDto[] як JSON → li.textContent у #breakdown-list
```

Докладна карта, межі довіри і конфігураційні входи: `docs/architecture.md`.

## 3. Виконані зміни

| Файл | Що змінено |
|---|---|
| `Application/Incidents/SeverityBreakdownQuery.cs` | новий клас запиту: `AsNoTracking`, `GroupBy`, `Count`, `ToListAsync`, доповнення нулями, порядок з enum, структурований log |
| `Presentation/Contracts/SeverityCountDto.cs` | новий DTO з двома полями: `Severity`, `Count` |
| `Presentation/Endpoints/IncidentEndpoints.cs` | замість заглушки 501 метод `GetSeverityBreakdownAsync`, перевірка `status` у `TryReadStatusFilter`, metadata `Produces<IReadOnlyList<SeverityCountDto>>` |
| `Program.cs` | реєстрація `SeverityBreakdownQuery` у DI як scoped |
| `Client/index.html`, `Client/app.js`, `Client/styles.css` | панель «Підсумок за критичністю»: вибір статусу, кнопка, стани завантаження, «Даних немає», помилки |
| `tests/SecureLab.Api.Tests/SeverityBreakdownTests.cs` | 4 `[Fact]` + `[Theory]` на 4 значення = 8 випадків |
| `tests/http/incidents.http` | коментар 501 замінено на 200, додано сценарії фільтра, порожнього і невалідного запиту |
| `docs/architecture.md`, `docs/report-template.md`, `README.md` | документація |

## 4. Перевірка

Порт PostgreSQL на моєму ПК: 54331 (через `infra/.env`), бо 54329 був зайнятий іншим локальним сервісом.

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, БД доступна | 200, `{"status":"ready"}` | curl.exe -i |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, лише Triaged | 200, один інцидент «Підозрілий лист із вкладенням», Medium | curl.exe -i |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200, `[]` | 200, `[]` | `.http`, curl.exe -i |
| T-04 | відновлений seed | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | 404, `application/problem+json`, title «Інцидент не знайдено» | curl.exe -i, тест |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | 400, `errors.status` | curl.exe -i |
| T-06 | етап 3, seed | `GET /api/incidents/severity-summary` | 200, Low 1, Medium 1, High 1, Critical 0, саме в цьому порядку | 200, `[Low 1, Medium 1, High 1, Critical 0]` | тест `Seed_GivesAllFourLevels_...`, curl.exe -i |
| T-07 | API і клієнт запущено | кнопка «Порахувати» | запит GET summary, показ через textContent, стани завантаження, порожньо, помилка | «Завантаження…», потім `Low: 1`, `Medium: 1`, `High: 1`, `Critical: 0`; Resolved дає «Даних немає»; при зупиненому API «Не вдалося отримати підсумок» | Network, скриншоти |
| T-08 | після експериментів | `--reset-database`, повтор T-02 і T-06 | той самий стан | reset завершився з кодом 0, T-02 і T-06 повторились без змін | вивід reset |
| T-09 | етап 3 | summary з `status=triaged`, `2`, `New,Triaged` | 400 для кожного | 400 для кожного | тест `StatusOutsideAllowlist_...`, curl.exe -i |

Автоматична перевірка: `dotnet test ... --configuration Release`: 12 тестів, 12 пройшли (4 базові + 8 нових).

## 5. Security-сценарій

Навмисно вразливого стану в ЛР 1 немає. Окремо перевірено фільтр `status`: без заборони цифр і коми
`Enum.TryParse` + `Enum.IsDefined` пропускали `3` і `New,Triaged` (тест падав на двох випадках із чотирьох).
Після додавання заборони всі чотири випадки дають 400. Залишковий ризик: готовий фільтр списку
`GET /api/incidents?status=2` у starter досі повертає 200 (2 трактується як InProgress); його я не змінювала,
бо це поза межами ЛР 1.

## 6. Висновок

Підсумок за критичністю працює від кнопки до таблиці і назад. API повертає чотири рівні в сталому
порядку, зайвих полів немає, невалідний статус дає 400. Усі 12 тестів зелені, секретів у diff немає.
