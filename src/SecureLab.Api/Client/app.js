const listElement = document.querySelector("#incident-list");
const listStatusElement = document.querySelector("#list-status");
const detailsElement = document.querySelector("#incident-details");
const filterForm = document.querySelector("#filter-form");
const breakdownForm = document.querySelector("#breakdown-form");
const breakdownStatusElement = document.querySelector("#breakdown-status");
const breakdownListElement = document.querySelector("#breakdown-list");

async function apiFetch(path, options = {}) {
  const response = await fetch(path, {
    headers: { Accept: "application/json", ...options.headers },
    ...options,
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.title ?? `HTTP ${response.status}`);
  }

  return response.json();
}

function createTextElement(tagName, text, className) {
  const element = document.createElement(tagName);
  element.textContent = text;
  if (className) {
    element.className = className;
  }
  return element;
}

function renderIncidentList(incidents) {
  listElement.replaceChildren();

  if (incidents.length === 0) {
    listStatusElement.textContent = "За заданим фільтром інцидентів немає.";
    return;
  }

  listStatusElement.textContent = `Знайдено: ${incidents.length}`;
  for (const incident of incidents) {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "incident-card";
    button.append(
      createTextElement("strong", incident.title),
      createTextElement("span", `${incident.severity} · ${incident.status}`, "metadata"),
    );
    button.addEventListener("click", () => loadIncidentDetails(incident.id));
    item.append(button);
    listElement.append(item);
  }
}

function renderIncidentDetails(incident) {
  const heading = createTextElement("h3", incident.title);
  const metadata = createTextElement(
    "p",
    `${incident.severity} · ${incident.status} · автор: ${incident.ownerDisplayName}`,
    "metadata",
  );
  const description = createTextElement("p", incident.description);
  const commentsHeading = createTextElement("h4", "Коментарі");
  const comments = document.createElement("ul");

  for (const comment of incident.comments) {
    const item = document.createElement("li");
    item.append(
      createTextElement("strong", `${comment.authorDisplayName}: `),
      document.createTextNode(comment.text),
    );
    comments.append(item);
  }

  if (incident.comments.length === 0) {
    comments.append(createTextElement("li", "Коментарів немає."));
  }

  detailsElement.className = "";
  detailsElement.replaceChildren(heading, metadata, description, commentsHeading, comments);
}

async function loadIncidents() {
  listStatusElement.textContent = "Завантаження…";
  listElement.replaceChildren();

  const status = new FormData(filterForm).get("status");
  const query = status ? `?status=${encodeURIComponent(status)}` : "";

  try {
    renderIncidentList(await apiFetch(`/api/incidents${query}`));
  } catch (error) {
    listStatusElement.textContent = `Помилка: ${error.message}`;
  }
}

async function loadIncidentDetails(id) {
  detailsElement.className = "details-placeholder";
  detailsElement.textContent = "Завантаження…";

  try {
    renderIncidentDetails(await apiFetch(`/api/incidents/${encodeURIComponent(id)}`));
  } catch (error) {
    detailsElement.textContent = `Помилка: ${error.message}`;
  }
}

// Підсумок за критичністю: GET /api/incidents/severity-summary[?status=...]
async function loadSeverityBreakdown() {
  breakdownStatusElement.textContent = "Завантаження…";
  breakdownListElement.replaceChildren();

  const status = new FormData(breakdownForm).get("status");
  const query = status ? `?status=${encodeURIComponent(status)}` : "";

  try {
    const levels = await apiFetch(`/api/incidents/severity-summary${query}`);
    const total = levels.reduce((sum, level) => sum + level.count, 0);

    // Сервер завжди повертає всі рівні, тому "порожньо" означає нуль інцидентів загалом.
    if (levels.length === 0 || total === 0) {
      breakdownStatusElement.textContent = "Даних немає: за цими умовами інцидентів не знайдено.";
      return;
    }

    breakdownStatusElement.textContent = `Усього інцидентів: ${total}`;
    for (const level of levels) {
      const row = document.createElement("li");
      row.textContent = `${level.severity}: ${level.count}`;
      if (level.count === 0) {
        row.className = "breakdown-zero";
      }
      breakdownListElement.append(row);
    }
  } catch {
    // Фіксований текст: без stack trace, SQL і деталей сервера.
    breakdownStatusElement.textContent = "Не вдалося отримати підсумок. Спробуйте ще раз.";
  }
}

breakdownForm.addEventListener("submit", (event) => {
  event.preventDefault();
  loadSeverityBreakdown();
});

filterForm.addEventListener("submit", (event) => {
  event.preventDefault();
  loadIncidents();
});

loadIncidents();
