const loginForm = document.querySelector("#loginForm");
const statusBox = document.querySelector("#status");

function setStatus(message, kind = "") {
  statusBox.textContent = message;
  statusBox.className = `status ${kind}`.trim();
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  const data = await response.json().catch(() => ({}));

  if (!response.ok) {
    throw new Error(data.message || "请求失败");
  }

  return data;
}

function jumpByRole(session) {
  window.location.replace(session.isAdmin ? "/records.html" : "/");
}

async function ensureGuestPage() {
  const response = await fetch("/api/auth/me");
  if (!response.ok) {
    return;
  }

  const session = await response.json();
  jumpByRole(session);
}

loginForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const button = loginForm.querySelector("button");
  const formData = new FormData(loginForm);

  button.disabled = true;
  button.textContent = "登录中";
  setStatus("正在登录...");

  try {
    const session = await fetchJson("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        username: formData.get("username"),
        password: formData.get("password")
      })
    });

    jumpByRole(session);
  } catch (error) {
    setStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "登录";
  }
});

ensureGuestPage();
