# Матриця доступу: Incident, baseline 2-A (ЛР 4, рівень «Відмінно»)

Allow означає право конкретного користувача на конкретну дію з конкретним записом, а не просто вхід.
Роль і id беруться лише з principal, який сформувала cookie після входу (`CallerProfile.TryRead`).
Усі рішення для одного запису ухвалює `Application/Access/IncidentAccessGuard.cs`, переліки обмежує
`Application/Access/IncidentScope.cs`. Анонім на всіх маршрутах нижче отримує 401 Problem Details без redirect.

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник ресурсу має значення? | Стан реалізації |
|---|---|---:|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | лише власні записи | усі записи (потрібно, щоб знайти New для triage) | лише власні записи, як Reporter | Так для Reporter і Administrator | `IncidentScope.VisibleTo` у `IncidentQueries.GetListAsync`; тести `ReadPathScopeTests.List_*`, `IncidentEndpointTests.GetList_ForAlice_*` |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | лише власні записи | усі записи | лише власні записи | Так для Reporter і Administrator | `IncidentScope` до тексту, сортування і `Take(50)` у `IncidentSearchQuery`; тести `ReadPathScopeTests.Search_*` |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | лише власні записи, усі чотири рівні з нулями | усі записи | лише власні записи (у seed усі нулі) | Так для Reporter і Administrator | `IncidentScope` до `GroupBy` у `SeverityBreakdownQuery`; тести `ReadPathScopeTests.SeveritySummary_*` |
| Перегляд деталей `GET /api/incidents/{id}` | достатній, вхідний контракт ЛР 5 | deny, 401 | owner: allow; чужий: deny, 403; неіснуючий: 404 | allow для аналізу будь-якого інциденту; внутрішні нотатки видно | owner: allow; чужий: deny, 403 | Так | `IncidentAction.ReadDetails`; тести `OwnershipBoundaryTests` |
| Створення `POST /api/incidents` | успадкований baseline | deny, 401 | allow, власник = id з principal | allow, власник = id з principal | allow, власник = id з principal | Сервер призначає власника | ЛР 3, `OwnerFromPrincipalTests`; CSRF-токен обов'язковий |
| Update content `PATCH /api/incidents/{id}` (головна action) | достатній; периметр добрий | deny, 401 | owner: allow лише title і description; чужий: deny, 403 | deny, 403 (право читати не дає права писати); власний запис: allow | owner: allow; чужий: deny, 403 | Так | `IncidentAction.EditContent`; зайве поле в JSON: 400 до коду endpoint; тести `ForeignIncidentChangeTests` |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner і статус New: allow, 204; owner після triage: deny, 403; чужий: deny, 403 | deny, 403 (видалення не входить в обов'язок аналітика) | owner і New: allow; інакше deny, 403 | Так | `IncidentAction.Remove`; тести `ForeignIncidentChangeTests.*Delete*` |
| Зміна severity/status: `POST /api/incidents/{id}/triage` | добрий | deny, 401 | deny, 403, навіть для власного New | allow лише для статусу New; інший статус: 409; наступний статус Triaged ставить сервер | deny, 403 (предметне правило не дає Administrator triage) | Ні: вирішують роль і поточний статус | `IncidentAction.Triage` + перевірка статусу в `IncidentChangeService.TriageAsync`; подія `authorization.denied`; тести `TriageRoleTests` |
| Зміна severity | добрий | deny, 401 | deny: поля немає в `UpdateIncidentRequest`, спроба дає 400 | окремого маршруту немає | deny | Ні | Окремої дії для severity не створено, поле відхиляється контрактом (A-08b) |
| Перегляд коментаря `GET /api/incident-comments/{id}` | добрий | deny, 401 | allow лише для публічного коментаря до власного інциденту; чужий parent: 403 | allow після перевірки parent | як Reporter | Так для parent | `IncidentAccessGuard.DecideComment`; тести `ChildResourceTests` |
| Перегляд internal comment | добрий | deny, 401 | deny, 403 за прямим id; у details не показується навіть власнику | allow після авторизації для parent incident (і за прямим id, і в details) | deny, 403 | Ні для internal rule; доступ до parent перевіряється окремо | `CanSeeInternalNotes` у details-проєкції і в `DecideComment`; тести `ChildResourceTests` |
| Export/audit | відмінний, якщо flow існує | не застосовується | не застосовується | не застосовується | не застосовується | Не застосовується | Такого маршруту у проєкті немає |

Child resource, через який можна непрямо отримати дані інциденту: `IncidentComment` (маршрут
`GET /api/incident-comments/{id}` і масив `comments` у details). `IncidentStatusHistory` назовні не віддається.

## Політика відмов: 403 для чужого, 404 для неіснуючого

- Чужий запис, який існує: **403** з title «Доступ до інциденту заборонено». Відповідь однакова для
  всіх причин відмови і не містить жодного поля чужого запису.
- Запис, якого немає: **404** «Інцидент не знайдено» (для коментаря «Коментар не знайдено»).
- Конфлікт стану при дозволеній ролі (triage не з New): **409**. Це не відмова в доступі, а правило переходу.
- Анонім: **401** ще до коду endpoint (`RequireAuthorization`).

Чому 403, а не 404. Трекер внутрішній: усі користувачі є працівниками однієї організації, а id інцидентів
бачать аналітики і пересилають у листах. Факт існування запису тут не секрет, а от його зміст і право
змінювати є. 403 чесно каже Reporter-у «запис є, але не ваш», а підтримка і аудит бачать саме відмову в
доступі, а не загадкове «не знайдено». Відмова в мутації ще й записується в журнал з тим самим кодом.

Ціна вибору: за відповіддю можна дізнатися, чи існує id. Для UUID v4 перебір нереальний, але seed-id
навчального стенда передбачувані. Тому в тілі 403 немає ні назви, ні власника, ні статусу. A-10 (однакові
відповіді для чужого й неіснуючого) за цієї політики не застосовується.

## Least privilege: чому в кожної ролі саме ці права

- **Reporter** повідомляє про інцидент і доповнює власний опис. Чужі записи йому для роботи не потрібні,
  тому ні читання, ні зміни чужого. Видалення лише поки запис New: після triage він став частиною розслідування.
- **Analyst** розбирає всі нові повідомлення, тому читає всі записи і бачить внутрішні нотатки. Але його
  обов'язок обмежений переходом New -> Triaged. Редагувати чужий текст і видаляти він не може: інакше
  аналітик міг би непомітно переписати свідчення того, хто повідомив.
- **Administrator** у картці 2-A має «лише окремо визначені адміністративні операції». Для інцидентів
  жодної такої операції не опубліковано, тому роль не додає прав: адміністратор працює як звичайний автор.
  Обхід ownership за назвою ролі був би правом «на все», якого правило не дає.

## Що охоплено і що лишається

Охоплено всі маршрути, які приймають id інциденту прямо (details, PATCH, DELETE, triage) або непрямо
(коментар), і всі переліки (list, search, severity-summary). Batch- і export-маршрутів у проєкті немає.

Залишкові ризики:
- одночасні triage двох аналітиків: обидва прочитають New; захисту версії рядка немає (сценарію від
  викладача не було);
- 403 підтверджує існування id (свідомий компроміс, див. вище);
- роль записана в cookie під час входу: зняття ролі Analyst діє лише після повторного входу або зміни
  security stamp;
- CSRF: токен з ЛР 3 тепер захищає й PATCH, DELETE і triage, але повний огляд усіх форм лишається темою ЛР 6.
