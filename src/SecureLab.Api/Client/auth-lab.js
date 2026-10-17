// Браузерний клієнт ЛР 3. Cookie сеансу ставить і читає браузер; цей код її не бачить (HttpOnly).
// Пароль живе лише в полі форми і в тілі одного запиту, після спроби поле очищується.
// Нічого не записується в localStorage, sessionStorage, dataset чи Console.

const $ = selector => document.querySelector(selector);

// fetch лише на свій origin і з cookie цього ж origin.
async function callApi(path, options = {}) {
  const target = new URL(path, location.origin);
  if (target.origin !== location.origin) {
    throw new Error("Дозволено лише адреси цього застосунку.");
  }
  const response = await fetch(target, { ...options, credentials: "same-origin" });
  note(`${options.method ?? "GET"} ${target.pathname}`, response.status);
  return response;
}

function postJson(path, payload) {
  return callApi(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
}

function note(what, status) {
  const item = document.createElement("li");
  item.textContent = `${new Date().toLocaleTimeString("uk-UA")}  ${what}  ->  ${status}`;
  $("#request-journal").prepend(item);
}

function say(id, text) {
  $(id).textContent = text;
}

function isJson(response) {
  return (response.headers.get("Content-Type") ?? "").startsWith("application/json");
}

// Стан і профіль показуються лише з відповіді сервера на /api/me.
function showAnonymous() {
  $("#session-box").dataset.state = "anonymous";
  say("#session-text", "ви не ввійшли");
  $("#profile-card").hidden = true;
  for (const id of ["#profile-id", "#profile-name", "#profile-roles"]) say(id, "");
}

function showSignedIn(profile) {
  $("#session-box").dataset.state = "signed-in";
  say("#session-text", `активний, користувач ${profile.userName}`);
  say("#profile-id", profile.userId);
  say("#profile-name", profile.userName);
  say("#profile-roles", profile.roles.join(", "));
  $("#profile-card").hidden = false;
}

function showUnknown(status) {
  $("#session-box").dataset.state = "unknown";
  say("#session-text", `невідомий (відповідь ${status})`);
  $("#profile-card").hidden = true;
}

async function refreshSession() {
  let response;
  try {
    response = await callApi("/api/me");
  } catch {
    showUnknown("мережа");
    return;
  }
  if (response.status === 401) {
    showAnonymous();
  } else if (response.ok && isJson(response)) {
    showSignedIn(await response.json());
  } else {
    showUnknown(response.status);
  }
}

// Реєстрація: 201 не означає входу, тому далі пропоную увійти.
$("#signup-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const payload = {
    userName: data.get("userName"),
    email: data.get("email"),
    displayName: data.get("displayName"),
    password: data.get("password")
  };
  form.elements.password.value = "";
  const response = await postJson("/api/auth/register", payload);
  if (response.status === 201) {
    const created = await response.json();
    say("#signup-note", `Обліковий запис ${created.userName} створено. Тепер увійдіть.`);
    form.reset();
  } else if (response.status === 409) {
    say("#signup-note", "Таке ім'я користувача або email уже зайняте.");
  } else if (response.status === 400) {
    const problem = await response.json();
    say("#signup-note", Object.values(problem.errors ?? {}).flat().join(" ") || "Перевірте поля форми.");
  } else {
    say("#signup-note", `Реєстрацію не завершено (${response.status}).`);
  }
});

$("#signin-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const payload = { userName: data.get("userName"), password: data.get("password") };
  form.elements.password.value = "";
  const response = await postJson("/api/auth/login", payload);
  if (response.status === 204) {
    say("#signin-note", "Вхід виконано.");
  } else if (response.status === 429) {
    say("#signin-note", "Забагато спроб. Зачекайте і спробуйте ще раз.");
  } else {
    say("#signin-note", "Не вдалося увійти. Перевірте ім'я і пароль.");
  }
  await refreshSession();
});

// Інцидент: лише чотири поля контракту. Власника, статус і id клієнт не надсилає.
$("#incident-form").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = new FormData(form);
  const payload = {
    title: data.get("title"),
    description: data.get("description"),
    severity: data.get("severity"),
    occurredAtUtc: new Date(data.get("occurredAt")).toISOString()
  };
  const response = await postJson("/api/incidents", payload);
  if (response.status === 201) {
    const created = await response.json();
    say("#incident-note", `Створено: ${created.title} (${created.severity}, ${created.status}).`);
    form.reset();
  } else if (response.status === 401) {
    say("#incident-note", "Спершу увійдіть.");
    showAnonymous();
  } else if (response.status === 400 && isJson(response)) {
    const problem = await response.json();
    say("#incident-note", Object.values(problem.errors ?? {}).flat().join(" "));
  } else {
    say("#incident-note", `Інцидент не створено (${response.status}).`);
  }
});

// Вихід: спершу серверний sign-out, потім очищення сторінки і повторна перевірка /api/me.
$("#signout-button").addEventListener("click", async () => {
  const response = await callApi("/api/auth/logout", { method: "POST" });
  for (const id of ["#signin-note", "#signup-note", "#incident-note"]) say(id, "");
  $("#incident-form").reset();
  say("#signin-note", response.status === 204 ? "Сеанс завершено на сервері." : `Вихід: ${response.status}`);
  await refreshSession();
});

refreshSession();
